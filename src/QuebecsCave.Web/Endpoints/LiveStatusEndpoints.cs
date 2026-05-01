using QuebecsCave.Core.Twitch;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Endpoints;

public static class LiveStatusEndpoints
{
    public static IEndpointRouteBuilder MapLiveStatusEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live-status", (ILiveStatusService live) => Results.Ok(live.Current))
            .RequireAuthorization(AuthorizationPolicies.ApiKeyOnly)
            .DisableAntiforgery();
        return app;
    }
}
