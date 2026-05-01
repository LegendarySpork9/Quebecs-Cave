using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken);
    Task<User?> GetByTwitchUserIdAsync(string twitchUserId, CancellationToken cancellationToken);
    Task<User> UpsertFromTwitchAsync(
        string twitchUserId,
        string twitchLogin,
        string displayName,
        string? avatarUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task UpdatePreferencesAsync(
        int userId,
        string? themePreference,
        string? timeZoneId,
        CancellationToken cancellationToken);
}
