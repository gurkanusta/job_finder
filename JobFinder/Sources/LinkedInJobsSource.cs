using System.Net;
using System.Text.RegularExpressions;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// LinkedIn'in public (login gerektirmeyen) "guest jobs" API'sinden ilan çeker:
///   GET https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search
///       ?keywords={kelime}&location={konum}&start={n}
///
/// Bu bir JSON API değil; her sayfa bir HTML fragmanı (&lt;li&gt; ilan kartları) döner.
/// Kartlardan başlık, şirket, konum ve ilan linki regex ile ayıklanır. Bu, kullanıcının
/// e-postada gördüğü LinkedIn alert'i DEĞİL — doğrudan siteden yapılandırılmış ilan.
///
/// config.json → linkedinJobs.keywords listesindeki her kelime ayrı sorgu olur.
/// Dönen ilanlar sonra JobMatcher (junior) filtresinden geçer.
/// </summary>
public sealed class LinkedInJobsSource : IJobSource
{
    private const string ApiBase =
        "https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search";

    private readonly HttpClient _http;
    private readonly LinkedInJobsConfig _cfg;

    public string Name => "linkedinjobs";

    public LinkedInJobsSource(HttpClient http, LinkedInJobsConfig cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    // Her <li>...</li> bir ilan kartı. İçinden başlık/şirket/konum/link ayıklanır.
    private static readonly Regex CardRx = new(@"<li>.*?</li>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TitleRx = new(@"base-search-card__title""[^>]*>(.*?)</h3>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex CompanyRx = new(@"base-search-card__subtitle""[^>]*>\s*<a[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LocationRx = new(@"job-search-card__location""[^>]*>(.*?)</span>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex UrlRx = new(@"href=""(https://[a-z]+\.linkedin\.com/jobs/view/[^""?]+)", RegexOptions.Compiled);
    // İlan linkinin sonundaki sayısal id sağlam dedup anahtarıdır (ör. .../...-4438714428).
    private static readonly Regex IdRx = new(@"(\d{6,})$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        if (_cfg.Keywords.Count == 0)
        {
            Console.Error.WriteLine("[linkedinjobs] keywords boş — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maxPages = Math.Max(1, _cfg.MaxPages);
        var location = string.IsNullOrWhiteSpace(_cfg.Location) ? "Turkey" : _cfg.Location;

        foreach (var keyword in _cfg.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            try
            {
                var total = 0;
                for (int page = 0; page < maxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var start = page * 10;
                    var url = $"{ApiBase}?keywords={Uri.EscapeDataString(keyword)}" +
                              $"&location={Uri.EscapeDataString(location)}&start={start}";

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,tr;q=0.8");
                    req.Headers.Referrer = new Uri("https://www.linkedin.com/jobs/search");

                    using var resp = await _http.SendAsync(req, ct);
                    // LinkedIn hızlı ardışık isteklerde 429 döner; her istekten sonra biraz bekle.
                    await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine(
                            $"[linkedinjobs] '{keyword}' start={start}: HTTP {(int)resp.StatusCode} " +
                            "(datacenter IP'si rate-limit'lenmiş olabilir).");
                        // 429 ise biraz daha bekleyip bu kelimeyi geç (sonrakiler kurtulabilir).
                        if ((int)resp.StatusCode == 429)
                            await Task.Delay(TimeSpan.FromSeconds(3), ct);
                        break;
                    }

                    var html = await resp.Content.ReadAsStringAsync(ct);
                    var cards = CardRx.Matches(html);
                    if (cards.Count == 0) break; // Daha fazla sonuç yok.

                    foreach (Match card in cards)
                    {
                        var posting = MapCard(card.Value);
                        if (posting is null) continue;
                        if (!seen.Add(posting.DedupKey)) continue; // Aynı ilan başka kelimede de çıkabilir.
                        result.Add(posting);
                        total++;
                    }

                    // LinkedIn tam sayfa dolduramadıysa (10'dan az) sonraki sayfa boş demektir.
                    if (cards.Count < 10) break;
                }

                Console.WriteLine($"[linkedinjobs] '{keyword}': {total} ilan alındı.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[linkedinjobs] '{keyword}' HATA: {ex.Message}");
            }
        }

        Console.WriteLine($"[linkedinjobs] toplam {result.Count} benzersiz ilan.");
        return result;
    }

    private static JobPosting? MapCard(string card)
    {
        var urlM = UrlRx.Match(card);
        if (!urlM.Success) return null;
        var url = WebUtility.HtmlDecode(urlM.Groups[1].Value).Trim();

        var title = Clean(TitleRx.Match(card).Groups[1].Value);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var company = Clean(CompanyRx.Match(card).Groups[1].Value);
        var location = Clean(LocationRx.Match(card).Groups[1].Value);

        var idM = IdRx.Match(url);
        var dedup = idM.Success ? $"linkedin:{idM.Groups[1].Value}" : null;

        return new JobPosting
        {
            Source = "LinkedIn",
            Title = title,
            Company = company,
            Location = location,
            Url = url,
            DedupKeyOverride = dedup,
        };
    }

    /// <summary>HTML etiket kalıntılarını ve entity'leri temizle, boşlukları sadeleştir.</summary>
    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
