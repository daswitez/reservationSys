using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReportingService.Tests;

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var request = Context.Request;

        if (!request.Headers.TryGetValue("X-Test-Role", out var roleValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var role = roleValues.ToString();
        var userId = request.Headers.TryGetValue("X-Test-User-Id", out var uid)
            ? uid.ToString()
            : Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("user_id", userId),
            new("roles", role)
        };

        if (request.Headers.TryGetValue("X-Test-Tenant-Id", out var tenantId))
            claims.Add(new Claim("tenant_id", tenantId.ToString()));

        if (request.Headers.TryGetValue("X-Test-Branch-Id", out var branchId))
            claims.Add(new Claim("branch_id", branchId.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
