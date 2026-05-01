using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface IGameRepository
{
    Task<IReadOnlyList<Game>> ListAsync(CancellationToken cancellationToken);
    Task<Game?> GetByIdAsync(int gameId, CancellationToken cancellationToken);
    Task<Game?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
    Task<int> CreateAsync(Game game, CancellationToken cancellationToken);
    Task UpdateAsync(Game game, CancellationToken cancellationToken);
    Task DeleteAsync(int gameId, CancellationToken cancellationToken);

    /// <summary>
    /// Patches just the IconUrl. Used by the box-art refresh job so the
    /// background sweep can't race an admin edit on Name/Slug/IsCustomIcon.
    /// </summary>
    Task UpdateIconAsync(int gameId, string iconUrl, CancellationToken cancellationToken);
}
