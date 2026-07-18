using JobFinder.Models;

namespace JobFinder.Sources;

/// <summary>
/// Her ilan kaynağının uyguladığı sözleşme. Runner her kaynağı try/catch içinde
/// çağırır; biri patlarsa diğerleri etkilenmez.
/// </summary>
public interface IJobSource
{
    /// <summary>--source argümanıyla eşleşen kısa ad (ör. "greenhouse").</summary>
    string Name { get; }

    Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct);
}
