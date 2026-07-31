using System.Text.RegularExpressions;
using JobFinder.Models;

namespace JobFinder.Matching;

/// <summary>
/// Bir ilan başlığının config'teki kelime listelerine göre eşleşip eşleşmediğine karar verir.
/// Türkçe büyük/küçük harf (İ/I, ı/i) tuzaklarını normalize ederek karşılaştırır.
/// </summary>
public sealed class JobMatcher
{
    private readonly MatchingConfig _cfg;

    public JobMatcher(MatchingConfig cfg) => _cfg = cfg;

    /// <summary>
    /// İlan metninde "bu junior bir pozisyon" sinyali. TR ilanları başlıkta junior
    /// yazmaz, gerekliliklerde yazar: "yeni mezun", "0-2 yıl deneyim", "stajyer".
    /// </summary>
    private static readonly Regex JuniorHint = new(
        @"yeni\s+mezun|new\s+grad|son\s+sınıf|son\s+sinif|stajyer|intern|junior|jr\.?\b|trainee|" +
        @"deneyim\s+şartı\s+(yok|aranmaz)|tecrübe\s+şartı\s+yok|yetiştirilmek\s+üzere|" +
        @"\b0\s*[-–]\s*[12]\s*(yıl|yil|year)|\b1\s*[-–]\s*2\s*(yıl|yil|year)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "En az 3 yıl deneyim" gibi kıdem talebi. JuniorHint'i EZER — "junior" kelimesi
    /// ilanın başka yerinde geçse bile 3+ yıl isteyen ilan junior değildir.
    /// </summary>
    private static readonly Regex SeniorExperience = new(
        @"(en\s+az|minimum|min\.?|at\s+least)\s*([3-9]|[1-9]\d)\s*\+?\s*(yıl|yil|year)|" +
        @"\b([3-9]|[1-9]\d)\s*\+\s*(yıl|yil|year)|" +
        @"\b([3-9]|[1-9]\d)\s*[-–]\s*\d+\s*(yıl|yil|year)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsMatch(JobPosting job) => IsMatch(job.Title, job.Description);

    public bool IsMatch(string title) => IsMatch(title, "");

    public bool IsMatch(string title, string description)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var norm = Normalize(title);

        // Hariç tutulanlardan biri geçiyorsa direkt ele.
        foreach (var ex in _cfg.ExcludeKeywords)
        {
            if (!string.IsNullOrWhiteSpace(ex) && norm.Contains(Normalize(ex)))
                return false;
        }

        bool hasLevel = ContainsAny(norm, _cfg.LevelKeywords);
        bool hasRole = ContainsAny(norm, _cfg.RoleKeywords);

        // Başlıkta seviye yoksa ilan metnine bak: TR ilanlarının çoğu böyle.
        // Rol şartı YİNE başlıktan gelir — metinde "developer" geçmesi ilanı
        // yazılım ilanı yapmaz (her ilanda "ekibimiz" tarzı doldurma var).
        if (!hasLevel && _cfg.UseDescriptionForLevel && !string.IsNullOrWhiteSpace(description))
            hasLevel = JuniorHint.IsMatch(description) && !SeniorExperience.IsMatch(description);

        return _cfg.Mode.Trim().ToLowerInvariant() switch
        {
            "strict" => hasLevel && hasRole,
            "role" => hasRole,
            "either" => hasLevel || hasRole,
            // mode boş → eski requireBothGroups davranışı.
            _ => _cfg.RequireBothGroups ? (hasLevel && hasRole) : (hasLevel || hasRole),
        };
    }

    private static bool ContainsAny(string normalizedTitle, IEnumerable<string> keywords)
    {
        foreach (var kw in keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && normalizedTitle.Contains(Normalize(kw)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Türkçe'ye duyarlı normalizasyon: önce sorunlu büyük harfleri (İ, I) sabitle,
    /// sonra invariant küçült. Böylece "Yazılım" ve "yazılım" güvenle eşleşir.
    /// </summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var buffer = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case 'İ': buffer.Append('i'); break;
                case 'I': buffer.Append('ı'); break;
                default: buffer.Append(char.ToLowerInvariant(ch)); break;
            }
        }
        return buffer.ToString();
    }
}
