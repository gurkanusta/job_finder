namespace JobFinder.Models;

/// <summary>
/// Bir iş ilanını temsil eder. Tüm kaynaklar (Greenhouse, Lever, LinkedIn maili,
/// scraping, Telegram) bu ortak modele dönüştürerek çıktı verir.
/// </summary>
public sealed class JobPosting
{
    /// <summary>Kaynak etiketi: "Greenhouse", "Lever", "LinkedIn", "Kariyer.net" vb.</summary>
    public required string Source { get; init; }

    public required string Title { get; init; }

    public string Company { get; init; } = "";

    public string Location { get; init; } = "";

    public required string Url { get; init; }

    /// <summary>
    /// Opsiyonel ilan tarihi (varsa). Bildirimde/sıralamada kullanılabilir.
    /// </summary>
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>
    /// Dedup (tekilleştirme) anahtarı. Belirtilmezse normalize edilmiş URL kullanılır.
    /// Aynı ilan iki kez bildirilmesin diye seen-jobs.json bu anahtarla eşleşir.
    /// </summary>
    public string? DedupKeyOverride { get; init; }

    public string DedupKey => DedupKeyOverride ?? NormalizeUrl(Url);

    // Bunlar sadece takip amaçlı; dedup'ta yok sayılır. gh_jid, id gibi ANLAMLI
    // paramlar KORUNUR (yoksa aynı board'daki tüm ilanlar tek anahtara çöker).
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "trk", "ref", "refid", "origin", "savedsearchid", "originToLandingJobPostings",
    };

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim().TrimEnd('/').ToLowerInvariant();

        // Query'yi koru ama sadece takip paramlarını at. Fragment'ı düşür.
        var kept = new List<string>();
        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = pair.Split('=', 2)[0];
                if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;
                if (TrackingParams.Contains(key)) continue;
                kept.Add(pair);
            }
        }

        var baseUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        var normalized = kept.Count > 0 ? $"{baseUrl}?{string.Join('&', kept)}" : baseUrl;
        return normalized.ToLowerInvariant();
    }
}
