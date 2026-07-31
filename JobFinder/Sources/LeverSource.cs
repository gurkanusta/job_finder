using System.Text.Json;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Lever Postings API'sinden ilanları çeker (resmi, herkese açık JSON):
///   GET https://api.lever.co/v0/postings/{company}?mode=json
/// Config'teki ats == "lever" olan şirketler için çalışır.
/// </summary>
public sealed class LeverSource : IJobSource
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<CompanyEntry> _companies;
    private readonly IReadOnlyList<string> _locationAllow;

    public string Name => "lever";

    public LeverSource(HttpClient http, IEnumerable<CompanyEntry> companies, AtsConfig? ats = null)
    {
        _http = http;
        _companies = companies.Where(c =>
            string.Equals(c.Ats, "lever", StringComparison.OrdinalIgnoreCase)).ToList();
        _locationAllow = ats?.LocationAllow ?? new List<string>();
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        var result = new List<JobPosting>();
        foreach (var company in _companies)
        {
            try
            {
                var url = $"https://api.lever.co/v0/postings/{company.Token}?mode=json";
                var json = await _http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                int count = 0, skipped = 0;
                foreach (var job in doc.RootElement.EnumerateArray())
                {
                    count++;
                    var title = job.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    var hostedUrl = job.TryGetProperty("hostedUrl", out var u) ? u.GetString() ?? "" : "";
                    var jobId = job.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var location = job.TryGetProperty("categories", out var cats)
                                   && cats.TryGetProperty("location", out var l)
                        ? l.GetString() ?? "" : "";
                    DateTimeOffset? created = job.TryGetProperty("createdAt", out var ca)
                                              && ca.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ca.GetInt64()) : null;

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(hostedUrl)) continue;

                    // Lever board'ları uluslararası — Türkiye dışını burada ele.
                    if (!AtsLocationFilter.IsAllowed(location, company, _locationAllow)) { skipped++; continue; }

                    result.Add(new JobPosting
                    {
                        Source = "Lever",
                        Title = title,
                        Company = company.Name,
                        Location = location,
                        Url = hostedUrl,
                        PostedAt = created,
                        DedupKeyOverride = string.IsNullOrEmpty(jobId) ? null : $"lever:{company.Token}:{jobId}",
                    });
                }

                Console.WriteLine($"[lever] {company.Name}: {count} ilan alındı" +
                                  (skipped > 0 ? $" ({skipped} tanesi Türkiye dışı — elendi)" : ""));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[lever] {company.Name} HATA: {ex.Message}");
            }
        }
        return result;
    }
}
