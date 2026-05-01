namespace QuebecsCave.Core.Twitch;

public interface ITwitchClient
{
    Task<IReadOnlyList<TwitchVod>> GetUserVodsAsync(
        string userId,
        int take,
        string type,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TwitchGame>> GetGamesByNameAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TwitchGame>> GetGamesByIdAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken);

    Task<TwitchLiveStream?> GetLiveStreamAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<TwitchUserToken> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken);

    Task<TwitchUserToken> RefreshUserTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<TwitchUser?> GetUserAsync(string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<TwitchModerator>> GetModeratorsAsync(
        string broadcasterId,
        string accessToken,
        CancellationToken cancellationToken);
}
