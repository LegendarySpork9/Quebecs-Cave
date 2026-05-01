using QuebecsCave.Core.Twitch;

namespace QuebecsCave.Services.Twitch;

public sealed class LiveStatusService : ILiveStatusService
{
    private readonly object _gate = new();
    private LiveStatus _current = LiveStatus.Offline(DateTimeOffset.MinValue);

    public LiveStatus Current
    {
        get { lock (_gate) return _current; }
    }

    public event Action<LiveStatus>? Changed;

    /// <summary>
    /// Replace the current snapshot. Only fires Changed if the live-flag,
    /// game name, or title differ — viewer-count tweaks shouldn't churn the UI.
    /// </summary>
    internal void Update(LiveStatus next)
    {
        bool fire;
        lock (_gate)
        {
            fire = _current.IsLive   != next.IsLive
                || _current.GameName != next.GameName
                || _current.Title    != next.Title;
            _current = next;
        }
        if (fire)
        {
            Changed?.Invoke(next);
        }
    }
}
