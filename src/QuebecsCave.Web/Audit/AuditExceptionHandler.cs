using Microsoft.AspNetCore.Diagnostics;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Time;

namespace QuebecsCave.Web.Audit;

/// <summary>
/// Captures unhandled exceptions from the request pipeline into ErrorLog
/// (Source = 'Api' for /api/* paths, otherwise 'Website'). Returns false
/// so ASP.NET continues with its default error rendering.
/// </summary>
public sealed class AuditExceptionHandler : IExceptionHandler
{
    private readonly IErrorLogger _errorLogger;
    private readonly IErrorStatusLookup _statusLookup;
    private readonly IClock _clock;
    private readonly ILogger<AuditExceptionHandler> _logger;

    public AuditExceptionHandler(
        IErrorLogger errorLogger,
        IErrorStatusLookup statusLookup,
        IClock clock,
        ILogger<AuditExceptionHandler> logger)
    {
        _errorLogger = errorLogger;
        _statusLookup = statusLookup;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            // UseExceptionHandler may have rewritten the path to "/Error" by the
            // time TryHandleAsync runs. The pre-rewrite path lives on the
            // exception-handler feature.
            var feature = httpContext.Features.Get<IExceptionHandlerPathFeature>();
            var path = feature?.Path ?? httpContext.Request.Path.Value ?? "";
            var source = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                ? AuditSource.Api
                : AuditSource.Website;

            var statusId = await _statusLookup.GetIdAsync(ErrorStatusName.Open, cancellationToken);

            var context = System.Text.Json.JsonSerializer.Serialize(new
            {
                method = httpContext.Request.Method,
                path,
                queryString = httpContext.Request.QueryString.Value,
                userId = httpContext.User.FindFirst("user_id")?.Value,
                traceId = httpContext.TraceIdentifier,
            });

            _errorLogger.Enqueue(new ErrorLogEntry(
                Source: source,
                ExceptionType: exception.GetType().FullName ?? exception.GetType().Name,
                Message: Truncate(exception.Message, 2000),
                StackTrace: exception.StackTrace,
                Context: context,
                StatusId: statusId,
                GitHubIssueUrl: null,
                AddressedByUserId: null,
                AddressedAt: null,
                Notes: null,
                OccurredAt: _clock.Now));
        }
        catch (Exception captureEx)
        {
            _logger.LogWarning(captureEx, "Failed to write ErrorLog entry; continuing.");
        }

        // Let ASP.NET's default UseExceptionHandler/Error page run.
        return false;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}
