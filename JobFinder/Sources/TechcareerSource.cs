using System.Text.Json;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Techcareer.net'in resmi (herkese açık) BFF JSON API'sinden ilan çeker:
///   GET https://www.techcareer.net/api/bff/jobs/job-list
///       ?jobs[search][select]=position
///       &jobs[search][keyword]={kelime}
///       &jobs[isCompleted]=false
///       &jobs[page]={n}
///
/// HTML kazımak yerine bu API kullanılır çünkü site Next.js ile JS-render edilir
/// ve düz HTML çekince ilan listesi güvenilir gelmez. API temiz JSON döner:
///   { "jobs": [ { title, slug, company.companyProfileName, location.locationName } ],
///     "totalCount", "pageCount", ... }
///
/// config.json → techcareer.positionKeywords listesindeki her kelime ayrı sorgu olur.
/// Dönen ilanlar sonra JobMatcher (junior/backend) filtresinden geçer.
/// </summary>
public sealed class TechcareerSource : IJobSource
{
    private const string ApiBase = "https://www.techcareer.net/api/bff/jobs/job-list";
    private const string DetailBase = "https://www.techcareer.net/jobs/detail/";

    private readonly HttpClient _http;
    private readonly TechcareerConfig _cfg;

    public string Name => "techcareer";

    public TechcareerSource(HttpClient http, TechcareerConfig cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        if (_cfg.PositionKeywords.Count == 0)
        {
            Console.Error.WriteLine("[techcareer] positionKeywords boş — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maxPages = Math.Max(1, _cfg.MaxPages);

        // Anahtar kelime araması Techcareer'da çok dar davranıyor: toplam ~190 açık
        // ilanın sadece ~40'ını döndürüyordu. Havuz zaten küçük olduğu için önce
        // TÜM açık ilanları çekip yerelde filtreliyoruz; kelime aramaları bunun
        // üstüne ek kapsama sağlar (havuz sayfalaması ileride kaçırırsa diye).
        await FetchAllPagesAsync(null, _cfg.AllJobsMaxPages, result, seen, ct);

        foreach (var keyword in _cfg.PositionKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            var added = await FetchAllPagesAsync(keyword, maxPages, result, seen, ct);
            Console.WriteLine($"[techcareer] '{keyword}': {added} yeni ilan.");
        }

        Console.WriteLine($"[techcareer] toplam {result.Count} benzersiz ilan.");
        return result;
    }

    /// <summary>
    /// keyword null ise filtresiz "tüm açık ilanlar" listesi çekilir.
    /// Zaten görülmüş slug'lar atlanır; eklenen YENİ ilan sayısını döner.
    /// </summary>
    private async Task<int> FetchAllPagesAsync(
        string? keyword, int maxPages, List<JobPosting> result, HashSet<string> seen, CancellationToken ct)
    {
        var label = keyword ?? "(tüm ilanlar)";
        var added = 0;
        try
        {
            for (int page = 1; page <= maxPages; page++)
            {
                ct.ThrowIfCancellationRequested();
                var search = keyword is null
                    ? ""
                    : $"jobs[search][select]=position&jobs[search][keyword]={Uri.EscapeDataString(keyword)}&";
                var url = $"{ApiBase}?{search}jobs[isCompleted]=false&jobs[page]={page}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                // API JSON bekliyor; tarayıcıya benzer başlıklar engellenme riskini azaltır.
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
                req.Headers.Referrer = new Uri("https://www.techcareer.net/jobs");

                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine(
                        $"[techcareer] '{label}' sayfa {page}: HTTP {(int)resp.StatusCode} " +
                        "(GitHub Actions datacenter IP'si bloklanmış olabilir).");
                    break;
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
                    break;

                var pageCount = root.TryGetProperty("pageCount", out var pc) && pc.ValueKind == JsonValueKind.Number
                    ? pc.GetInt32() : 1;

                foreach (var job in jobs.EnumerateArray())
                {
                    var posting = MapJob(job);
                    if (posting is null) continue;
                    // Aynı ilan birden fazla kelimede/sayfada çıkabilir; slug ile tekilleştir.
                    if (!seen.Add(posting.DedupKey)) continue;
                    result.Add(posting);
                    added++;
                }

                if (page >= pageCount) break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[techcareer] '{label}' HATA: {ex.Message}");
        }
        return added;
    }

    private static JobPosting? MapJob(JsonElement job)
    {
        var title = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var slug = job.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(slug))
            return null;

        var company = "";
        if (job.TryGetProperty("company", out var c) && c.ValueKind == JsonValueKind.Object
            && c.TryGetProperty("companyProfileName", out var cn))
            company = cn.GetString() ?? "";

        var location = "";
        if (job.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.Object
            && l.TryGetProperty("locationName", out var ln))
            location = ln.GetString() ?? "";

        // İlan metni HTML olarak geliyor. Seviye bilgisi ("yeni mezun", "en az 3 yıl")
        // başlıkta değil BURADA olduğu için JobMatcher'a düz metin olarak veriyoruz.
        var description = job.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

        return new JobPosting
        {
            Source = "Techcareer.net",
            Title = title.Trim(),
            Company = company.Trim(),
            Location = location.Trim(),
            Url = DetailBase + slug,
            Description = HtmlToText(description),
            // slug ilan id'sini içerir (ör. backend-developer-4472196) → sağlam dedup anahtarı.
            DedupKeyOverride = $"techcareer:{slug}",
            CrossSourceKey = KariyerGroupId(slug),
        };
    }

    /// <summary>
    /// Slug'ın sonundaki ilan id'si. Kariyer.net aynı id'yi kullandığı için
    /// kaynaklar arası çakışmayı kesmede ortak anahtar olur.
    /// </summary>
    internal static string? KariyerGroupId(string slugOrUrl)
    {
        var m = System.Text.RegularExpressions.Regex.Match(slugOrUrl, @"(\d{5,})\s*/?\s*$");
        return m.Success ? $"kariyergroup:{m.Groups[1].Value}" : null;
    }

    /// <summary>HTML etiketlerini boşluğa çevirip entity'leri çözer (regex eşleşmesi için yeterli).</summary>
    internal static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
