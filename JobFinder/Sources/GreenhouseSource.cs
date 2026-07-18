using System.Text.Json;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Greenhouse Boards API'sinden ilanları çeker (resmi, herkese açık JSON):
///   GET https://boards-api.greenhouse.io/v1/boards/{token}/jobs
/// Config'teki ats == "greenhouse" olan şirketler için çalışır.
/// </summary>
public sealed class GreenhouseSource : IJobSource
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<CompanyEntry> _companies;

    public string Name => "greenhouse";

    public GreenhouseSource(HttpClient http, IEnumerable<CompanyEntry> companies)
    {
        _http = http;
        _companies = companies.Where(c =>
            string.Equals(c.Ats, "greenhouse", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        var result = new List<JobPosting>();
        foreach (var company in _companies)
        {
            try
            {
                var url = $"https://boards-api.greenhouse.io/v1/boards/{company.Token}/jobs?content=false";
                var json = await _http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) continue;

                foreach (var job in jobs.EnumerateArray())
                {
                    var title = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var absUrl = job.TryGetProperty("absolute_url", out var u) ? u.GetString() ?? "" : "";
                    var jobId = job.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt64().ToString() : "";
                    var location = job.TryGetProperty("location", out var loc)
                                   && loc.TryGetProperty("name", out var ln)
                        ? ln.GetString() ?? "" : "";
                    DateTimeOffset? updated = job.TryGetProperty("updated_at", out var up)
                                              && up.ValueKind == JsonValueKind.String
                                              && DateTimeOffset.TryParse(up.GetString(), out var d)
                        ? d : null;

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(absUrl)) continue;

                    result.Add(new JobPosting
                    {
                        Source = "Greenhouse",
                        Title = title,
                        Company = company.Name,
                        Location = location,
                        Url = absUrl,
                        PostedAt = updated,
                        // Explicit key: Greenhouse absolute_url'ü bazen ?gh_jid=... query'si taşır;
                        // job id ile tekilleştirmek en güvenlisi.
                        DedupKeyOverride = string.IsNullOrEmpty(jobId) ? null : $"greenhouse:{company.Token}:{jobId}",
                    });
                }

                Console.WriteLine($"[greenhouse] {company.Name}: {jobs.GetArrayLength()} ilan alındı");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[greenhouse] {company.Name} HATA: {ex.Message}");
            }
        }
        return result;
    }
}
