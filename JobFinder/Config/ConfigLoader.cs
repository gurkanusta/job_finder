using System.Text.Json;
using JobFinder.Models;

namespace JobFinder.Config;

/// <summary>config.json'u okur. Yol CONFIG_PATH env'i ile ezilebilir; yoksa
/// çalışma dizininden yukarı doğru aranır (dotnet run nereden çağrılırsa çağrılsın bulunur).</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load()
    {
        var path = ResolvePath(Environment.GetEnvironmentVariable("CONFIG_PATH"), "config.json");
        if (path is null)
            throw new FileNotFoundException("config.json bulunamadı. Repo kökünde olmalı veya CONFIG_PATH ile belirtin.");

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options)
                  ?? throw new InvalidOperationException("config.json parse edilemedi.");
        return cfg;
    }

    /// <summary>Verilen açık yol geçerliyse onu; değilse cwd'den yukarı doğru arayarak dosyayı bulur.</summary>
    public static string? ResolvePath(string? explicitPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
