using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;

namespace QuebecsCave.Web.Auth;

/// <summary>
/// Bridges the cookie principal across Blazor's static-prerender → interactive-Server
/// boundary.
///
/// The problem: an <c>@rendermode InteractiveServer</c> page renders twice. On the
/// first pass the request HttpContext is alive and the cookie principal is
/// available; on the second pass a fresh component instance starts in a
/// SignalR circuit with no HttpContext to read from. Without ferrying the
/// principal across, &lt;AuthorizeView&gt; / [CascadingParameter] AuthState
/// flips to anonymous on the interactive render and any role-gated UI
/// disables itself.
///
/// The solution: during prerender we read HttpContext.User and
/// <see cref="PersistentComponentState.PersistAsJson"/> the relevant claims
/// into the response HTML as a state blob. The interactive circuit picks it
/// up via <c>TryTakeFromJson</c> and reconstructs the principal. From the
/// component's point of view AuthState is identical across both phases.
/// </summary>
public sealed class PersistingAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private const string PersistKey = "QuebecsCave.AuthUser.v1";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PersistentComponentState _persistentState;
    private readonly PersistingComponentStateSubscription _subscription;
    private Task<AuthenticationState>? _cached;

    public PersistingAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor,
        PersistentComponentState persistentState)
    {
        _httpContextAccessor = httpContextAccessor;
        _persistentState = persistentState;
        _subscription = persistentState.RegisterOnPersisting(PersistAsync, RenderMode.InteractiveServer);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        // Interactive phase: pick up what prerender persisted.
        if (_persistentState.TryTakeFromJson<PersistedUser>(PersistKey, out var persisted) && persisted is not null)
        {
            _cached = Task.FromResult(new AuthenticationState(persisted.ToPrincipal()));
            return _cached;
        }

        // Prerender phase (HttpContext is alive) or anonymous request.
        var principal = _httpContextAccessor.HttpContext?.User
            ?? new ClaimsPrincipal(new ClaimsIdentity());
        _cached = Task.FromResult(new AuthenticationState(principal));
        return _cached;
    }

    private async Task PersistAsync()
    {
        var state = await GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        _persistentState.PersistAsJson(PersistKey, PersistedUser.From(state.User));
    }

    public void Dispose() => _subscription.Dispose();
}

/// <summary>
/// Wire-format for the auth principal. Preserves multi-value claims (e.g. multiple
/// <c>role</c> entries) by using an ordered list rather than a dictionary.
/// </summary>
public sealed class PersistedUser
{
    public string AuthenticationType { get; set; } = "Cookie";
    public List<PersistedClaim> Claims { get; set; } = new();

    public static PersistedUser From(ClaimsPrincipal principal)
    {
        var user = new PersistedUser();
        if (principal.Identity is ClaimsIdentity identity && !string.IsNullOrEmpty(identity.AuthenticationType))
        {
            user.AuthenticationType = identity.AuthenticationType;
        }
        user.Claims = principal.Claims
            .Select(c => new PersistedClaim { Type = c.Type, Value = c.Value })
            .ToList();
        return user;
    }

    public ClaimsPrincipal ToPrincipal()
    {
        var identity = new ClaimsIdentity(
            Claims.Select(c => new Claim(c.Type, c.Value)),
            AuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}

public sealed class PersistedClaim
{
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
}
