using System.Security.Claims;
using FluentAssertions;
using QuebecsCave.Web.Auth;

namespace QuebecsCave.Web.Tests.Auth;

[TestClass]
public sealed class PersistedUserTests
{
    [TestMethod]
    public void From_PreservesAuthenticationType()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var persisted = PersistedUser.From(principal);

        persisted.AuthenticationType.Should().Be("Cookies");
    }

    [TestMethod]
    public void From_PreservesAllClaims()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "123"),
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("user_id", "42"),
        }, "Cookies");

        var persisted = PersistedUser.From(new ClaimsPrincipal(identity));

        persisted.Claims.Should().HaveCount(3);
        persisted.Claims.Should().Contain(c => c.Type == "user_id" && c.Value == "42");
    }

    [TestMethod]
    public void From_PreservesMultipleRoleClaims()
    {
        // Regression: a single ClaimsPrincipal can hold many ClaimTypes.Role
        // values. Storing them in a dictionary would lose all but one.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.Role, "Streamer"),
            new Claim(ClaimTypes.Role, "Moderator"),
            new Claim(ClaimTypes.Role, "Developer"),
        }, "Cookies");

        var persisted = PersistedUser.From(new ClaimsPrincipal(identity));

        persisted.Claims.Where(c => c.Type == ClaimTypes.Role)
                        .Select(c => c.Value)
                        .Should().BeEquivalentTo(new[] { "Viewer", "Streamer", "Moderator", "Developer" });
    }

    [TestMethod]
    public void RoundTrip_ReproducesAuthenticatedPrincipalWithAllRoles()
    {
        var original = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "alice"),
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.Role, "Moderator"),
        }, "Cookies");

        var persisted = PersistedUser.From(new ClaimsPrincipal(original));
        var reconstructed = persisted.ToPrincipal();

        reconstructed.Identity!.IsAuthenticated.Should().BeTrue();
        reconstructed.Identity.AuthenticationType.Should().Be("Cookies");
        reconstructed.IsInRole("Viewer").Should().BeTrue();
        reconstructed.IsInRole("Moderator").Should().BeTrue();
        reconstructed.IsInRole("Developer").Should().BeFalse();
        reconstructed.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("42");
    }

    [TestMethod]
    public void From_AnonymousIdentity_DoesNotOverwriteDefaultAuthenticationType()
    {
        // Identity with no AuthenticationType is anonymous; we should leave the
        // default ("Cookie") in place rather than persisting an empty string,
        // which would later round-trip into an unauthenticated principal.
        var anon = new ClaimsPrincipal(new ClaimsIdentity());

        var persisted = PersistedUser.From(anon);

        persisted.AuthenticationType.Should().Be("Cookie");
        persisted.Claims.Should().BeEmpty();
    }
}
