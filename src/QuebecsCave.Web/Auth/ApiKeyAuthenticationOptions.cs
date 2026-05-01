using Microsoft.AspNetCore.Authentication;

namespace QuebecsCave.Web.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public IReadOnlyList<string> ServiceKeys { get; set; } = Array.Empty<string>();
}
