using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Services.Identity;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Tests.Identity;

[TestClass]
public sealed class RoleResolverTests
{
    private const string StreamerId = "68000893";
    private const string OtherUserId = "12345678";

    private Mock<IModeratorCacheRepository> _mods = null!;
    private Mock<IDeveloperRepository> _devs = null!;
    private RoleResolver _sut = null!;

    [TestInitialize]
    public void SetUp()
    {
        _mods = new Mock<IModeratorCacheRepository>();
        _devs = new Mock<IDeveloperRepository>();
        _mods.Setup(m => m.IsModeratorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _devs.Setup(d => d.IsDeveloperAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _sut = new RoleResolver(
            _mods.Object,
            _devs.Object,
            Options.Create(new TwitchOptions { StreamerUserId = StreamerId }));
    }

    [TestMethod]
    public async Task Resolve_PlainViewer_ReturnsViewerOnly()
    {
        var roles = await _sut.ResolveAsync(OtherUserId, CancellationToken.None);
        roles.Should().BeEquivalentTo(new[] { RoleName.Viewer });
    }

    [TestMethod]
    public async Task Resolve_MatchingStreamer_AddsStreamerRole()
    {
        var roles = await _sut.ResolveAsync(StreamerId, CancellationToken.None);
        roles.Should().BeEquivalentTo(new[] { RoleName.Viewer, RoleName.Streamer });
    }

    [TestMethod]
    public async Task Resolve_ModeratorCacheHit_AddsModeratorRole()
    {
        _mods.Setup(m => m.IsModeratorAsync(OtherUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var roles = await _sut.ResolveAsync(OtherUserId, CancellationToken.None);

        roles.Should().BeEquivalentTo(new[] { RoleName.Viewer, RoleName.Moderator });
    }

    [TestMethod]
    public async Task Resolve_DeveloperHit_AddsDeveloperRole()
    {
        _devs.Setup(d => d.IsDeveloperAsync(OtherUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var roles = await _sut.ResolveAsync(OtherUserId, CancellationToken.None);

        roles.Should().BeEquivalentTo(new[] { RoleName.Viewer, RoleName.Developer });
    }

    [TestMethod]
    public async Task Resolve_StreamerWhoIsAlsoModAndDev_GetsAllRoles()
    {
        _mods.Setup(m => m.IsModeratorAsync(StreamerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _devs.Setup(d => d.IsDeveloperAsync(StreamerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var roles = await _sut.ResolveAsync(StreamerId, CancellationToken.None);

        roles.Should().BeEquivalentTo(new[]
        {
            RoleName.Viewer, RoleName.Streamer, RoleName.Moderator, RoleName.Developer
        });
    }

    [TestMethod]
    public async Task Resolve_ModAndDeveloperButNotStreamer_GetsBothNonStreamerRoles()
    {
        _mods.Setup(m => m.IsModeratorAsync(OtherUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _devs.Setup(d => d.IsDeveloperAsync(OtherUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var roles = await _sut.ResolveAsync(OtherUserId, CancellationToken.None);

        roles.Should().BeEquivalentTo(new[]
        {
            RoleName.Viewer, RoleName.Moderator, RoleName.Developer
        });
        roles.Should().NotContain(RoleName.Streamer);
    }

    [TestMethod]
    public async Task Resolve_StreamerIdNotConfigured_DoesNotAssignStreamer()
    {
        var sut = new RoleResolver(
            _mods.Object,
            _devs.Object,
            Options.Create(new TwitchOptions { StreamerUserId = "" }));

        var roles = await sut.ResolveAsync("anything", CancellationToken.None);

        roles.Should().BeEquivalentTo(new[] { RoleName.Viewer });
    }

    [TestMethod]
    public async Task Resolve_StreamerComparisonIsCaseSensitive()
    {
        // Twitch IDs are numeric, but the comparison is Ordinal — guard against
        // a future change that swaps it for case-insensitive matching.
        var sut = new RoleResolver(
            _mods.Object,
            _devs.Object,
            Options.Create(new TwitchOptions { StreamerUserId = "ABC" }));

        var roles = await sut.ResolveAsync("abc", CancellationToken.None);

        roles.Should().NotContain(RoleName.Streamer);
    }
}
