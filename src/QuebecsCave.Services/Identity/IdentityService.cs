using Microsoft.Extensions.Options;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Identity;

public sealed record SignInResult(
    int UserId,
    string TwitchUserId,
    string TwitchLogin,
    string DisplayName,
    string? AvatarUrl,
    string? ThemePreference,
    string? TimeZoneId,
    IReadOnlyList<string> Roles);

/// <summary>
/// Encapsulates the "user just authenticated with Twitch" flow: upsert into
/// the User table, resolve the additive role set, persist the streamer's
/// refresh token if applicable. Returns the data the auth layer needs to
/// build claims.
/// </summary>
public interface IIdentityService
{
    Task<SignInResult> ProcessTwitchSignInAsync(
        TwitchUser twitchUser,
        string? accessToken,
        string? refreshToken,
        int expiresInSeconds,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken);
}

public sealed class IdentityService : IIdentityService
{
    private readonly IUserRepository _users;
    private readonly ITwitchTokenRepository _tokens;
    private readonly IRoleResolver _roleResolver;
    private readonly TwitchOptions _twitchOptions;
    private readonly IClock _clock;

    public IdentityService(
        IUserRepository users,
        ITwitchTokenRepository tokens,
        IRoleResolver roleResolver,
        IOptions<TwitchOptions> twitchOptions,
        IClock clock)
    {
        _users = users;
        _tokens = tokens;
        _roleResolver = roleResolver;
        _twitchOptions = twitchOptions.Value;
        _clock = clock;
    }

    public async Task<SignInResult> ProcessTwitchSignInAsync(
        TwitchUser twitchUser,
        string? accessToken,
        string? refreshToken,
        int expiresInSeconds,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var user = await _users.UpsertFromTwitchAsync(
            twitchUser.Id,
            twitchUser.Login,
            string.IsNullOrEmpty(twitchUser.DisplayName) ? twitchUser.Login : twitchUser.DisplayName,
            string.IsNullOrEmpty(twitchUser.ProfileImageUrl) ? null : twitchUser.ProfileImageUrl,
            _clock.Now,
            cancellationToken);

        var isStreamer = string.Equals(
            twitchUser.Id,
            _twitchOptions.StreamerUserId,
            StringComparison.Ordinal);

        if (isStreamer && !string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
        {
            await _tokens.UpsertAsync(
                user.UserId,
                accessToken,
                refreshToken,
                _clock.Now.AddSeconds(Math.Max(60, expiresInSeconds)),
                string.Join(' ', scopes),
                cancellationToken);
        }

        var roles = await _roleResolver.ResolveAsync(twitchUser.Id, cancellationToken);

        return new SignInResult(
            user.UserId,
            twitchUser.Id,
            twitchUser.Login,
            user.DisplayName,
            user.AvatarUrl,
            user.ThemePreference,
            user.TimeZoneId,
            roles);
    }
}
