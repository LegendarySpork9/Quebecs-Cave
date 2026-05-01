using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Tests.TestUtilities;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
}
