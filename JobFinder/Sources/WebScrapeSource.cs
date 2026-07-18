using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Kariyer.net / Techcareer.net / Coderspace gibi siteler için "best effort" scraper.
/// Her scrapeTarget için bir örnek oluşturulur; Name site adından türetilir
/// (ör. "kariyer", "techcareer", "coderspace") → --source ile ayrı test edilebilir.
///
/// KIRILGANLIK NOTU: Bu siteler anti-bot koruması kullanır ve/veya JS ile render edilir.
/// GitHub Actions'ın datacenter IP'sinden 403/boş dönebilir. Bu yüzden HER denemede
/// status kodu, içerik uzunluğu ve bulunan ilan sayısı loglanır — sessiz başarısızlık yok.
///
/// Çıkarım stratejisi (sırayla dener, ilk sonuç vereni kullanır):
///   1) JSON-LD schema.org/JobPosting  (en standart, en dayanıklı)
///   2) __NEXT_DATA__ / __NUXT__ gömülü JSON içinden başlık+link toplama
///   3) Anchor fallback: site'e özgü job-link regex'i
/// Site yapısı değişirse sessizce hata verir, programı çökertmez.
/// </summary>
public sealed class WebScrapeSource : IJobSource
{
    private readonly HttpClient _http;
    private readonly ScrapeTarget _target;

    public string Name { get; }

    public WebScrapeSource(HttpClient http, ScrapeTarget target)
    {
        _http = http;
        _target = target;
        Name = DeriveName(target.Site);
    }

    public static string DeriveName(string site)
    {
        var s = JobFinder.Matching.JobMatcher.Normalize(site);
        if (s.Contains("kariyer")) return "kariyer";
        if (s.Contains("techcareer")) return "techcareer";
        if (s.Contains("coderspace")) return "coderspace";
        // Genel: harf/rakam dışını at.
        return Regex.Replace(s, "[^a-z0-9]", "");
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_target.Url))
        {
            Console.Error.WriteLine($"[{Name}] URL boş — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _target.Url);
            // Tarayıcıya benzer başlıklar (ToS dostu, saldırgan değil).
            req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
            req.Headers.Referrer = new Uri(new Uri(_target.Url).GetLeftPart(UriPartial.Authority));

            using var resp = await _http.SendAsync(req, ct);
            var html = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"[{Name}] engellenmiş olabilir: HTTP {(int)resp.StatusCode}, uzunluk={html.Length}. " +
                    "(GitHub Actions datacenter IP'si bloklanmış olabilir.)");
                return Array.Empty<JobPosting>();
            }

            var jobs = new List<JobPosting>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRange(IEnumerable<JobPosting> items)
            {
                foreach (var j in items)
                    if (seen.Add(j.DedupKey)) jobs.Add(j);
            }

            AddRange(FromJsonLd(html));
            if (jobs.Count == 0) AddRange(FromAnchors(html));

            var strategy = jobs.Count > 0 ? "" : " (hiç ilan çıkarılamadı — muhtemelen JS-render veya yapı değişikliği)";
            Console.WriteLine($"[{Name}] HTTP {(int)resp.StatusCode}, uzunluk={html.Length}, ilan={jobs.Count}{strategy}");
            return jobs;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] HATA: {ex.Message}");
            return Array.Empty<JobPosting>();
        }
    }

    // ── Strateji 1: JSON-LD (schema.org/JobPosting) ──────────────────────────
    private IEnumerable<JobPosting> FromJsonLd(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null) yield break;

        foreach (var script in scripts)
        {
            var raw = HtmlEntity.DeEntitize(script.InnerText);
            if (string.IsNullOrWhiteSpace(raw)) continue;

            JsonDocument? json = null;
            try { json = JsonDocument.Parse(raw); } catch { continue; }
            using (json)
            {
                foreach (var jp in EnumerateJobPostings(json.RootElement))
                    yield return jp;
            }
        }
    }

    private IEnumerable<JobPosting> EnumerateJobPostings(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                foreach (var jp in EnumerateJobPostings(item))
                    yield return jp;
            yield break;
        }
        if (el.ValueKind != JsonValueKind.Object) yield break;

        // @graph desteği
        if (el.TryGetProperty("@graph", out var graph))
            foreach (var jp in EnumerateJobPostings(graph))
                yield return jp;

        var type = el.TryGetProperty("@type", out var t) ? TypeString(t) : "";
        if (!type.Contains("JobPosting", StringComparison.OrdinalIgnoreCase)) yield break;

        var title = el.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url) && el.TryGetProperty("mainEntityOfPage", out var mep))
            url = mep.ValueKind == JsonValueKind.String ? mep.GetString() ?? "" : "";

        string company = "";
        if (el.TryGetProperty("hiringOrganization", out var org))
            company = org.ValueKind == JsonValueKind.Object && org.TryGetProperty("name", out var on)
                ? on.GetString() ?? "" : (org.ValueKind == JsonValueKind.String ? org.GetString() ?? "" : "");

        string location = "";
        if (el.TryGetProperty("jobLocation", out var loc))
            location = ExtractLocation(loc);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) yield break;

        yield return new JobPosting
        {
            Source = _target.Site,
            Title = title.Trim(),
            Company = company.Trim(),
            Location = location.Trim(),
            Url = AbsolutizeUrl(url),
        };
    }

    private static string TypeString(JsonElement t) =>
        t.ValueKind == JsonValueKind.Array
            ? string.Join(",", t.EnumerateArray().Select(x => x.GetString()))
            : t.GetString() ?? "";

    private static string ExtractLocation(JsonElement loc)
    {
        if (loc.ValueKind == JsonValueKind.Array && loc.GetArrayLength() > 0)
            return ExtractLocation(loc[0]);
        if (loc.ValueKind != JsonValueKind.Object) return "";
        if (loc.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Object)
        {
            var city = addr.TryGetProperty("addressLocality", out var c) ? c.GetString() ?? "" : "";
            var region = addr.TryGetProperty("addressRegion", out var r) ? r.GetString() ?? "" : "";
            return string.Join(", ", new[] { city, region }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        if (loc.TryGetProperty("name", out var n)) return n.GetString() ?? "";
        return "";
    }

    // ── Strateji 2: Anchor fallback ──────────────────────────────────────────
    private IEnumerable<JobPosting> FromAnchors(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null) yield break;

        // Site'e göre ilan detay linki paterni.
        var pattern = Name switch
        {
            "kariyer" => @"/is-ilani/|/ilan/",
            "techcareer" => @"/ilan/|/jobs?/|/is-ilani/",
            "coderspace" => @"/ilan/|/is-ilanlari/",
            _ => @"/(ilan|is-ilani|job|jobs|pozisyon)/",
        };
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);

        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            if (!rx.IsMatch(href)) continue;

            var title = HtmlEntity.DeEntitize(a.GetAttributeValue("title", "") ?? "");
            if (string.IsNullOrWhiteSpace(title))
                title = HtmlEntity.DeEntitize(a.InnerText ?? "");
            title = Regex.Replace(title, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(title) || title.Length < 4) continue;

            yield return new JobPosting
            {
                Source = _target.Site,
                Title = title,
                Url = AbsolutizeUrl(href),
            };
        }
    }

    private string AbsolutizeUrl(string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out _)) return href;
        try
        {
            var baseUri = new Uri(_target.Url);
            return new Uri(baseUri, href).ToString();
        }
        catch { return href; }
    }
}
