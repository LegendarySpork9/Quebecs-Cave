namespace QuebecsCave.Core.Audit;

/// <summary>
/// Source of an audit event — used in ErrorLog.Source and a few other places.
/// </summary>
public static class AuditSource
{
    public const string Api = "Api";
    public const string Website = "Website";
    public const string Downloader = "Downloader";
    public const string BackgroundService = "BackgroundService";
}

public static class WebsiteAction
{
    public const string PageView = "PageView";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string Reaction = "Reaction";
    public const string ThemeChange = "ThemeChange";
    public const string Search = "Search";
}

public static class DownloaderStage
{
    public const string Poll = "Poll";
    public const string TwitchAuth = "TwitchAuth";
    public const string TwitchVodList = "TwitchVodList";
    public const string Download = "Download";
    public const string Upload = "Upload";
    public const string Ingest = "Ingest";
    public const string Heartbeat = "Heartbeat";
}

public sealed record ApiCallLogEntry(
    string Method,
    string Path,
    string? QueryString,
    string? RequestBody,
    int ResponseStatus,
    string? ResponseBody,
    int? UserId,
    byte[] IpHash,
    byte[]? ServiceKeyHash,
    int DurationMs,
    DateTimeOffset CalledAt,
    int? RelatedAuditId);

public sealed record DownloaderEventEntry(
    string Stage,
    string? TwitchVodId,
    bool Success,
    int? DurationMs,
    string? Payload,
    string? Message,
    DateTimeOffset OccurredAt);

public sealed record WebsiteEventEntry(
    string Action,
    string? Path,
    int? UserId,
    byte[] IpHash,
    string? Detail,
    DateTimeOffset OccurredAt);

public sealed record ErrorLogEntry(
    string Source,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? Context,
    int StatusId,
    string? GitHubIssueUrl,
    int? AddressedByUserId,
    DateTimeOffset? AddressedAt,
    string? Notes,
    DateTimeOffset OccurredAt);
