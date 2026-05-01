using QuebecsCave.Core.Time;

namespace QuebecsCave.Services.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
