using FluentAssertions;
using Moq;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Services.Streams;
using DomainStream = QuebecsCave.Core.Domain.Stream;

namespace QuebecsCave.Services.Tests.Streams;

[TestClass]
public sealed class StreamServiceTests
{
    private Mock<IStreamRepository> _streams = null!;
    private Mock<IGameRepository> _games = null!;
    private StreamService _sut = null!;

    [TestInitialize]
    public void SetUp()
    {
        _streams = new Mock<IStreamRepository>();
        _games = new Mock<IGameRepository>();
        _sut = new StreamService(_streams.Object, _games.Object);
    }

    private static DomainStream MakeStream(int id, int gameId, string title = "VOD") => new()
    {
        StreamId = id,
        Title = title,
        Description = null,
        GameId = gameId,
        StreamedAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
        DurationSeconds = 7200,
        VideoUrl = $"https://vod/{id}",
        ThumbnailUrl = null,
        TwitchVodId = null,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Game MakeGame(int id, string name, string slug, string? icon = null) => new()
    {
        GameId = id,
        Name = name,
        Slug = slug,
        IconUrl = icon,
    };

    [TestMethod]
    public async Task GetRecent_EnrichesStreamsWithMatchingGameMetadata()
    {
        var stream = MakeStream(1, gameId: 10, title: "Stardew run");
        _streams.Setup(s => s.RecentAsync(20, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { stream });
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MakeGame(10, "Stardew Valley", "stardew-valley", "https://cdn/stardew.png") });

        var result = await _sut.GetRecentAsync(20, CancellationToken.None);

        result.Should().HaveCount(1);
        var card = result[0];
        card.StreamId.Should().Be(1);
        card.Title.Should().Be("Stardew run");
        card.GameId.Should().Be(10);
        card.GameName.Should().Be("Stardew Valley");
        card.GameSlug.Should().Be("stardew-valley");
        card.GameIconUrl.Should().Be("https://cdn/stardew.png");
    }

    [TestMethod]
    public async Task GetRecent_MissingGame_FallsBackToUnknownLabel()
    {
        var stream = MakeStream(1, gameId: 999);
        _streams.Setup(s => s.RecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { stream });
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Game>());

        var result = await _sut.GetRecentAsync(20, CancellationToken.None);

        var card = result.Single();
        card.GameName.Should().Be("Unknown game");
        card.GameSlug.Should().Be("");
        card.GameIconUrl.Should().BeNull();
    }

    [TestMethod]
    public async Task GetRecent_NoStreams_DoesNotQueryGames()
    {
        _streams.Setup(s => s.RecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<DomainStream>());

        var result = await _sut.GetRecentAsync(20, CancellationToken.None);

        result.Should().BeEmpty();
        _games.Verify(g => g.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Search_ReturnsPageWithCountAndPagingPassThrough()
    {
        var filter = new StreamFilter(GameId: null, From: null, To: null, Search: "stardew", Skip: 40, Take: 20);
        _streams.Setup(s => s.ListAsync(filter, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { MakeStream(1, 10), MakeStream(2, 10) });
        _streams.Setup(s => s.CountAsync(filter, It.IsAny<CancellationToken>()))
                .ReturnsAsync(57);
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MakeGame(10, "Stardew", "stardew") });

        var page = await _sut.SearchAsync(filter, CancellationToken.None);

        page.Total.Should().Be(57);
        page.Skip.Should().Be(40);
        page.Take.Should().Be(20);
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(c => c.GameName == "Stardew");
    }

    [TestMethod]
    public async Task GetById_StreamMissing_ReturnsNullWithoutQueryingGames()
    {
        _streams.Setup(s => s.GetByIdAsync(404, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DomainStream?)null);

        var card = await _sut.GetByIdAsync(404, CancellationToken.None);

        card.Should().BeNull();
        _games.Verify(g => g.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetById_StreamFound_ReturnsEnrichedCard()
    {
        _streams.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeStream(1, 10, "Stardew run"));
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MakeGame(10, "Stardew Valley", "stardew-valley") });

        var card = await _sut.GetByIdAsync(1, CancellationToken.None);

        card.Should().NotBeNull();
        card!.GameName.Should().Be("Stardew Valley");
    }

    [TestMethod]
    public async Task Related_FiltersOutTheSourceStreamAndCapsAtTake()
    {
        // Repo returns take+1 items; the source stream must be filtered out and
        // the result must respect the requested take.
        var fromRepo = new[]
        {
            MakeStream(1, 10), // source — should be excluded
            MakeStream(2, 10),
            MakeStream(3, 10),
            MakeStream(4, 10),
        };
        _streams.Setup(s => s.ListAsync(
                    It.Is<StreamFilter>(f => f.GameId == 10 && f.Take == 4),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(fromRepo);
        _games.Setup(g => g.ListAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MakeGame(10, "Stardew", "stardew") });

        var related = await _sut.RelatedAsync(streamId: 1, gameId: 10, take: 3, CancellationToken.None);

        related.Should().HaveCount(3);
        related.Select(r => r.StreamId).Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }
}
