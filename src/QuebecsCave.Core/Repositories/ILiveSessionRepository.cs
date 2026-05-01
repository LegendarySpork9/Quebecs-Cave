using QuebecsCave.Core.Domain;

namespace QuebecsCave.Core.Repositories;

public interface ILiveSessionRepository
{
    /// <summary>
    /// Find an open session for the broadcaster (EndedAt IS NULL) whose game
    /// matches the given Twitch game id (or has no game). If found, bump
    /// LastSeenAt and update Title; otherwise insert a new session.
    /// Returns the session id.
    /// </summary>
    Task<int> UpsertActiveAsync(
        string broadcasterUserId,
        string? twitchGameId,
        string? gameName,
        string? title,
        int? resolvedGameId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark the latest open session for the broadcaster as ended. No-op if
    /// none open. Returns the affected session id, or null.
    /// </summary>
    Task<int?> CloseLatestActiveAsync(
        string broadcasterUserId,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Find the session that was running at the given time. Matches when
    /// StartedAt &lt;= at AND (EndedAt IS NULL OR at &lt; EndedAt).
    /// Returns the most recent matching session, or null.
    /// </summary>
    Task<LiveSession?> FindForTimeAsync(
        string broadcasterUserId,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveSession>> ListRecentAsync(string broadcasterUserId, int take, CancellationToken cancellationToken);
}
