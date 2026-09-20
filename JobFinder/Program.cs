using JobFinder.Config;
using JobFinder.Matching;
using JobFinder.Models;
using JobFinder.Notifications;
using JobFinder.Sources;
using JobFinder.Storage;

// ─── Argümanlar ──────────────────────────────────────────────────────────────
// --source <ad>   sadece belirtilen kaynağı çalıştır (greenhouse, lever, linkedin,
//                 telegram, kariyer, techcareer, coderspace). Virgülle birden fazla.
// --dry-run       Telegram'a mesaj GÖNDERME ve seen-jobs.json'u YAZMA (test için).
// --list-sources  Mevcut kaynakları yazıp çık.
var opts = CliOptions.Parse(args);

var cfg = ConfigLoader.Load();
var matcher = new JobMatcher(cfg.Matching);
using var http = HttpClientFactory.Create();

// ─── Kaynakları kaydet ───────────────────────────────────────────────────────
var allSources = new List<IJobSource>
{
    new GreenhouseSource(http, cfg.Companies, cfg.Ats),
    new LeverSource(http, cfg.Companies, cfg.Ats),
    new TechcareerSource(http, cfg.Techcareer),
    new KariyerSource(http, cfg.Kariyer),
    new LinkedInJobsSource(http, cfg.LinkedInJobs),
    new LinkedInEmailSource(cfg.LinkedIn),
    new TelegramChannelSource(http, cfg.TelegramChannels),
    new TalentProgramDiscoverySource(http, cfg.TalentDiscovery),
};
// Config'teki her scrape hedefi ayrı bir kaynak olur (--source kariyer/techcareer/coderspace).
foreach (var target in cfg.ScrapeTargets)
    allSources.Add(new WebScrapeSource(http, target));

if (opts.ListSources)
{
    Console.WriteLine("Mevcut kaynaklar: " + string.Join(", ", allSources.Select(s => s.Name)));
    return 0;
}

var sources = opts.Sources.Count == 0
    ? allSources
    : allSources.Where(s => opts.Sources.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();

if (sources.Count == 0)
{
    Console.Error.WriteLine($"'{string.Join(",", opts.Sources)}' ile eşleşen kaynak yok. " +
                            $"Geçerli: {string.Join(", ", allSources.Select(s => s.Name))}");
    return 1;
}

Console.WriteLine($"== JobFinder başladı ({DateTimeOffset.Now:yyyy-MM-dd HH:mm}) | " +
                  $"kaynaklar: {string.Join(", ", sources.Select(s => s.Name))} | dryRun={opts.DryRun} ==");

// ─── Kaynakları çalıştır (her biri izole) ────────────────────────────────────
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var collected = new List<JobPosting>();
foreach (var source in sources)
{
    try
    {
        var jobs = await source.FetchAsync(cts.Token);
        Console.WriteLine($"[{source.Name}] toplam {jobs.Count} ham ilan döndü.");
        collected.AddRange(jobs);
    }
    catch (Exception ex)
    {
        // Bir kaynak patlarsa tüm program çökmesin.
        Console.Error.WriteLine($"[{source.Name}] KAYNAK ÇÖKTÜ: {ex.Message}");
    }
}

// ─── Filtrele + tekilleştir ──────────────────────────────────────────────────
// SEEN_JOBS_PATH verildiyse dosya henüz yoksa BİLE onu kullan (oluşturulacak).
// Verilmediyse config.json'un yanındaki seen-jobs.json'u bulmaya çalış.
var seenEnv = Environment.GetEnvironmentVariable("SEEN_JOBS_PATH");
var seenPath = !string.IsNullOrWhiteSpace(seenEnv)
    ? seenEnv
    : ConfigLoader.ResolvePath(null, "seen-jobs.json")
      ?? Path.Combine(Directory.GetCurrentDirectory(), "seen-jobs.json");
var store = SeenJobsStore.Load(seenPath);

// Greenhouse/Lever kendi kaynağı içinde ats.locationAllow ile zaten filtrelendi
// (allJobsInTurkey bypass'ı dahil) — burada tekrar süzülmezler. Techcareer/Kariyer/
// LinkedIn ise Türkiye genelinde anahtar kelime araması yapıyor, şehir filtrelemiyor;
// aynı allow-listesi (artık sadece İstanbul) onlara burada uygulanır.
var locationAllow = cfg.Ats.LocationAllow;
var locationFiltered = collected
    .Where(j => j.Source is "Greenhouse" or "Lever" || AtsLocationFilter.IsAllowed(j.Location, locationAllow))
    .ToList();
var locSkipped = collected.Count - locationFiltered.Count;
if (locSkipped > 0)
    Console.WriteLine($"Lokasyon filtresiyle elenen (İstanbul dışı): {locSkipped}");

var matched = locationFiltered.Where(j => matcher.IsMatch(j)).ToList();
Console.WriteLine($"Eşleşen (junior filtresi geçen): {matched.Count} / {locationFiltered.Count}");

// Aynı tur içinde tekrar edenleri de temizle.
var newJobs = new List<JobPosting>();
var seenThisRun = new HashSet<string>(StringComparer.Ordinal);
foreach (var job in matched)
{
    if (!seenThisRun.Add(job.DedupKey)) continue;
    if (store.IsSeen(job.DedupKey)) continue;
    // Techcareer + Kariyer aynı ilanı farklı URL'lerle veriyor; ortak id varsa
    // ikincisini ele (hem bu turda hem geçmiş turlarda).
    if (job.CrossSourceKey is { } xkey)
    {
        if (!seenThisRun.Add(xkey)) continue;
        if (store.IsSeen(xkey)) continue;
    }
    newJobs.Add(job);
}

Console.WriteLine($"YENİ ilan sayısı: {newJobs.Count}");

// ─── Bildir ──────────────────────────────────────────────────────────────────
var notifier = new TelegramNotifier(http, opts.DryRun);
if (!notifier.IsConfigured && !opts.DryRun)
    Console.Error.WriteLine("[uyarı] TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID yok — mesaj gönderilemeyecek.");

foreach (var job in newJobs)
{
    await notifier.SendJobAsync(job, cts.Token);
    store.MarkSeen(job.DedupKey);
    if (job.CrossSourceKey is { } xkey) store.MarkSeen(xkey);
}

// ─── Kaydet ──────────────────────────────────────────────────────────────────
if (opts.DryRun)
{
    Console.WriteLine("[dry-run] seen-jobs.json yazılmadı.");
}
else
{
    store.Save(cfg.SeenRetentionDays);
    Console.WriteLine($"seen-jobs.json güncellendi ({store.Count} kayıt): {seenPath}");
}

Console.WriteLine("== JobFinder bitti ==");
return 0;

// ─────────────────────────────────────────────────────────────────────────────
sealed class CliOptions
{
    public List<string> Sources { get; } = new();
    public bool DryRun { get; private set; }
    public bool ListSources { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--source" when i + 1 < args.Length:
                    o.Sources.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--dry-run":
                    o.DryRun = true;
                    break;
                case "--list-sources":
                    o.ListSources = true;
                    break;
            }
        }
        return o;
    }
}
