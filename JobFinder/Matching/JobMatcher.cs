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

    public bool IsMatch(JobPosting job) => IsMatch(job.Title);

    public bool IsMatch(string title)
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

        return _cfg.RequireBothGroups ? (hasLevel && hasRole) : (hasLevel || hasRole);
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
