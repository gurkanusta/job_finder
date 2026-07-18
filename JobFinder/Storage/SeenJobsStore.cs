using System.Text.Json;

namespace JobFinder.Storage;

/// <summary>
/// Daha önce bildirilmiş ilanların dedup anahtarlarını tutar (seen-jobs.json).
/// Her anahtar için ilk görülme zamanı saklanır; eski kayıtlar budanarak dosya şişmesi önlenir.
/// </summary>
public sealed class SeenJobsStore
{
    private readonly string _path;
    private readonly Dictionary<string, DateTimeOffset> _seen;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private SeenJobsStore(string path, Dictionary<string, DateTimeOffset> seen)
    {
        _path = path;
        _seen = seen;
    }

    public static SeenJobsStore Load(string path)
    {
        Dictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var model = JsonSerializer.Deserialize<SeenModel>(json, Options);
                    if (model?.Keys is not null)
                        seen = new Dictionary<string, DateTimeOffset>(model.Keys, StringComparer.Ordinal);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SeenJobsStore] {path} okunamadı, boş başlıyorum: {ex.Message}");
            }
        }
        return new SeenJobsStore(path, seen);
    }

    public bool IsSeen(string dedupKey) => _seen.ContainsKey(dedupKey);

    public void MarkSeen(string dedupKey)
    {
        if (!_seen.ContainsKey(dedupKey))
            _seen[dedupKey] = DateTimeOffset.UtcNow;
    }

    /// <summary>retentionDays'ten eski kayıtları at, sonra dosyayı yaz.</summary>
    public void Save(int retentionDays)
    {
        if (retentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var stale = _seen.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _seen.Remove(k);
        }

        var model = new SeenModel { Keys = _seen };
        var json = JsonSerializer.Serialize(model, Options);
        File.WriteAllText(_path, json);
    }

    public int Count => _seen.Count;

    private sealed class SeenModel
    {
        public Dictionary<string, DateTimeOffset> Keys { get; set; } = new();
    }
}
