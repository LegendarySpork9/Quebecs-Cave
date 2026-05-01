using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Identity;
using QuebecsCave.Services.Tests.TestUtilities;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Tests.Identity;

[TestClass]
public sealed class IdentityServiceTests
{
    private const string StreamerTwitchId = "68000893";
    private static readonly DateTimeOffset Now = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);

    private Mock<IUserRepository> _users = null!;
    private Mock<ITwitchTokenRepository> _tokens = null!;
    private Mock<IRoleResolver> _roles = null!;
    private FakeClock _clock = null!;
    private IdentityService _sut = null!;

    [TestInitialize]
    public void SetUp()
    {
        _users = new Mock<IUserRepository>();
        _tokens = new Mock<ITwitchTokenRepository>();
        _roles = new Mock<IRoleResolver>();
        _clock = new FakeClock(Now);

        _users.Setup(u => u.UpsertFromTwitchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string twitchId, string login, string display, string? avatar, DateTimeOffset _, CancellationToken __) => new User
            {
                UserId = 42,
                TwitchUserId = twitchId,
                TwitchLogin = login,
                DisplayName = display,
                AvatarUrl = avatar,
                ThemePreference = "dark",
                TimeZoneId = "Europe/London",
            });

        _roles.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { RoleName.Viewer });

        _sut = new IdentityService(
            _users.Object,
            _tokens.Object,
            _roles.Object,
            Options.Create(new TwitchOptions { StreamerUserId = StreamerTwitchId }),
            _clock);
    }

    private static TwitchUser MakeUser(string id, string login = "longlivequebec", string? avatar = "https://cdn/me.png") =>
        new(id, login, "LongLiveQuebec", "user@example.com", avatar ?? "", "Bio");

    [TestMethod]
    public async Task ProcessSignIn_NonStreamer_DoesNotPersistTokens()
    {
        await _sut.ProcessTwitchSignInAsync(
            MakeUser("99999"),
            accessToken: "at",
            refreshToken: "rt",
            expiresInSeconds: 3600,
            scopes: new[] { "user:read" },
            CancellationToken.None);

        _tokens.Verify(t => t.UpsertAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProcessSignIn_Streamer_PersistsTokensWithExpiryDerivedFromClock()
    {
        await _sut.ProcessTwitchSignInAsync(
            MakeUser(StreamerTwitchId),
            accessToken: "access",
            refreshToken: "refresh",
            expiresInSeconds: 3600,
            scopes: new[] { "user:read", "moderator:read:chatters" },
            CancellationToken.None);

        _tokens.Verify(t => t.UpsertAsync(
            42,
            "access",
            "refresh",
            Now.AddSeconds(3600),
            "user:read moderator:read:chatters",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessSignIn_Streamer_ClampsTinyExpiryToFloor()
    {
        // Twitch occasionally returns very small expires_in values; we floor at
        // 60s so the refresh path has a sane window.
        await _sut.ProcessTwitchSignInAsync(
            MakeUser(StreamerTwitchId),
            accessToken: "a",
            refreshToken: "r",
            expiresInSeconds: 5,
            scopes: Array.Empty<string>(),
            CancellationToken.None);

        _tokens.Verify(t => t.UpsertAsync(
            42, "a", "r", Now.AddSeconds(60), "", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessSignIn_Streamer_NoTokens_SkipsTokenPersistence()
    {
        await _sut.ProcessTwitchSignInAsync(
            MakeUser(StreamerTwitchId),
            accessToken: null,
            refreshToken: null,
            expiresInSeconds: 3600,
            scopes: Array.Empty<string>(),
            CancellationToken.None);

        _tokens.Verify(t => t.UpsertAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProcessSignIn_DisplayNameFallsBackToLoginWhenEmpty()
    {
        var user = new TwitchUser("123", "loginname", "", null, "", null);

        await _sut.ProcessTwitchSignInAsync(user, null, null, 0, Array.Empty<string>(), CancellationToken.None);

        _users.Verify(u => u.UpsertFromTwitchAsync(
            "123", "loginname", "loginname", null, Now, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessSignIn_EmptyAvatar_ConvertedToNull()
    {
        var user = new TwitchUser("123", "login", "Display", null, "", null);

        await _sut.ProcessTwitchSignInAsync(user, null, null, 0, Array.Empty<string>(), CancellationToken.None);

        _users.Verify(u => u.UpsertFromTwitchAsync(
            "123", "login", "Display", null, Now, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessSignIn_ResultIncludesResolvedRoles()
    {
        _roles.Setup(r => r.ResolveAsync(StreamerTwitchId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { RoleName.Viewer, RoleName.Streamer, RoleName.Moderator });

        var result = await _sut.ProcessTwitchSignInAsync(
            MakeUser(StreamerTwitchId),
            "a", "r", 3600, Array.Empty<string>(),
            CancellationToken.None);

        result.UserId.Should().Be(42);
        result.TwitchUserId.Should().Be(StreamerTwitchId);
        result.Roles.Should().BeEquivalentTo(new[] { RoleName.Viewer, RoleName.Streamer, RoleName.Moderator });
    }
}
