using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QuebecsCave.Web.Auth;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = values.ToString();
        if (string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty API key"));
        }

        var matched = false;
        foreach (var configured in Options.ServiceKeys)
        {
            if (FixedTimeEquals(presented, configured))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key"));
        }

        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, $"service:{keyHash[..12]}"),
            new Claim("service_key_hash", keyHash),
        };
        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
