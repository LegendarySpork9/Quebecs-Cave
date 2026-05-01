using FluentAssertions;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Twitch;

namespace QuebecsCave.Services.Tests.Twitch;

[TestClass]
public sealed class LiveStatusServiceTests
{
    private static LiveStatus Live(string game, string title, int viewers) =>
        new(IsLive: true, GameName: game, Title: title, StartedAt: DateTimeOffset.UnixEpoch, ViewerCount: viewers, CheckedAt: DateTimeOffset.UnixEpoch);

    [TestMethod]
    public void Update_TransitionFromOfflineToLive_FiresChanged()
    {
        var sut = new LiveStatusService();
        var fired = 0;
        sut.Changed += _ => fired++;

        sut.Update(Live("Stardew Valley", "Hi", 100));

        fired.Should().Be(1);
        sut.Current.IsLive.Should().BeTrue();
    }

    [TestMethod]
    public void Update_GameChange_FiresChanged()
    {
        var sut = new LiveStatusService();
        sut.Update(Live("Stardew Valley", "Title", 100));
        var fired = 0;
        sut.Changed += _ => fired++;

        sut.Update(Live("Minecraft", "Title", 100));

        fired.Should().Be(1);
        sut.Current.GameName.Should().Be("Minecraft");
    }

    [TestMethod]
    public void Update_TitleChange_FiresChanged()
    {
        var sut = new LiveStatusService();
        sut.Update(Live("Game", "Old title", 100));
        var fired = 0;
        sut.Changed += _ => fired++;

        sut.Update(Live("Game", "New title", 100));

        fired.Should().Be(1);
    }

    [TestMethod]
    public void Update_ViewerCountChangeOnly_DoesNotFire()
    {
        var sut = new LiveStatusService();
        sut.Update(Live("Game", "Title", 100));
        var fired = 0;
        sut.Changed += _ => fired++;

        sut.Update(Live("Game", "Title", 250));

        fired.Should().Be(0);
        // The snapshot should still be replaced even though the event didn't fire.
        sut.Current.ViewerCount.Should().Be(250);
    }

    [TestMethod]
    public void Update_GoingOffline_FiresChanged()
    {
        var sut = new LiveStatusService();
        sut.Update(Live("Game", "Title", 100));
        var fired = 0;
        sut.Changed += _ => fired++;

        sut.Update(LiveStatus.Offline(DateTimeOffset.UnixEpoch));

        fired.Should().Be(1);
        sut.Current.IsLive.Should().BeFalse();
    }

    [TestMethod]
    public void Current_DefaultsToOffline()
    {
        new LiveStatusService().Current.IsLive.Should().BeFalse();
    }
}
