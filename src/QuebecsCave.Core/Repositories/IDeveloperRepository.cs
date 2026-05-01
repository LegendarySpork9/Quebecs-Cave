using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface IDeveloperRepository
{
    Task<IReadOnlyList<Developer>> ListAsync(CancellationToken cancellationToken);
    Task<bool> IsDeveloperAsync(string twitchUserId, CancellationToken cancellationToken);
    Task<int> AddAsync(string twitchUserId, string twitchLogin, int addedByUserId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RemoveAsync(string twitchUserId, CancellationToken cancellationToken);
}

public interface IModeratorCacheRepository
{
    Task<IReadOnlyList<ModeratorCacheEntry>> ListAsync(CancellationToken cancellationToken);
    Task<bool> IsModeratorAsync(string twitchUserId, CancellationToken cancellationToken);
    Task ReplaceAllAsync(IReadOnlyCollection<(string TwitchUserId, string TwitchLogin)> moderators, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ITwitchTokenRepository
{
    Task<TwitchToken?> GetForUserAsync(int userId, CancellationToken cancellationToken);
    Task UpsertAsync(int userId, string accessToken, string refreshToken, DateTimeOffset expiresAt, string scopes, CancellationToken cancellationToken);
}
