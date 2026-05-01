using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Twitch;
using QuebecsCave.Services.Identity;

namespace QuebecsCave.Web.Auth;

/// <summary>
/// Custom event handler for the Twitch OAuth flow. After Twitch returns the
/// access token, fetch /helix/users, upsert into our User table, resolve the
/// additive role set, and stamp claims onto the principal.
/// </summary>
internal static class TwitchOAuthEvents
{
    public static OAuthEvents Build() => new()
    {
        OnCreatingTicket = HandleCreatingTicketAsync,
    };

    private static async Task HandleCreatingTicketAsync(OAuthCreatingTicketContext ctx)
    {
        // Twitch /helix/users requires Authorization: Bearer + Client-Id headers.
        using var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken!);
        req.Headers.Add("Client-Id", ctx.Options.ClientId);

        using var resp = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<UsersResponse>(cancellationToken: ctx.HttpContext.RequestAborted);
        var dto = payload?.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch /helix/users returned no user");

        var twitchUser = new TwitchUser(
            dto.Id ?? "",
            dto.Login ?? "",
            string.IsNullOrEmpty(dto.DisplayName) ? (dto.Login ?? "") : dto.DisplayName,
            string.IsNullOrEmpty(dto.Email) ? null : dto.Email,
            dto.ProfileImageUrl ?? "",
            string.IsNullOrEmpty(dto.Description) ? null : dto.Description);

        var identityService = ctx.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
        var websiteEvents = ctx.HttpContext.RequestServices.GetRequiredService<IWebsiteEventLogger>();
        var clock = ctx.HttpContext.RequestServices.GetRequiredService<QuebecsCave.Core.Time.IClock>();

        var scopes = ctx.TokenResponse.Response?.RootElement.TryGetProperty("scope", out var scopeProp) == true && scopeProp.ValueKind == System.Text.Json.JsonValueKind.Array
            ? scopeProp.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray()
            : Array.Empty<string>();

        var refreshToken = ctx.RefreshToken;
        var expiresInSeconds = (int)(ctx.ExpiresIn?.TotalSeconds ?? 0);

        var result = await identityService.ProcessTwitchSignInAsync(
            twitchUser,
            ctx.AccessToken,
            refreshToken,
            expiresInSeconds,
            scopes,
            ctx.HttpContext.RequestAborted);

        var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()));
        identity.AddClaim(new Claim("user_id", result.UserId.ToString()));
        identity.AddClaim(new Claim(TwitchClaimTypes.TwitchUserId, result.TwitchUserId));
        identity.AddClaim(new Claim(TwitchClaimTypes.TwitchLogin, result.TwitchLogin));
        identity.AddClaim(new Claim(ClaimTypes.Name, result.DisplayName));
        if (!string.IsNullOrEmpty(result.AvatarUrl))
        {
            identity.AddClaim(new Claim("avatar_url", result.AvatarUrl));
        }
        foreach (var role in result.Roles)
        {
            identity.AddClaim(new Claim(TwitchClaimTypes.Role, role));
        }

        websiteEvents.Enqueue(new WebsiteEventEntry(
            Action: WebsiteAction.Login,
            Path: ctx.HttpContext.Request.Path,
            UserId: result.UserId,
            IpHash: Audit.IpHasher.Hash(ctx.HttpContext.Connection.RemoteIpAddress),
            Detail: System.Text.Json.JsonSerializer.Serialize(new { provider = "twitch", roles = result.Roles }),
            OccurredAt: clock.Now));
    }

    private sealed class UsersResponse
    {
        [JsonPropertyName("data")] public List<UserDto>? Data { get; set; }
    }

    private sealed class UserDto
    {
        [JsonPropertyName("id")]                public string? Id { get; set; }
        [JsonPropertyName("login")]             public string? Login { get; set; }
        [JsonPropertyName("display_name")]      public string? DisplayName { get; set; }
        [JsonPropertyName("email")]             public string? Email { get; set; }
        [JsonPropertyName("profile_image_url")] public string? ProfileImageUrl { get; set; }
        [JsonPropertyName("description")]       public string? Description { get; set; }
    }
}
