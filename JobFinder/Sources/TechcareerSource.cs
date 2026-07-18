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

        foreach (var keyword in _cfg.PositionKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            try
            {
                var total = 0;
                for (int page = 1; page <= maxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = $"{ApiBase}?jobs[search][select]=position" +
                              $"&jobs[search][keyword]={Uri.EscapeDataString(keyword)}" +
                              $"&jobs[isCompleted]=false&jobs[page]={page}";

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    // API JSON bekliyor; tarayıcıya benzer başlıklar engellenme riskini azaltır.
                    req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
                    req.Headers.Referrer = new Uri("https://www.techcareer.net/jobs");

                    using var resp = await _http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine(
                            $"[techcareer] '{keyword}' sayfa {page}: HTTP {(int)resp.StatusCode} " +
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
                        total++;
                    }

                    if (page >= pageCount) break;
                }

                Console.WriteLine($"[techcareer] '{keyword}': {total} ilan alındı.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[techcareer] '{keyword}' HATA: {ex.Message}");
            }
        }

        Console.WriteLine($"[techcareer] toplam {result.Count} benzersiz ilan.");
        return result;
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

        return new JobPosting
        {
            Source = "Techcareer.net",
            Title = title.Trim(),
            Company = company.Trim(),
            Location = location.Trim(),
            Url = DetailBase + slug,
            // slug ilan id'sini içerir (ör. backend-developer-4472196) → sağlam dedup anahtarı.
            DedupKeyOverride = $"techcareer:{slug}",
        };
    }
}
