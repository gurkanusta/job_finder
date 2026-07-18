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

    [JsonPropertyName("telegramChannels")]
    public List<string> TelegramChannels { get; set; } = new();

    [JsonPropertyName("linkedin")]
    public LinkedInConfig LinkedIn { get; set; } = new();

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
    /// true  = başlık HEM bir seviye HEM bir rol kelimesi içermeli (ör. "Junior Backend").
    /// false = seviye VEYA rol kelimesinden biri yeterli (daha gevşek, daha çok gürültü).
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
