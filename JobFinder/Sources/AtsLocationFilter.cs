
using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Greenhouse/Lever board'ları ULUSLARARASI: Trendyol'un Riyad/Atina/Bükreş,
/// Dream Games'in Londra, Insider'ın New York/Singapur ilanları aynı board'da.
/// Techcareer/Kariyer/LinkedIn kaynakları zaten Türkiye'ye kilitli olduğu için
/// lokasyon filtresi SADECE bu iki ATS kaynağına uygulanır.
///
/// Boş lokasyon = karar veremiyoruz → GEÇİRİLİR (ilan kaybetmemek için).
/// Bazı board'lar lokasyon alanını yanlış dolduruyor (ör. Peak Games "Full-time"
/// yazıyor); o şirketler config'te allJobsInTurkey:true ile işaretlenir.
/// </summary>
public static class AtsLocationFilter
{
    public static bool IsAllowed(string location, CompanyEntry company, IReadOnlyList<string> allow)
    {
        // Şirketin tüm ilanları zaten Türkiye'de → lokasyon alanına hiç bakma.
        if (company.AllJobsInTurkey) return true;
        // Filtre tanımlı değilse kimseyi eleme.
        if (allow.Count == 0) return true;
        // Lokasyon bilgisi yoksa eleme (yanlış negatif riskini almıyoruz).
        if (string.IsNullOrWhiteSpace(location)) return true;

        var norm = Fold(location);
        foreach (var a in allow)
        {
            if (!string.IsNullOrWhiteSpace(a) && norm.Contains(Fold(a)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Lokasyon karşılaştırması için Türkçe harfleri ASCII'ye katlar.
    /// JobMatcher.Normalize BURADA YETMİYOR: o, Türkçe kuralına uyup 'I' → 'ı'
    /// çeviriyor, dolayısıyla board'ların ASCII yazdığı "Istanbul" → "ıstanbul"
    /// oluyor ve config'teki "istanbul" ile EŞLEŞMİYORDU. Lokasyonda dilbilgisi
    /// doğruluğu değil eşleşme önemli, o yüzden i/ı/İ hepsi 'i'ye katlanır.
    /// </summary>
    private static string Fold(string s)
    {
        var buffer = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            buffer.Append(char.ToLowerInvariant(ch) switch
            {
                'ı' or 'i' or 'İ' or 'î' => 'i',
                'ğ' => 'g',
                'ü' or 'û' => 'u',
                'ş' => 's',
                'ö' => 'o',
                'ç' => 'c',
                'â' => 'a',
                var c => c,
            });
        }
        return buffer.ToString();
    }
}
