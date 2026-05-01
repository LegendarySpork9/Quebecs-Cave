using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;

namespace QuebecsCave.Services.Games;

public sealed record GameWithStreamCount(Game Game, int StreamCount);

public interface IGameService
{
    Task<IReadOnlyList<Game>> ListAsync(CancellationToken cancellationToken);
    Task<Game?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
}

public sealed class GameService : IGameService
{
    private readonly IGameRepository _games;

    public GameService(IGameRepository games) => _games = games;

    public Task<IReadOnlyList<Game>> ListAsync(CancellationToken cancellationToken) =>
        _games.ListAsync(cancellationToken);

    public Task<Game?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        _games.GetBySlugAsync(slug, cancellationToken);
}
