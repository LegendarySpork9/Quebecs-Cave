using Microsoft.Extensions.Options;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Identity;

public interface IRoleResolver
{
    /// <summary>
    /// Returns the additive role set for a Twitch user. A user can be just a
    /// Developer, just a Moderator, both, or any combination plus Viewer.
    /// Streamer is binary — only the configured streamer ID matches.
    /// </summary>
    Task<IReadOnlyList<string>> ResolveAsync(string twitchUserId, CancellationToken cancellationToken);
}

public sealed class RoleResolver : IRoleResolver
{
    private readonly IModeratorCacheRepository _mods;
    private readonly IDeveloperRepository _developers;
    private readonly TwitchOptions _twitchOptions;

    public RoleResolver(
        IModeratorCacheRepository mods,
        IDeveloperRepository developers,
        IOptions<TwitchOptions> twitchOptions)
    {
        _mods = mods;
        _developers = developers;
        _twitchOptions = twitchOptions.Value;
    }

    public async Task<IReadOnlyList<string>> ResolveAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        var roles = new List<string> { RoleName.Viewer };

        if (!string.IsNullOrEmpty(_twitchOptions.StreamerUserId)
            && string.Equals(twitchUserId, _twitchOptions.StreamerUserId, StringComparison.Ordinal))
        {
            roles.Add(RoleName.Streamer);
        }

        if (await _mods.IsModeratorAsync(twitchUserId, cancellationToken))
        {
            roles.Add(RoleName.Moderator);
        }

        if (await _developers.IsDeveloperAsync(twitchUserId, cancellationToken))
        {
            roles.Add(RoleName.Developer);
        }

        return roles;
    }
}
