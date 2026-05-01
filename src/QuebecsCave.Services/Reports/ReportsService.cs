using QuebecsCave.Core.Reports;
using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Reports;

public interface IReportsService
{
    Task<ReportsBundle> GetReportsAsync(CancellationToken cancellationToken);
}

public sealed class ReportsService : IReportsService
{
    private readonly IReportsRepository _repo;
    private readonly IClock _clock;

    public ReportsService(IReportsRepository repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async Task<ReportsBundle> GetReportsAsync(CancellationToken cancellationToken)
    {
        var since = _clock.Now.AddDays(-30);
        var kpisTask    = _repo.GetKpisAsync(cancellationToken);
        var streamsTask = _repo.GetTopStreamsAsync(5, cancellationToken);
        var gamesTask   = _repo.GetTopGamesAsync(5, cancellationToken);
        var emojisTask  = _repo.GetTopEmojisAsync(10, cancellationToken);
        var growthTask  = _repo.GetDailyViewsAsync(since, cancellationToken);

        await Task.WhenAll(kpisTask, streamsTask, gamesTask, emojisTask, growthTask);

        return new ReportsBundle(
            kpisTask.Result,
            streamsTask.Result,
            gamesTask.Result,
            emojisTask.Result,
            growthTask.Result);
    }
}
