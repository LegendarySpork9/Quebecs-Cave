using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Repositories;
using QuebecsCave.Core.Time;
using QuebecsCave.Services.Identity;
using QuebecsCave.Web.Audit;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Endpoints;

public static class DevAuthEndpoints
{
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/auth/signin/{key}", async (
            string key,
            HttpContext http,
            IUserRepository users,
            IRoleResolver roles,
            IClock clock,
            IWebsiteEventLogger websiteEvents,
            IOptions<DevAuthOptions> devAuth,
            IHostEnvironment env,
            CancellationToken cancellationToken) =>
        {
            if (!env.IsDevelopment() || !devAuth.Value.Enabled)
            {
                return Results.NotFound();
            }

            var profile = DevAuthProfiles.FindByKey(key);
            if (profile is null) return Results.NotFound();

            var user = await users.UpsertFromTwitchAsync(
                profile.TwitchUserId,
                profile.TwitchLogin,
                profile.DisplayName,
                profile.AvatarUrl,
                clock.Now,
                cancellationToken);

            var resolvedRoles = await roles.ResolveAsync(profile.TwitchUserId, cancellationToken);

            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()));
            identity.AddClaim(new Claim("user_id", user.UserId.ToString()));
            identity.AddClaim(new Claim(TwitchClaimTypes.TwitchUserId, user.TwitchUserId));
            identity.AddClaim(new Claim(TwitchClaimTypes.TwitchLogin, user.TwitchLogin));
            identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
            if (!string.IsNullOrEmpty(user.AvatarUrl))
            {
                identity.AddClaim(new Claim("avatar_url", user.AvatarUrl));
            }
            foreach (var role in resolvedRoles)
            {
                identity.AddClaim(new Claim(TwitchClaimTypes.Role, role));
            }

            var principal = new ClaimsPrincipal(identity);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });

            websiteEvents.Enqueue(new WebsiteEventEntry(
                Action: WebsiteAction.Login,
                Path: "/dev/auth/signin",
                UserId: user.UserId,
                IpHash: IpHasher.Hash(http.Connection.RemoteIpAddress),
                Detail: System.Text.Json.JsonSerializer.Serialize(new { provider = "dev", profile = profile.Key, roles = resolvedRoles }),
                OccurredAt: clock.Now));

            return Results.Redirect("/");
        });

        return app;
    }
}
