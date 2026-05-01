namespace QuebecsCave.Services.Audit;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    public ApiCallLogOptions ApiCallLog { get; set; } = new();
    public int RetentionDays { get; set; } = 30;
    public int RetentionRunHourLocal { get; set; } = 3;
}

public sealed class ApiCallLogOptions
{
    public bool Enabled { get; set; } = true;
    public bool CaptureBodies { get; set; }
}
