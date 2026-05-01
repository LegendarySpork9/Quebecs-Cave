using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Services.Audit;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Audit;

/// <summary>
/// Captures every /api/* request as an ApiCallLogEntry and enqueues it on
/// the async writer. Does not block the request. Body capture is opt-in via
/// <see cref="ApiCallLogOptions.CaptureBodies"/> — defaults to off in prod,
/// on in dev.
/// </summary>
public sealed class ApiCallLogMiddleware
{
    private const int MaxBodyLength = 4 * 1024;

    private readonly RequestDelegate _next;
    private readonly IApiCallLogger _logger;
    private readonly IClock _clock;
    private readonly IOptionsMonitor<AuditOptions> _options;

    public ApiCallLogMiddleware(
        RequestDelegate next,
        IApiCallLogger logger,
        IClock clock,
        IOptionsMonitor<AuditOptions> options)
    {
        _next = next;
        _logger = logger;
        _clock = clock;
        _options = options;
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var opts = _options.CurrentValue.ApiCallLog;
        if (!opts.Enabled)
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        string? capturedRequestBody = null;
        Stream? originalResponseBody = null;
        MemoryStream? captureBuffer = null;

        if (opts.CaptureBodies)
        {
            context.Request.EnableBuffering();
            capturedRequestBody = await ReadAndRewindAsync(context.Request.Body);

            originalResponseBody = context.Response.Body;
            captureBuffer = new MemoryStream();
            context.Response.Body = captureBuffer;
        }

        Exception? thrown = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            thrown = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            string? capturedResponseBody = null;
            if (opts.CaptureBodies && originalResponseBody is not null && captureBuffer is not null)
            {
                captureBuffer.Position = 0;
                capturedResponseBody = await new StreamReader(captureBuffer).ReadToEndAsync();
                captureBuffer.Position = 0;
                await captureBuffer.CopyToAsync(originalResponseBody);
                context.Response.Body = originalResponseBody;
            }

            try
            {
                var entry = new ApiCallLogEntry(
                    Method: context.Request.Method,
                    Path: path,
                    QueryString: string.IsNullOrEmpty(context.Request.QueryString.Value)
                        ? null
                        : BodySanitizer.Sanitize(context.Request.QueryString.Value),
                    RequestBody: capturedRequestBody is null ? null : BodySanitizer.Sanitize(capturedRequestBody),
                    ResponseStatus: context.Response.StatusCode,
                    ResponseBody: capturedResponseBody is null ? null : BodySanitizer.Sanitize(capturedResponseBody),
                    UserId: ResolveUserId(context.User),
                    IpHash: IpHasher.Hash(context.Connection.RemoteIpAddress),
                    ServiceKeyHash: ResolveServiceKeyHash(context.User),
                    DurationMs: (int)sw.ElapsedMilliseconds,
                    CalledAt: _clock.Now,
                    RelatedAuditId: null);
                _logger.Enqueue(entry);
            }
            catch
            {
                // Never let audit overhead corrupt a response.
            }

            // Suppress warning about thrown — we re-throw above; this just avoids unused warning.
            _ = thrown;
        }
    }

    private static async Task<string?> ReadAndRewindAsync(Stream body)
    {
        if (!body.CanRead || !body.CanSeek) return null;
        var buf = new byte[MaxBodyLength];
        var total = 0;
        int read;
        while (total < buf.Length && (read = await body.ReadAsync(buf.AsMemory(total, buf.Length - total))) > 0)
        {
            total += read;
        }
        body.Position = 0;
        if (total == 0) return null;
        return System.Text.Encoding.UTF8.GetString(buf, 0, total);
    }

    private static int? ResolveUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue("user_id");
        return int.TryParse(v, out var id) ? id : null;
    }

    private static byte[]? ResolveServiceKeyHash(ClaimsPrincipal user)
    {
        var hex = user.FindFirstValue(TwitchClaimTypes.ServiceKeyHash);
        return IpHasher.HashFromHeader(hex);
    }
}

public static class ApiCallLogMiddlewareExtensions
{
    public static IApplicationBuilder UseApiCallLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiCallLogMiddleware>();
}
