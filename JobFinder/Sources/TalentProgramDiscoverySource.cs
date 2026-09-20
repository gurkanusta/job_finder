using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Otomatik yetenek/staj programı keşif kaynağı.
/// Web araması yaparak şirketlerin "genç yetenek", "yetenek programı", "staj programı" sayfalarını bulur.
/// 
/// Çalışma prensibi:
/// 1. config.json'daki talentDiscovery.keywords ile arama yapar (ör. "genç yetenek programı site:kariyer.*")
/// 2. Sonuç URL'lerini ziyaret eder ve sayfa başlığı/meta description'dan ilan oluşturur
/// 3. JobMatcher ile junior//backend filtresinden geçirir
/// 
/// Not: GitHub Actions datacenter IP'si arama motorları tarafından rate-limitlenebilir.
/// Bu yüzden her adım loglanır, sessiz başarısızlık yok.
/// </summary>
public sealed class TalentProgramDiscoverySource : IJobSource
{
    private readonly HttpClient _http;
    private readonly TalentDiscoveryConfig _cfg;

    public string Name => "talent-discovery";

    public TalentProgramDiscoverySource(HttpClient http, TalentDiscoveryConfig cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        if (_cfg.Keywords.Count == 0)
        {
            Console.Error.WriteLine($"[{Name}] keywords boş — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in _cfg.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            var added = 0;

            try
            {
                for (int page = 1; page <= _cfg.MaxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();

                    // DuckDuckGo HTML (JS'siz, bot-dostu) yerine Bing/Google HTML kullanıyoruz
                    // Burada basit bir search engine HTML'si çekiyoruz
                    var searchUrls = BuildSearchUrls(keyword, page);

                    foreach (var searchUrl in searchUrls)
                    {
                        var urls = await ExtractResultUrlsAsync(searchUrl, ct);
                        foreach (var url in urls)
                        {
                            if (!seen.Add(url)) continue;

                            var postings = await FetchAndParsePageAsync(url, ct);
                            foreach (var p in postings)
                            {
                                if (seen.Add(p.DedupKey)) result.Add(p);
                                added++;
                            }
                        }

                        // Rate limit için kısa bekle
                        await Task.Delay(_cfg.DelayMs, ct);
                    }

                    if (added == 0) break; // yeni sonuç yok
                }

                Console.WriteLine($"[{Name}] '{keyword}': {added} yeni ilan.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{Name}] '{keyword}' HATA: {ex.Message}");
            }
        }

        Console.WriteLine($"[{Name}] toplam {result.Count} benzersiz ilan.");
        return result;
    }

    private List<string> BuildSearchUrls(string keyword, int page)
    {
        var urls = new List<string>();
        var encoded = Uri.EscapeDataString(keyword);

        // DuckDuckGo HTML (lite) - en basit ve en az bloklanma riski
        // https://duckduckgo.com/html/?q=...&s=... (sayfa başlangıcı)
        // s parametresi: 1. sayfa = 0, 2. sayfa = 50, 3. sayfa = 100...
        var start = (page - 1) * 50;
        urls.Add($"https://duckduckgo.com/html/?q={encoded}&s={start}&kl=tr-tr");

        return urls;
    }

    private async Task<List<string>> ExtractResultUrlsAsync(string searchUrl, CancellationToken ct)
    {
        var urls = new List<string>();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[{Name}] Arama HTTP {(int)resp.StatusCode}: {searchUrl}");
                return urls;
            }

            var html = await resp.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // DEBUG: Log HTML length
            Console.WriteLine($"[{Name}] Search HTML length: {html.Length}");

            // DuckDuckGo result linkleri: <a class="result__snippet" ...> veya <a class="result__url" ...>
            // Daha güvenli: result snippets içindeki linkleri al
            var links = doc.DocumentNode.SelectNodes("//a[@class='result__snippet' or @class='result__url' or contains(@class, 'result__')]");

            if (links != null)
            {
                Console.WriteLine($"[{Name}] Found {links.Count} candidate links");
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(href) && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        // Sadece kariyer/yetene/staj sayfalarını filtrele
                        if (IsRelevantUrl(href))
                            urls.Add(href);
                    }
                }
            }
            else
            {
                Console.WriteLine($"[{Name}] No links found with primary selector, trying fallback...");
                // Fallback: tüm linkleri dene
                var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                if (allLinks != null)
                {
                    Console.WriteLine($"[{Name}] Fallback: {allLinks.Count} total links");
                    foreach (var link in allLinks)
                    {
                        var href = link.GetAttributeValue("href", "");
                        if (!string.IsNullOrWhiteSpace(href) && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsRelevantUrl(href))
                                urls.Add(href);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] URL çıkarma hatası: {ex.Message}");
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool IsRelevantUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        // Sadece bilinen kariyer platformları ve şirket kariyer sayfaları
        var allowedDomains = new[]
        {
            "kariyer.", "careers.", "jobs.", "is-ilani", "ilan.", "staj", "yetenek",
            "talent", "genclik", "genç", "young", "graduate", "intern", "trainee",
            "youthall.com", "anbeankampus.co", "toptalent.co", "iskur.gov.tr",
            "lever.co", "greenhouse.io", "bamboohr.com", "workable.com"
        };

        // Engellenen: sadece ana sayfa, giriş sayfası, blog vs.
        var blockedPatterns = new[]
        {
            "/login", "/giris", "/signin", "/register", "/kayit", "/blog", "/hakkinda",
            "/about", "/iletisim", "/contact", "/privacy", "/kvkk", "/cookie"
        };

        if (blockedPatterns.Any(p => lower.Contains(p))) return false;
        return allowedDomains.Any(d => lower.Contains(d));
    }

    private async Task<List<JobPosting>> FetchAndParsePageAsync(string url, CancellationToken ct)
    {
        var postings = new List<JobPosting>();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");
            req.Headers.Referrer = new Uri(new Uri(url).GetLeftPart(UriPartial.Authority));

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return postings;

            var html = await resp.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Title ve meta description çek
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode != null ? HtmlEntity.DeEntitize(titleNode.InnerText).Trim() : "";

            var metaDesc = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
            var description = metaDesc != null ? HtmlEntity.DeEntitize(metaDesc.GetAttributeValue("content", "")).Trim() : "";

            // İlk anlamlı h1/h2 de title olabilir
            if (string.IsNullOrWhiteSpace(title) || title.Length < 5)
            {
                var h1 = doc.DocumentNode.SelectSingleNode("//h1");
                if (h1 != null)
                    title = HtmlEntity.DeEntitize(h1.InnerText).Trim();
            }

            // Sadece ilgili anahtar kelimeler geçen sayfaları al
            var combined = $"{title} {description}".ToLowerInvariant();
            var relevantKeywords = new[] { "genç yetenek", "genc yetenek", "yetenek program", "staj program", "yaz staj", "talent program", "internship", "graduate program", "trainee", "yeni mezun", "mt program", "management trainee" };

            if (!relevantKeywords.Any(k => combined.Contains(k))) return postings;

            // Domain'ten site adı çıkar
            var host = new Uri(url).Host.Replace("www.", "");
            var siteName = host.Split('.')[0];

            postings.Add(new JobPosting
            {
                Source = $"TalentDiscovery:{siteName}",
                Title = title,
                Description = description,
                Url = url,
                // CrossSourceKey yok; DedupKey URL'den oluşur
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] Sayfa parse hatası ({url}): {ex.Message}");
        }

        return postings;
    }
}