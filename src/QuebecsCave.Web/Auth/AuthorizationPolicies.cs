using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using QuebecsCave.Core.Domain;

namespace QuebecsCave.Web.Auth;

/// <summary>
/// Two layers of policies:
///
/// <para><b>ApiKeyOnly</b> — requires the X-Api-Key scheme. Used on every
/// /api/* group so anonymous JSON callers (e.g. the downloader) get a
/// clean 401 if the key is missing.</para>
///
/// <para><b>Role policies</b> — Streamer / ReportsAccess / AuditAccess /
/// AuthenticatedViewer — operate on cookie-authenticated principals only.
/// They do NOT require the service-key claim, so Blazor pages (which only
/// have the cookie) can apply them via [Authorize(Policy=...)] or
/// &lt;AuthorizeView&gt;.</para>
///
/// <para>Where an API endpoint needs both — JSON caller using a key AND
/// acting on behalf of a logged-in user — chain them:
/// <c>RequireAuthorization(AuthorizationPolicies.ApiKeyOnly, AuthorizationPolicies.Streamer)</c>.</para>
/// </summary>
public static class AuthorizationPolicies
{
    public const string ApiKeyOnly = "ApiKeyOnly";
    public const string AuthenticatedViewer = "AuthenticatedViewer";
    public const string ReportsAccess = "ReportsAccess";
    public const string AuditAccess = "AuditAccess";
    public const string Streamer = "Streamer";

    public static void Add(AuthorizationOptions options)
    {
        options.AddPolicy(ApiKeyOnly, p => p
            .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser());

        options.AddPolicy(AuthenticatedViewer, p => p
            .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == TwitchClaimTypes.TwitchUserId)));

        options.AddPolicy(ReportsAccess, p => p
            .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyRole(ctx, RoleName.Streamer, RoleName.Moderator)));

        options.AddPolicy(AuditAccess, p => p
            .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyRole(ctx, RoleName.Streamer, RoleName.Moderator, RoleName.Developer)));

        options.AddPolicy(Streamer, p => p
            .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyRole(ctx, RoleName.Streamer)));
    }

    private static bool HasAnyRole(AuthorizationHandlerContext ctx, params string[] roles)
    {
        foreach (var role in roles)
        {
            if (ctx.User.HasClaim(TwitchClaimTypes.Role, role))
            {
                return true;
            }
        }
        return false;
    }
}
