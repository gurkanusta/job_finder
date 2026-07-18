using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobFinder.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace JobFinder.Sources;

/// <summary>
/// LinkedIn'i SCRAPE ETMEZ. Bunun yerine kullanıcının kurduğu native "Job Alert"
/// maillerini Gmail'den IMAP ile okur ve mail body'sindeki TÜM ilan linklerini çıkarır.
///
/// Kimlik bilgileri environment'tan gelir (config'e/koda gömülmez):
///   GMAIL_IMAP_USER      → Gmail adresi
///   GMAIL_APP_PASSWORD   → Gmail App Password (2FA açıkken oluşturulur)
/// </summary>
public sealed class LinkedInEmailSource : IJobSource
{
    private readonly LinkedInConfig _cfg;
    // LinkedIn ilan linkleri: .../jobs/view/1234567890/ (comm/ prefix'li de olabilir)
    private static readonly Regex JobIdRegex =
        new(@"/jobs/view/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Name => "linkedin";

    public LinkedInEmailSource(LinkedInConfig cfg) => _cfg = cfg;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        var user = Environment.GetEnvironmentVariable("GMAIL_IMAP_USER");
        var pass = Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Console.Error.WriteLine("[linkedin] GMAIL_IMAP_USER / GMAIL_APP_PASSWORD yok — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(_cfg.ImapHost, _cfg.ImapPort, MailKit.Security.SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(user, pass, ct);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var since = DateTime.Now.AddDays(-Math.Max(1, _cfg.LookbackDays));
            var query = SearchQuery.DeliveredAfter(since);
            var uids = await client.Inbox.SearchAsync(query, ct);
            Console.WriteLine($"[linkedin] son {_cfg.LookbackDays} günde {uids.Count} mail tarandı.");

            foreach (var uid in uids)
            {
                ct.ThrowIfCancellationRequested();
                var msg = await client.Inbox.GetMessageAsync(uid, ct);

                // Sadece LinkedIn'den gelen job-alert maillerini işle.
                if (!IsFromLinkedIn(msg)) continue;

                var html = msg.HtmlBody;
                if (string.IsNullOrWhiteSpace(html)) continue;

                foreach (var job in ParseJobs(html, seenIds))
                    result.Add(job);
            }

            await client.DisconnectAsync(true, ct);
            Console.WriteLine($"[linkedin] {result.Count} benzersiz ilan linki çıkarıldı.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[linkedin] HATA: {ex.Message}");
        }

        return result;
    }

    private bool IsFromLinkedIn(MimeMessage msg)
    {
        var needle = _cfg.FromContains;
        if (string.IsNullOrWhiteSpace(needle)) return true;
        foreach (var from in msg.From.Mailboxes)
        {
            if (from.Address?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mail body'sindeki TÜM ilan kartlarını çıkarır (mail birden fazla ilan içerir).
    /// /jobs/view/ içeren tüm anchor'ları tarar; job id ile tekilleştirir.
    /// </summary>
    internal static IEnumerable<JobPosting> ParseJobs(string html, HashSet<string> seenIds)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null) yield break;

        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            var m = JobIdRegex.Match(href);
            if (!m.Success) continue;

            var jobId = m.Groups[1].Value;
            if (!seenIds.Add(jobId)) continue; // aynı mailde/turda tekrar eden linkler

            var title = HtmlEntity.DeEntitize(a.InnerText ?? "").Trim();
            title = Regex.Replace(title, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

            // Tracking'siz kanonik link.
            var cleanUrl = $"https://www.linkedin.com/jobs/view/{jobId}/";

            yield return new JobPosting
            {
                Source = "LinkedIn",
                Title = title,
                Company = "",       // mail body'de güvenilir değil, best-effort boş
                Location = "",
                Url = cleanUrl,
                DedupKeyOverride = $"linkedin:{jobId}",
            };
        }
    }
}
