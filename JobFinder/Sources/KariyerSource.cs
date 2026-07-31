using System.Net;
using System.Text.RegularExpressions;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Kariyer.net arama sonuçlarından ilan çeker.
///
/// NEDEN AYRI BİR KAYNAK (genel WebScrapeSource yerine):
///  1) URL biçimi: /is-ilanlari/junior+backend gibi PATH tabanlı adresler aramayı
///     SESSİZCE yok sayıyor — 50 ilan dönüyor ama hepsi alakasız (aşçı, muhasebe,
///     satış danışmanı). Tek çalışan biçim: /is-ilanlari?kw=junior%20backend
///  2) Başlık kalitesi: genel anchor fallback, kartın TÜM metnini başlık sanıyordu
///     ("Junior Back-End Geliştirici Samet Kalıp ve Madeni Eşya San. ve Tic. A.Ş.
///     İstanbul(Asya) İş Yerinde Tam zamanlı update 3 gün"). Bu hem eşleşmeyi bozuyor
///     hem de şirket adında "mimar"/"müdür" geçen ilanları yanlışlıkla eliyordu.
///     Kart HTML'i yapısal: <div data-test="ad-card" positionName="..." cityName="...">
///     içinde <span data-test="ad-card-title"> ve şirket adı <img alt="..."> içinde.
///
/// Sayfa JS ile render edilse de kart verisi sunucudan gelen HTML içinde attribute
/// olarak gömülü olduğu için düz HTTP yeterli. Site yapısı değişirse ilan=0 loglanır.
/// </summary>
public sealed class KariyerSource : IJobSource
{
    private const string SearchBase = "https://www.kariyer.net/is-ilanlari";

    private readonly HttpClient _http;
    private readonly KariyerConfig _cfg;

    public string Name => "kariyer";

    public KariyerSource(HttpClient http, KariyerConfig cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    // Kart bloğu: <div data-test="ad-card" ...attrs...> ... bir sonraki karta kadar
    private static readonly Regex CardRx = new(
        @"<div[^>]*data-test=""ad-card""(?<attrs>[^>]*)>(?<body>.*?)(?=<div[^>]*data-test=""ad-card""|\z)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitleRx = new(
        @"data-test=""ad-card-title""[^>]*>(?<t>.*?)</span>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HrefRx = new(
        @"href=""(?<u>[^""]*/is-ilani/[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompanyRx = new(
        @"data-test=""company-image""[^>]*\salt=""(?<c>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        if (_cfg.Keywords.Count == 0)
        {
            Console.Error.WriteLine("[kariyer] keywords boş — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maxPages = Math.Max(1, _cfg.MaxPages);

        foreach (var keyword in _cfg.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            var added = 0;
            try
            {
                for (int page = 1; page <= maxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = $"{SearchBase}?kw={Uri.EscapeDataString(keyword)}" +
                              (page > 1 ? $"&sayfa={page}" : "");

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
                    req.Headers.Referrer = new Uri("https://www.kariyer.net");

                    using var resp = await _http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine(
                            $"[kariyer] '{keyword}' sayfa {page}: HTTP {(int)resp.StatusCode} " +
                            "(GitHub Actions datacenter IP'si bloklanmış olabilir).");
                        break;
                    }

                    var html = await resp.Content.ReadAsStringAsync(ct);
                    var before = added;
                    foreach (var posting in ParseCards(html))
                    {
                        if (!seen.Add(posting.DedupKey)) continue;
                        result.Add(posting);
                        added++;
                    }
                    // Sayfa yeni ilan getirmediyse sayfalama bitmiştir (ya da tekrar ediyor).
                    if (added == before) break;
                }

                Console.WriteLine($"[kariyer] '{keyword}': {added} yeni ilan.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[kariyer] '{keyword}' HATA: {ex.Message}");
            }
        }

        Console.WriteLine($"[kariyer] toplam {result.Count} benzersiz ilan.");
        return result;
    }

    internal static IEnumerable<JobPosting> ParseCards(string html)
    {
        foreach (Match card in CardRx.Matches(html))
        {
            var attrs = card.Groups["attrs"].Value;
            var body = card.Groups["body"].Value;

            var href = HrefRx.Match(body);
            if (!href.Success) continue;

            // Başlık: önce kartın kendi span'i, olmazsa attribute'taki positionName.
            var title = Clean(TitleRx.Match(body) is { Success: true } m ? m.Groups["t"].Value : "");
            if (string.IsNullOrWhiteSpace(title))
                title = Clean(Attr(attrs, "positionName"));
            if (string.IsNullOrWhiteSpace(title) || title.Length < 4) continue;

            var company = Clean(CompanyRx.Match(body) is { Success: true } c ? c.Groups["c"].Value : "");
            var city = Clean(Attr(attrs, "cityName"));
            var workModel = Clean(Attr(attrs, "workModelText"));   // "Hibrit", "Uzaktan", "İş Yerinde"
            var location = string.Join(", ", new[] { city, workModel }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var url = href.Groups["u"].Value;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://www.kariyer.net" + (url.StartsWith('/') ? url : "/" + url);

            yield return new JobPosting
            {
                Source = "Kariyer.net",
                Title = title,
                Company = company,
                Location = location,
                Url = url,
                // Techcareer aynı ilan id'sini kullanıyor → aynı ilan iki kez gitmesin.
                CrossSourceKey = TechcareerSource.KariyerGroupId(url),
            };
        }
    }

    private static string Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, $@"\b{Regex.Escape(name)}=""(?<v>[^""]*)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["v"].Value : "";
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
