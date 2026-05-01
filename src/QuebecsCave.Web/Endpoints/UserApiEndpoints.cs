using System.Security.Claims;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Web.Audit;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Endpoints;

public sealed record MeDto(
    int UserId,
    string TwitchUserId,
    string TwitchLogin,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Roles);

public sealed record PreferencesDto(string? Theme, string? TimeZoneId);

public static class UserApiEndpoints
{
    public static IEndpointRouteBuilder MapUserApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users/me")
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly, AuthorizationPolicies.AuthenticatedViewer)
            .DisableAntiforgery();

        group.MapGet("", (HttpContext http) =>
        {
            var dto = BuildMe(http.User);
            return dto is null ? Results.Unauthorized() : Results.Ok(dto);
        });

        group.MapGet("/preferences", async (
            HttpContext http,
            IUserRepository users,
            CancellationToken cancellationToken) =>
        {
            var userId = ResolveUserId(http.User);
            if (userId is null) return Results.Unauthorized();

            var user = await users.GetByIdAsync(userId.Value, cancellationToken);
            if (user is null) return Results.NotFound();

            return Results.Ok(new PreferencesDto(user.ThemePreference, user.TimeZoneId));
        });

        group.MapPut("/preferences", async (
            PreferencesDto prefs,
            HttpContext http,
            IUserRepository users,
            IClock clock,
            IWebsiteEventLogger websiteEvents,
            CancellationToken cancellationToken) =>
        {
            var userId = ResolveUserId(http.User);
            if (userId is null) return Results.Unauthorized();

            var theme = NormaliseTheme(prefs.Theme);
            var timeZone = NormaliseTimeZone(prefs.TimeZoneId);

            await users.UpdatePreferencesAsync(userId.Value, theme, timeZone, cancellationToken);

            websiteEvents.Enqueue(new WebsiteEventEntry(
                Action: WebsiteAction.ThemeChange,
                Path: "/api/users/me/preferences",
                UserId: userId,
                IpHash: IpHasher.Hash(http.Connection.RemoteIpAddress),
                Detail: System.Text.Json.JsonSerializer.Serialize(new { theme, timeZone }),
                OccurredAt: clock.Now));

            return Results.Ok(new PreferencesDto(theme, timeZone));
        });

        return app;
    }

    internal static MeDto? BuildMe(ClaimsPrincipal user)
    {
        var userId = ResolveUserId(user);
        if (userId is null) return null;
        return new MeDto(
            UserId: userId.Value,
            TwitchUserId: user.FindFirstValue(TwitchClaimTypes.TwitchUserId) ?? "",
            TwitchLogin: user.FindFirstValue(TwitchClaimTypes.TwitchLogin) ?? "",
            DisplayName: user.FindFirstValue(ClaimTypes.Name) ?? "",
            AvatarUrl: user.FindFirstValue("avatar_url"),
            Roles: user.FindAll(TwitchClaimTypes.Role).Select(c => c.Value).Distinct().ToArray());
    }

    private static int? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("user_id");
        return int.TryParse(raw, out var id) ? id : null;
    }

    private static string? NormaliseTheme(string? theme)
    {
        if (string.IsNullOrEmpty(theme)) return null;
        return theme switch
        {
            ThemePreference.Dark or ThemePreference.Light => theme,
            _ => null,
        };
    }

    private static string? NormaliseTimeZone(string? tz)
    {
        if (string.IsNullOrEmpty(tz)) return null;
        try
        {
            // Validate against system zone list — accepts both Windows IDs and IANA on .NET 10.
            TimeZoneInfo.FindSystemTimeZoneById(tz);
            return tz;
        }
        catch
        {
            return null;
        }
    }
}
