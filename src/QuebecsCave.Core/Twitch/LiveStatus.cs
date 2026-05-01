namespace QuebecsCave.Core.Twitch;

/// <summary>
/// Last-known live state of the streamer's channel. The poller refreshes
/// this every <c>Twitch:LiveStatusPollSeconds</c>; consumers (UI + API
/// endpoint) read the snapshot rather than polling Helix themselves.
/// </summary>
public sealed record LiveStatus(
    bool IsLive,
    string? GameName,
    string? Title,
    DateTimeOffset? StartedAt,
    int ViewerCount,
    DateTimeOffset CheckedAt)
{
    public static LiveStatus Offline(DateTimeOffset checkedAt) =>
        new(false, null, null, null, 0, checkedAt);
}

public interface ILiveStatusService
{
    LiveStatus Current { get; }
    event Action<LiveStatus>? Changed;
}
