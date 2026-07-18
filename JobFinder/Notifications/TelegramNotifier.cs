using System.Net.Http.Json;
using JobFinder.Models;

namespace JobFinder.Notifications;

/// <summary>
/// Telegram Bot API üzerinden bildirim gönderir. Token ve chat id environment
/// variable'dan okunur (TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID) — asla config'e gömülmez.
/// </summary>
public sealed class TelegramNotifier
{
    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly string? _chatId;
    private readonly bool _dryRun;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_token) && !string.IsNullOrWhiteSpace(_chatId);

    public TelegramNotifier(HttpClient http, bool dryRun)
    {
        _http = http;
        _token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        _chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
        _dryRun = dryRun;
    }

    public async Task SendJobAsync(JobPosting job, CancellationToken ct)
    {
        // Format: [Kaynak] Şirket — Pozisyon, Konum + link
        var header = string.IsNullOrWhiteSpace(job.Company)
            ? $"[{job.Source}] {job.Title}"
            : $"[{job.Source}] {job.Company} — {job.Title}";
        if (!string.IsNullOrWhiteSpace(job.Location))
            header += $", {job.Location}";

        var text = $"{header}\n{job.Url}";

        if (_dryRun || !IsConfigured)
        {
            Console.WriteLine($"[DRY-RUN telegram] {text.Replace("\n", " | ")}");
            return;
        }

        await SendRawAsync(text, ct);
        // Telegram rate limit'ine takılmamak için mesajlar arası küçük bekleme.
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
    }

    public async Task SendRawAsync(string text, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{_token}/sendMessage";
        var payload = new
        {
            chat_id = _chatId,
            text,
            disable_web_page_preview = false,
        };
        using var resp = await _http.PostAsJsonAsync(url, payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"[Telegram] Gönderim başarısız ({(int)resp.StatusCode}): {body}");
        }
    }
}
