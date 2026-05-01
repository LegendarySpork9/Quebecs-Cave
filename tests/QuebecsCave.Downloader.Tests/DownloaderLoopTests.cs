using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Downloader;
using QuebecsCave.Downloader.Api;
using QuebecsCave.Downloader.Pipeline;
using QuebecsCave.Downloader.Storage;
using QuebecsCave.Downloader.YtDlp;

namespace QuebecsCave.Downloader.Tests;

[TestClass]
public sealed class DownloaderLoopTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameDto VarietyGame = new(
        Id: 5, Name: "Variety", Slug: "variety",
        IconUrl: null, TwitchGameId: null, CreatedAt: FixedNow);

    [TestMethod]
    public async Task RunOnceAsync_NoNewVods_DoesNotIngest()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod>
            {
                Vod("v-1"),
                Vod("v-2"),
            });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "v-1", "v-2" });

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Yt.Verify(
            y => y.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.CreateStreamAsync(It.IsAny<IngestStreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RunOnceAsync_NewVod_DownloadsAndIngests()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("new-1") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameBySlugAsync("variety", It.IsAny<CancellationToken>()))
            .ReturnsAsync(VarietyGame);
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("new-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(true, 0, "", TimeSpan.FromSeconds(1)));

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Yt.Verify(
            y => y.DownloadVideoAsync("new-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Api.Verify(
            a => a.CreateStreamAsync(
                It.Is<IngestStreamRequest>(r => r.TwitchVodId == "new-1" && r.GameId == VarietyGame.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RunOnceAsync_YtDlpFails_DoesNotIngestAndPostsError()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("broken") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameBySlugAsync("variety", It.IsAny<CancellationToken>()))
            .ReturnsAsync(VarietyGame);
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("broken", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(false, 1, "yt-dlp went home", TimeSpan.FromSeconds(1)));

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Api.Verify(
            a => a.CreateStreamAsync(It.IsAny<IngestStreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.PostErrorsAsync(It.IsAny<IReadOnlyList<ErrorEventDto>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task RunOnceAsync_NoGames_SkipsIngest()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("orphan") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDto?)null);
        harness.Api
            .Setup(a => a.ListGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameDto>());

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Yt.Verify(
            y => y.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.CreateStreamAsync(It.IsAny<IngestStreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RunOnceAsync_OneVodFails_OthersStillProcess()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("good"), Vod("bad"), Vod("good-2") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameBySlugAsync("variety", It.IsAny<CancellationToken>()))
            .ReturnsAsync(VarietyGame);
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("good", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(true, 0, "", TimeSpan.FromSeconds(1)));
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("bad", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(false, 1, "boom", TimeSpan.FromSeconds(1)));
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("good-2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(true, 0, "", TimeSpan.FromSeconds(1)));

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Api.Verify(
            a => a.CreateStreamAsync(It.Is<IngestStreamRequest>(r => r.TwitchVodId == "good"), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Api.Verify(
            a => a.CreateStreamAsync(It.Is<IngestStreamRequest>(r => r.TwitchVodId == "bad"), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.CreateStreamAsync(It.Is<IngestStreamRequest>(r => r.TwitchVodId == "good-2"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RunOnceAsync_VodMatchesLiveSession_UsesSessionGameNotFallback()
    {
        // Live-session match returns Minecraft (gameId=42); fallback is Variety
        // (gameId=5). The ingest call should carry GameId=42, never 5.
        var harness = new Harness();
        var minecraft = new GameForTimeDto(GameId: 42, Slug: "minecraft", Name: "Minecraft");

        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("matched") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameForTimeAsync("68000893", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(minecraft);
        harness.Api
            .Setup(a => a.GetGameBySlugAsync("variety", It.IsAny<CancellationToken>()))
            .ReturnsAsync(VarietyGame);
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("matched", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(true, 0, "", TimeSpan.FromSeconds(1)));

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        // Game from session, not fallback.
        harness.Api.Verify(
            a => a.CreateStreamAsync(
                It.Is<IngestStreamRequest>(r => r.TwitchVodId == "matched" && r.GameId == minecraft.GameId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Api.Verify(
            a => a.CreateStreamAsync(
                It.Is<IngestStreamRequest>(r => r.GameId == VarietyGame.Id),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // The path used to build storage paths should reflect the matched game's slug.
        harness.Storage.Verify(
            s => s.BuildVideoPath("minecraft", It.IsAny<int>(), "matched"),
            Times.Once);
    }

    [TestMethod]
    public async Task RunOnceAsync_NoLiveSessionMatch_UsesFallbackGame()
    {
        // game-for-time returns null → loop falls back to DefaultGameSlug.
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("orphan") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameForTimeAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameForTimeDto?)null);
        harness.Api
            .Setup(a => a.GetGameBySlugAsync("variety", It.IsAny<CancellationToken>()))
            .ReturnsAsync(VarietyGame);
        harness.Yt
            .Setup(y => y.DownloadVideoAsync("orphan", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YtDlpResult(true, 0, "", TimeSpan.FromSeconds(1)));

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Api.Verify(
            a => a.CreateStreamAsync(
                It.Is<IngestStreamRequest>(r => r.TwitchVodId == "orphan" && r.GameId == VarietyGame.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Storage.Verify(
            s => s.BuildVideoPath("variety", It.IsAny<int>(), "orphan"),
            Times.Once);
    }

    [TestMethod]
    public async Task RunOnceAsync_NoMatchAndNoFallback_ReportsErrorAndSkips()
    {
        // Neither live-session nor any game in the catalogue: the loop should
        // refuse to ingest the VOD, post an error, but still complete cleanly
        // (so other VODs in the same sweep would still run).
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { Vod("nowhere-to-go") });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        harness.Api
            .Setup(a => a.GetGameForTimeAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameForTimeDto?)null);
        harness.Api
            .Setup(a => a.GetGameBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDto?)null);
        harness.Api
            .Setup(a => a.ListGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameDto>());

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Yt.Verify(
            y => y.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.CreateStreamAsync(It.IsAny<IngestStreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Api.Verify(
            a => a.PostErrorsAsync(It.IsAny<IReadOnlyList<ErrorEventDto>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task RunOnceAsync_StreamsNeedingThumbnails_AreRetriedAndPatched()
    {
        // No new VODs to ingest, but one existing stream is missing a thumbnail.
        // The retry sweep should match it against the current Helix VOD list,
        // download the thumbnail, and PUT to /api/ingest/streams/{id}/thumbnail.
        var harness = new Harness();
        var pendingVod = Vod("ready-now") with { ThumbnailUrlTemplate = "https://cdn.example/%{width}x%{height}.jpg" };

        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod> { pendingVod });
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "ready-now" }); // already archived
        harness.Api
            .Setup(a => a.ListStreamsNeedingThumbnailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StreamNeedingThumbnailDto(
                    StreamId: 99,
                    TwitchVodId: "ready-now",
                    GameSlug: "minecraft",
                    StreamedAtYear: 2026),
            });
        harness.Storage
            .Setup(s => s.DownloadFileAsync(
                It.Is<string>(u => u.Contains("320x180")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        // Storage paths and the patched URL should reflect the stream's real
        // game slug + year, not a placeholder.
        harness.Storage.Verify(
            s => s.BuildThumbnailPath("minecraft", 2026, "ready-now"),
            Times.AtLeastOnce);
        harness.Storage.Verify(
            s => s.ToPublicThumbnailUrl("minecraft", 2026, "ready-now"),
            Times.AtLeastOnce);
        harness.Api.Verify(
            a => a.UpdateThumbnailAsync(99, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RunOnceAsync_PostsHeartbeatEvent()
    {
        var harness = new Harness();
        harness.Twitch
            .Setup(t => t.GetUserVodsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TwitchVod>());
        harness.Api
            .Setup(a => a.GetKnownVodIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        await harness.Loop.RunOnceAsync(CancellationToken.None);

        harness.Api.Verify(
            a => a.PostDownloaderEventsAsync(
                It.Is<IReadOnlyList<DownloaderEventDto>>(list => list.Any(e => e.Stage == DownloaderStage.Heartbeat)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    private static TwitchVod Vod(string id) => new(
        Id: id,
        UserId: "68000893",
        UserLogin: "longlivequebec",
        Title: $"VOD {id}",
        Description: "",
        CreatedAt: FixedNow.AddDays(-1),
        PublishedAt: FixedNow.AddDays(-1),
        Url: $"https://www.twitch.tv/videos/{id}",
        ThumbnailUrlTemplate: "",
        ViewCount: 0,
        Type: "archive",
        DurationSeconds: 60);

    private sealed class Harness
    {
        public Mock<ITwitchClient> Twitch { get; } = new();
        public Mock<ICaveApiClient> Api { get; } = new();
        public Mock<IYtDlpRunner> Yt { get; } = new();
        public Mock<IFileStorage> Storage { get; } = new();
        public DownloaderLoop Loop { get; }

        public Harness()
        {
            Storage.Setup(s => s.BuildVideoPath(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns<string, int, string>((slug, year, vod) => $"D:/cave/{slug}/{year}/{vod}.mp4");
            Storage.Setup(s => s.BuildThumbnailPath(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns<string, int, string>((slug, year, vod) => $"D:/cave/{slug}/{year}/{vod}.jpg");
            Storage.Setup(s => s.ToPublicVideoUrl(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns<string, int, string>((slug, year, vod) => $"https://vod.example/{slug}/{year}/{vod}.mp4");
            Storage.Setup(s => s.ToPublicThumbnailUrl(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns<string, int, string>((slug, year, vod) => $"https://vod.example/{slug}/{year}/{vod}.jpg");

            var clock = new FixedClock(FixedNow);
            var dlOpts = Options.Create(new DownloaderOptions { DefaultGameSlug = "variety", VodTake = 50 });
            var twOpts = Options.Create(new TwitchBroadcasterOptions { BroadcasterUserId = "68000893" });

            Loop = new DownloaderLoop(
                Twitch.Object, Api.Object, Yt.Object, Storage.Object,
                clock, dlOpts, twOpts, NullLogger<DownloaderLoop>.Instance);
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => Now = now;
        public DateTimeOffset Now { get; }
    }
}
