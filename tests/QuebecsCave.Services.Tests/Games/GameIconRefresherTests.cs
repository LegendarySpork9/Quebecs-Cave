using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Games;

namespace QuebecsCave.Services.Tests.Games;

[TestClass]
public sealed class GameIconRefresherTests
{
    private Mock<IGameRepository> _games = null!;
    private Mock<ITwitchClient> _twitch = null!;
    private GameIconRefresher _sut = null!;

    [TestInitialize]
    public void SetUp()
    {
        _games = new Mock<IGameRepository>();
        _twitch = new Mock<ITwitchClient>();
        _sut = new GameIconRefresher(_games.Object, _twitch.Object, NullLogger<GameIconRefresher>.Instance);
    }

    private static Game Make(int id, string slug, string? twitchId, bool customIcon, string? iconUrl = null) => new()
    {
        GameId = id,
        Name = slug,
        Slug = slug,
        IconUrl = iconUrl,
        TwitchGameId = twitchId,
        IsCustomIcon = customIcon,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static TwitchGame Tw(string id, string template) => new(id, $"name-{id}", template);

    [TestMethod]
    public async Task Refresh_SkipsGamesMarkedCustomIcon_EvenWithTwitchId()
    {
        // The whole point of the checkbox: refresher must leave these alone.
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { Make(1, "stardew", "509658", customIcon: true, iconUrl: "manual.png") });

        var count = await _sut.RefreshAsync(CancellationToken.None);

        count.Should().Be(0);
        _twitch.Verify(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _games.Verify(g => g.UpdateIconAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Refresh_SkipsGamesWithoutTwitchId()
    {
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { Make(1, "homemade", twitchId: null, customIcon: false) });

        var count = await _sut.RefreshAsync(CancellationToken.None);

        count.Should().Be(0);
        _twitch.Verify(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Refresh_NoCandidates_DoesNotHitTwitch()
    {
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Game>());

        await _sut.RefreshAsync(CancellationToken.None);

        _twitch.Verify(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Refresh_UpdatesIconForEligibleGame()
    {
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { Make(1, "stardew", "509658", customIcon: false) });
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { Tw("509658", "https://cdn/box/{width}x{height}.jpg") });

        var count = await _sut.RefreshAsync(CancellationToken.None);

        count.Should().Be(1);
        _games.Verify(g => g.UpdateIconAsync(1, "https://cdn/box/285x380.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Refresh_SkipsUpdateWhenIconAlreadyMatches()
    {
        const string url = "https://cdn/box/285x380.jpg";
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { Make(1, "stardew", "509658", customIcon: false, iconUrl: url) });
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[] { Tw("509658", "https://cdn/box/{width}x{height}.jpg") });

        var count = await _sut.RefreshAsync(CancellationToken.None);

        count.Should().Be(0);
        _games.Verify(g => g.UpdateIconAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Refresh_OnlySendsEligibleIdsToTwitch()
    {
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[]
              {
                  Make(1, "stardew",   "509658", customIcon: false),
                  Make(2, "manual",    "111",    customIcon: true),   // skipped
                  Make(3, "no-twitch", null,     customIcon: false),  // skipped
                  Make(4, "minecraft", "27471",  customIcon: false),
              });
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<TwitchGame>());

        await _sut.RefreshAsync(CancellationToken.None);

        _twitch.Verify(t => t.GetGamesByIdAsync(
            It.Is<IReadOnlyCollection<string>>(ids =>
                ids.Count == 2 && ids.Contains("509658") && ids.Contains("27471")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Refresh_BatchesAt100PerHelixCall()
    {
        var rows = Enumerable.Range(1, 250)
            .Select(i => Make(i, $"g{i}", twitchId: i.ToString(), customIcon: false))
            .ToArray();
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<TwitchGame>());

        await _sut.RefreshAsync(CancellationToken.None);

        // 250 ids → 100 + 100 + 50 = three calls
        _twitch.Verify(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [TestMethod]
    public async Task Refresh_TwitchBatchFails_ContinuesWithRemainingBatches()
    {
        var rows = Enumerable.Range(1, 150)
            .Select(i => Make(i, $"g{i}", twitchId: i.ToString(), customIcon: false))
            .ToArray();
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        var calls = 0;
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyCollection<string> ids, CancellationToken _) =>
               {
                   calls++;
                   if (calls == 1) throw new HttpRequestException("Twitch is grumpy");
                   return ids.Select(id => Tw(id, "https://cdn/{width}x{height}.jpg")).ToArray();
               });

        var count = await _sut.RefreshAsync(CancellationToken.None);

        // First batch (100) is dropped, second batch (50) succeeds and updates.
        count.Should().Be(50);
    }

    [TestMethod]
    public async Task Refresh_RepoUpdateFails_DoesNotAbortRest()
    {
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[]
              {
                  Make(1, "stardew",   "509658", customIcon: false),
                  Make(2, "minecraft", "27471",  customIcon: false),
              });
        _twitch.Setup(t => t.GetGamesByIdAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new[]
               {
                   Tw("509658", "https://cdn/a/{width}x{height}.jpg"),
                   Tw("27471",  "https://cdn/b/{width}x{height}.jpg"),
               });
        _games.Setup(g => g.UpdateIconAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("DB hiccup"));
        _games.Setup(g => g.UpdateIconAsync(2, It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var count = await _sut.RefreshAsync(CancellationToken.None);

        count.Should().Be(1);
        _games.Verify(g => g.UpdateIconAsync(2, "https://cdn/b/285x380.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void RenderBoxArt_ReplacesWidthAndHeightPlaceholders()
    {
        GameIconRefresher.RenderBoxArt("https://cdn/x/{width}x{height}.jpg")
            .Should().Be("https://cdn/x/285x380.jpg");
    }
}
