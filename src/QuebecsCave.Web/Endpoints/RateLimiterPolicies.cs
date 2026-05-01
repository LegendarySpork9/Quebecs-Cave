using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace QuebecsCave.Web.Endpoints;

/// <summary>
/// Rate-limiting policies registered globally and applied per-endpoint via
/// <c>RequireRateLimiting</c>. Keys on remote IP; defence-in-depth against
/// noisy/abusive clients (a misbehaving downloader, a bot poking
/// /reactions, etc.).
/// </summary>
public static class RateLimiterPolicies
{
    public const string PerIpPublic = "PerIpPublic";       // 60 / 60s

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(PerIpPublic, ctx =>
        {
            var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
            });
        });
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    }
}
