namespace QuebecsCave.Core.Time;

/// <summary>
/// Abstraction over system time. Always returns a DateTimeOffset so the
/// originating offset is preserved in storage (datetimeoffset columns).
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
