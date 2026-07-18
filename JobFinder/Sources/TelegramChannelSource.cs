using System.Text.Json;
using System.Text.RegularExpressions;
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Telegram topluluk kanal/gruplarındaki serbest metin ilan gönderilerini RESMİ Bot API
/// (getUpdates) ile okur — scraping DEĞİL. Bot ilgili kanala üye/admin olmalı ve
/// (gruplar için) privacy mode kapalı olmalı ki mesajları görebilsin.
///
/// TELEGRAM_BOT_TOKEN environment'tan okunur. config.telegramChannels bir allowlist'tir
/// (ör. "@kodluyoruz"); boşsa botun üye olduğu tüm sohbetler kabul edilir.
///
/// Not: getUpdates son ~24 saatlik güncellemeleri döndürür; 3 saatte bir çalıştığımız için
/// örtüşme olur ama dedup (seen-jobs.json'daki telegram:chat:msg anahtarı) tekrarları eler.
/// </summary>
public sealed class TelegramChannelSource : IJobSource
{
    private readonly HttpClient _http;
    private readonly HashSet<string> _allow;

    public string Name => "telegram";

    public TelegramChannelSource(HttpClient http, IEnumerable<string> channels)
    {
        _http = http;
        _allow = new HashSet<string>(
            channels.Select(c => c.TrimStart('@').Trim().ToLowerInvariant())
                    .Where(c => !string.IsNullOrWhiteSpace(c)),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("[telegram] TELEGRAM_BOT_TOKEN yok — atlanıyor.");
            return Array.Empty<JobPosting>();
        }

        var result = new List<JobPosting>();
        try
        {
            // offset commit etmiyoruz (limit=100, sadece okuma). Böylece başka tüketici yoksa
            // güncellemeler 24 saat boyunca okunabilir kalır.
            var url = $"https://api.telegram.org/bot{token}/getUpdates?limit=100&timeout=0" +
                      "&allowed_updates=[\"message\",\"channel_post\"]";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                Console.Error.WriteLine($"[telegram] getUpdates ok=false: {json}");
                return result;
            }

            var updates = doc.RootElement.GetProperty("result");
            int scanned = 0;
            foreach (var upd in updates.EnumerateArray())
            {
                var post = GetPost(upd);
                if (post is null) continue;
                scanned++;

                var value = post.Value;
                var chat = value.GetProperty("chat");
                var chatId = chat.GetProperty("id").GetInt64();
                var username = chat.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "";
                var chatTitle = chat.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";

                // Allowlist filtresi (username ile).
                if (_allow.Count > 0 && !_allow.Contains(username.ToLowerInvariant())) continue;

                var text = value.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                var messageId = value.GetProperty("message_id").GetInt64();
                var link = string.IsNullOrWhiteSpace(username)
                    ? ""
                    : $"https://t.me/{username}/{messageId}";

                // Başlık = gönderi metni (kısaltılmış); global matcher tam metne uygulanır
                // ama link ile kanal adını da göstermek için Title'ı okunur tutuyoruz.
                var snippet = Regex.Replace(text, @"\s+", " ").Trim();
                if (snippet.Length > 200) snippet = snippet[..200] + "…";

                result.Add(new JobPosting
                {
                    Source = "Telegram",
                    Title = snippet,
                    Company = string.IsNullOrWhiteSpace(chatTitle) ? username : chatTitle,
                    Location = "",
                    Url = string.IsNullOrWhiteSpace(link) ? $"tg://chat?id={chatId}&msg={messageId}" : link,
                    DedupKeyOverride = $"telegram:{chatId}:{messageId}",
                });
            }

            Console.WriteLine($"[telegram] {scanned} mesaj/gönderi tarandı, {result.Count} aday çıkarıldı.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[telegram] HATA: {ex.Message}");
        }

        return result;
    }

    /// <summary>update içinden message ya da channel_post nesnesini döndürür.</summary>
    private static JsonElement? GetPost(JsonElement upd)
    {
        if (upd.TryGetProperty("channel_post", out var cp)) return cp;
        if (upd.TryGetProperty("message", out var m)) return m;
        return null;
    }
}
