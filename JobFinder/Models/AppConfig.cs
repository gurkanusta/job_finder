using System.Text.Json.Serialization;

namespace JobFinder.Models;

/// <summary>config.json'un kök nesnesi. Tüm ayarlanabilir davranış burada.</summary>
public sealed class AppConfig
{
    [JsonPropertyName("matching")]
    public MatchingConfig Matching { get; set; } = new();

    [JsonPropertyName("companies")]
    public List<CompanyEntry> Companies { get; set; } = new();

    /// <summary>Greenhouse/Lever board'larına özel ayarlar (lokasyon filtresi).</summary>
    [JsonPropertyName("ats")]
    public AtsConfig Ats { get; set; } = new();

    [JsonPropertyName("scrapeTargets")]
    public List<ScrapeTarget> ScrapeTargets { get; set; } = new();

    /// <summary>Techcareer.net resmi JSON API'si (BFF) üzerinden pozisyon araması.</summary>
    [JsonPropertyName("techcareer")]
    public TechcareerConfig Techcareer { get; set; } = new();

    /// <summary>Kariyer.net arama sonuçları (?kw= sorgusu + ad-card parse).</summary>
    [JsonPropertyName("kariyer")]
    public KariyerConfig Kariyer { get; set; } = new();

    [JsonPropertyName("telegramChannels")]
    public List<string> TelegramChannels { get; set; } = new();

    [JsonPropertyName("linkedin")]
    public LinkedInConfig LinkedIn { get; set; } = new();

    /// <summary>LinkedIn'in public "guest jobs" API'si üzerinden doğrudan ilan araması (e-posta değil).</summary>
    [JsonPropertyName("linkedinJobs")]
    public LinkedInJobsConfig LinkedInJobs { get; set; } = new();

    /// <summary>
    /// Otomatik yetenek/staj programı keşif ayarları.
    /// </summary>
    [JsonPropertyName("talentDiscovery")]
    public TalentDiscoveryConfig TalentDiscovery { get; set; } = new();

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

    /// <summary>
    /// true = başlıkta seviye kelimesi yoksa ilan METNİNE bak ("yeni mezun",
    /// "0-2 yıl deneyim"). Türkiye ilanlarının başlığında "junior" yazmadığı için
    /// strict mod bu olmadan neredeyse SADECE LinkedIn ilanı geçiriyor.
    /// "en az 3 yıl deneyim" geçen ilanlar yine elenir. Rol şartı hep başlıktan.
    /// </summary>
    [JsonPropertyName("useDescriptionForLevel")]
    public bool UseDescriptionForLevel { get; set; } = true;
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

    /// <summary>
    /// true = bu şirketin TÜM ilanları Türkiye'de; ats.locationAllow filtresi atlanır.
    /// Lokasyon alanını bozuk dolduran board'lar için (ör. Peak Games her ilana
    /// lokasyon olarak "Full-time" yazıyor — filtre uygulansa hepsi elenirdi).
    /// </summary>
    [JsonPropertyName("allJobsInTurkey")]
    public bool AllJobsInTurkey { get; set; }
}

public sealed class AtsConfig
{
    /// <summary>
    /// Greenhouse/Lever ilanlarında kabul edilen lokasyon parçaları (alt dize eşleşmesi).
    /// Boşsa filtre uygulanmaz. Bu board'lar uluslararası olduğu için filtresiz
    /// bırakılırsa Teksas/Bengaluru ilanları da Telegram'a düşer.
    /// </summary>
    [JsonPropertyName("locationAllow")]
    public List<string> LocationAllow { get; set; } = new();
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

    /// <summary>
    /// Filtresiz "tüm açık ilanlar" listesinde taranacak sayfa sayısı (20 ilan/sayfa).
    /// Techcareer'da toplam ~190 açık ilan var, yani 10 sayfa hepsini kapsar.
    /// Kelime araması havuzun sadece 1/5'ini döndürdüğü için asıl kapsama buradan gelir.
    /// </summary>
    [JsonPropertyName("allJobsMaxPages")]
    public int AllJobsMaxPages { get; set; } = 10;
}

public sealed class KariyerConfig
{
    /// <summary>
    /// Arama kelimeleri. Her biri kariyer.net'e ?kw=... sorgusu olur.
    /// DİKKAT: /is-ilanlari/junior+backend gibi PATH tabanlı URL'ler aramayı yok sayıp
    /// alakasız ilan (aşçı, muhasebe) döndürüyor — ?kw= tek çalışan biçim.
    /// Boşsa bu kaynak atlanır.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Her kelime için taranacak sayfa sayısı.</summary>
    [JsonPropertyName("maxPages")]
    public int MaxPages { get; set; } = 2;
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
    /// <summary>
    /// LinkedIn Job Alert maillerini IMAP ile okuma kaynağı açık mı.
    /// 2026-07-31'de KAPATILDI: 34 mail tarayıp 0 ilan linki çıkarıyordu ve zaten
    /// linkedinJobs kaynağı aynı ilanları doğrudan siteden çekiyor. Kullanıcı
    /// LinkedIn ilanlarını e-postada da görüyor — bu kaynak sadece log gürültüsüydü.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

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

/// <summary>
/// Otomatik yetenek/staj programı keşif ayarları.
/// Web araması ile şirketlerin "genç yetenek", "yetenek programı", "staj programı" sayfalarını bulur.
/// </summary>
public sealed class TalentDiscoveryConfig
{
    /// <summary>
    /// Arama motoruna gönderilecek sorgular. Her biri ayrı bir arama olur.
    /// Ör: "genç yetenek programı site:kariyer.net", "staj programı yazılım site:linkedin.com"
    /// Boşsa bu kaynak atlanır.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Her anahtar kelime için taranacak sayfa sayısı (DuckDuckGo 50 sonuç/sayfa).</summary>
    [JsonPropertyName("maxPages")]
    public int MaxPages { get; set; } = 2;

    /// <summary>İki arama/istek arası bekleme (ms) - rate limit önlemi.</summary>
    [JsonPropertyName("delayMs")]
    public int DelayMs { get; set; } = 1500;
}