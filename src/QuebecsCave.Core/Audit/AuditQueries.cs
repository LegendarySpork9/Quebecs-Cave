namespace QuebecsCave.Core.Audit;

public sealed record AuditPage<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public sealed record ApiCallLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    int? UserId,
    string? PathContains,
    int? StatusCode,
    string? Method,
    int Skip,
    int Take);

public sealed record DownloaderEventFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Stage,
    bool? Success,
    string? TwitchVodId,
    int Skip,
    int Take);

public sealed record WebsiteEventFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Action,
    int? UserId,
    string? PathContains,
    int Skip,
    int Take);

public sealed record ErrorLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Source,
    int? StatusId,
    string? ExceptionTypeContains,
    string? Search,
    int Skip,
    int Take);

public sealed record ApiCallLogRow(
    long ApiCallLogId,
    string Method,
    string Path,
    string? QueryString,
    int ResponseStatus,
    int? UserId,
    int DurationMs,
    DateTimeOffset CalledAt,
    int? RelatedAuditId,
    string? RequestBody,
    string? ResponseBody);

public sealed record DownloaderEventRow(
    long DownloaderEventId,
    string Stage,
    string? TwitchVodId,
    bool Success,
    int? DurationMs,
    string? Message,
    string? Payload,
    DateTimeOffset OccurredAt);

public sealed record WebsiteEventRow(
    long WebsiteEventId,
    string Action,
    string? Path,
    int? UserId,
    string? Detail,
    DateTimeOffset OccurredAt);

public sealed record ErrorLogRow(
    long ErrorLogId,
    string Source,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? Context,
    int StatusId,
    string StatusName,
    string? GitHubIssueUrl,
    int? AddressedByUserId,
    string? AddressedByLogin,
    DateTimeOffset? AddressedAt,
    string? Notes,
    DateTimeOffset OccurredAt);

public sealed record SchemaVersionRow(
    int Id,
    string ScriptName,
    DateTimeOffset AppliedAt);
