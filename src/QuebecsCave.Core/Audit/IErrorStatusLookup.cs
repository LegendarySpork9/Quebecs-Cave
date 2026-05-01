namespace QuebecsCave.Core.Audit;

/// <summary>
/// Resolves the seeded ErrorStatus IDs by name. Implementation caches.
/// </summary>
public interface IErrorStatusLookup
{
    Task<int> GetIdAsync(string name, CancellationToken cancellationToken);
}
