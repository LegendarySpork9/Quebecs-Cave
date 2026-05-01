using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Time;
using QuebecsCave.Web.Audit;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/twitch/login", (string? returnUrl, HttpContext http) =>
        {
            var props = new AuthenticationProperties
            {
                RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl,
                IsPersistent = true,
            };
            return Results.Challenge(props, new[] { TwitchAuthDefaults.Scheme });
        });

        app.MapPost("/auth/logout", async (
            HttpContext http,
            IClock clock,
            IWebsiteEventLogger websiteEvents) =>
        {
            var userIdClaim = http.User.FindFirst("user_id")?.Value;
            int.TryParse(userIdClaim, out var userId);

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            websiteEvents.Enqueue(new WebsiteEventEntry(
                Action: WebsiteAction.Logout,
                Path: "/auth/logout",
                UserId: userId == 0 ? null : userId,
                IpHash: IpHasher.Hash(http.Connection.RemoteIpAddress),
                Detail: null,
                OccurredAt: clock.Now));

            return Results.Redirect("/");
        }).DisableAntiforgery();

        return app;
    }
}
