using Microsoft.AspNetCore.Authentication;

namespace QuebecsCave.Web.Auth;

public static class ApiKeyExtensions
{
    public static AuthenticationBuilder AddApiKey(
        this AuthenticationBuilder builder,
        Action<ApiKeyAuthenticationOptions> configure)
    {
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationDefaults.Scheme,
            configure);
    }
}
