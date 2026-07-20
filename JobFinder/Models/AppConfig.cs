using System.Text.Json.Serialization;

namespace JobFinder.Models;

/// <summary>config.json'un kök nesnesi. Tüm ayarlanabilir davranış burada.</summary>
public sealed class AppConfig
{
    [JsonPropertyName("matching")]
    public MatchingConfig Matching { get; set; } = new();

    [JsonPropertyName("companies")]
    public List<CompanyEntry> Companies { get; set; } = new();

    [JsonPropertyName("scrapeTargets")]
    public List<ScrapeTarget> ScrapeTargets { get; set; } = new();

    /// <summary>Techcareer.net resmi JSON API'si (BFF) üzerinden pozisyon araması.</summary>
    [JsonPropertyName("techcareer")]
    public TechcareerConfig Techcareer { get; set; } = new();

    [JsonPropertyName("telegramChannels")]
    public List<string> TelegramChannels { get; set; } = new();

    [JsonPropertyName("linkedin")]
    public LinkedInConfig LinkedIn { get; set; } = new();

    /// <summary>LinkedIn'in public "guest jobs" API'si üzerinden doğrudan ilan araması (e-posta değil).</summary>
    [JsonPropertyName("linkedinJobs")]
    public LinkedInJobsConfig LinkedInJobs { get; set; } = new();

    /// <summary>seen-jobs.json'da bir anahtarı kaç gün sonra unutalım (dosya şişmesin).</summary>
    [JsonPropertyName("seenRetentionDays")]
    public int SeenRetentionDays { get; set; } = 60;
}

public sealed class MatchingConfig
{
    /// <summary>Seviye kelimeleri: junior, jr, yeni mezun, entry level, graduate...</summary>
    [JsonPropertyName("levelKeywords")]
    public List<string> LevelKeywords { get; set; } = new();

    /// <summary>Rol kelimeleri: backend, yazılım geliştirici, software developer...</summary>
    [JsonPropertyName("roleKeywords")]
    public List<string> RoleKeywords { get; set; } = new();

    /// <summary>Başlıkta geçerse ilan elenir: senior, lead, kıdemli, manager...</summary>
    [JsonPropertyName("excludeKeywords")]
    public List<string> ExcludeKeywords { get; set; } = new();

    /// <summary>
    /// Eşleştirme modu (mode boşsa requireBothGroups'a düşer):
    ///   "strict" = başlıkta HEM seviye HEM rol geçmeli (ör. "Junior Backend"). En az bildirim.
    ///   "role"   = rol geçsin + hariç tutulan (senior/lead/kıdemli) geçmesin YETER; seviye
    ///              şartı yok. Türkiye ilan başlıkları çoğu zaman "junior" yazmadığı için
    ///              gerçek junior ilanlarını da yakalar. ÖNERİLEN.
    ///   "either" = seviye VEYA rol yeterli (en gevşek, en gürültülü).
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    /// <summary>
    /// mode boşken kullanılan eski davranış:
    /// true = seviye VE rol; false = seviye VEYA rol.
    /// </summary>
    [JsonPropertyName("requireBothGroups")]
    public bool RequireBothGroups { get; set; } = true;
}

public sealed class CompanyEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"greenhouse" veya "lever".</summary>
    [JsonPropertyName("ats")]
    public string Ats { get; set; } = "";

    /// <summary>Greenhouse board token'ı ya da Lever şirket slug'ı.</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";
}

public sealed class ScrapeTarget
{
    /// <summary>"Kariyer.net", "Techcareer.net", "Coderspace".</summary>
    [JsonPropertyName("site")]
    public string Site { get; set; } = "";

    /// <summary>Arama sonucu sayfasının tam URL'si.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public sealed class TechcareerConfig
{
    /// <summary>
    /// Pozisyon araması için anahtar kelimeler. Her biri Techcareer'ın BFF API'sine
    /// ayrı bir sorgu olur (select=position). Dönen ilanlar sonra JobMatcher ile
    /// junior/backend filtresinden geçer. Boşsa Techcareer kaynağı atlanır.
    /// </summary>
    [JsonPropertyName("positionKeywords")]
    public List<string> PositionKeywords { get; set; } = new();

    /// <summary>Her anahtar kelime için taranacak sayfa sayısı (20 ilan/sayfa).</summary>
    [JsonPropertyName("maxPages")]
    public int MaxPages { get; set; } = 1;
}

public sealed class LinkedInJobsConfig
{
    /// <summary>
    /// Arama kelimeleri. Her biri LinkedIn guest jobs API'sine ayrı sorgu olur
    /// (keywords=...). Dönen ilanlar sonra JobMatcher (junior) filtresinden geçer.
    /// Boşsa bu kaynak atlanır.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Konum filtresi (LinkedIn location parametresi). Ör. "Turkey", "Istanbul".</summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = "Turkey";

    /// <summary>Her kelime için taranacak sayfa sayısı (~10 ilan/sayfa, start=0,10,20...).</summary>
    [JsonPropertyName("maxPages")]
    public int MaxPages { get; set; } = 2;
}

public sealed class LinkedInConfig
{
    /// <summary>IMAP sunucusu (Gmail için imap.gmail.com).</summary>
    [JsonPropertyName("imapHost")]
    public string ImapHost { get; set; } = "imap.gmail.com";

    [JsonPropertyName("imapPort")]
    public int ImapPort { get; set; } = 993;

    /// <summary>Alert maillerini aramak için gün sayısı (son N gün).</summary>
    [JsonPropertyName("lookbackDays")]
    public int LookbackDays { get; set; } = 2;

    /// <summary>Gönderen filtresi (LinkedIn job alert maillerinin from adresi).</summary>
    [JsonPropertyName("fromContains")]
    public string FromContains { get; set; } = "linkedin.com";
}
