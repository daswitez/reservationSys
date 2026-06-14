using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class AccessControlEndpointTests(IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private readonly IdentityApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.UserBranchAccess
            .Where(access => access.User.Email.StartsWith("test-hu005-"))
            .ExecuteDeleteAsync();
        await dbContext.UserRoles
            .Where(userRole => userRole.User.Email.StartsWith("test-hu005-"))
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.Email.StartsWith("test-hu005-"))
            .ExecuteDeleteAsync();
        await dbContext.CatalogBranches
            .Where(branch => branch.Name.StartsWith("HU-005"))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("hu005-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task AdministrativePanel_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync($"/auth/access/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdministrativePanel_WithClientRole_ReturnsForbidden()
    {
        var setup = await CreateSetupAsync("client");
        using var request = AuthorizedRequest(
            $"/auth/access/branches/{setup.BranchId}",
            setup.UserId,
            setup.TenantId,
            "client");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanAccessEveryBranchInOwnTenant()
    {
        var setup = await CreateSetupAsync("tenant_admin");
        var secondBranchId = await CreateBranchAsync(setup.TenantId);
        using var firstRequest = AuthorizedRequest(
            $"/auth/access/branches/{setup.BranchId}",
            setup.UserId,
            setup.TenantId,
            "tenant_admin");
        using var secondRequest = AuthorizedRequest(
            $"/auth/access/branches/{secondBranchId}",
            setup.UserId,
            setup.TenantId,
            "tenant_admin");

        using var firstResponse = await _client.SendAsync(firstRequest);
        using var secondResponse = await _client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotAccessBranchFromAnotherTenant()
    {
        var setup = await CreateSetupAsync("tenant_admin");
        var otherTenantId = await CreateTenantAsync();
        var otherBranchId = await CreateBranchAsync(otherTenantId);
        using var request = AuthorizedRequest(
            $"/auth/access/branches/{otherBranchId}",
            setup.UserId,
            setup.TenantId,
            "tenant_admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CanAccessAssignedBranch()
    {
        var setup = await CreateSetupAsync("branch_admin", assignBranch: true);
        using var request = AuthorizedRequest(
            $"/auth/access/branches/{setup.BranchId}",
            setup.UserId,
            setup.TenantId,
            "branch_admin");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(setup.TenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(setup.BranchId, data.GetProperty("branchId").GetGuid());
        Assert.True(data.GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task BranchAdmin_CannotAccessUnassignedBranch()
    {
        var setup = await CreateSetupAsync("branch_admin", assignBranch: true);
        var unassignedBranchId = await CreateBranchAsync(setup.TenantId);
        using var request = AuthorizedRequest(
            $"/auth/access/branches/{unassignedBranchId}",
            setup.UserId,
            setup.TenantId,
            "branch_admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PrivateMe_UsesTenantFromJwtAndRejectsTenantMismatch()
    {
        var setup = await CreateSetupAsync("tenant_admin");
        using var validRequest = AuthorizedRequest(
            "/auth/me",
            setup.UserId,
            setup.TenantId,
            "tenant_admin");
        using var invalidRequest = AuthorizedRequest(
            "/auth/me",
            setup.UserId,
            Guid.NewGuid(),
            "tenant_admin");

        using var validResponse = await _client.SendAsync(validRequest);
        using var validPayload = await validResponse.Content.ReadFromJsonAsync<JsonDocument>();
        using var invalidResponse = await _client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.NotNull(validPayload);
        Assert.Equal(
            setup.TenantId,
            validPayload.RootElement.GetProperty("data").GetProperty("tenantId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, invalidResponse.StatusCode);
    }

    private async Task<AccessSetup> CreateSetupAsync(string roleCode, bool assignBranch = false)
    {
        var tenantId = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenantId);
        var now = DateTimeOffset.UtcNow;

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var role = await dbContext.Roles.SingleAsync(candidate => candidate.Code == roleCode);
        var user = new User
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            Email = NewEmail(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            FirstName = "Usuario",
            LastName = "Acceso",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.UserRoles.Add(new UserRole
        {
            User = user,
            Role = role,
            CreatedAt = now
        });

        if (assignBranch)
        {
            user.BranchAccess.Add(new UserBranchAccess
            {
                User = user,
                TenantId = tenantId,
                BranchId = branchId,
                CreatedAt = now
            });
        }

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return new AccessSetup(user.UserId, tenantId, branchId);
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        dbContext.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            Name = "Empresa HU-005",
            Slug = $"hu005-{tenantId:N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> CreateBranchAsync(Guid tenantId)
    {
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        dbContext.CatalogBranches.Add(new CatalogBranch
        {
            BranchId = branchId,
            TenantId = tenantId,
            Name = $"HU-005 {branchId:N}",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return branchId;
    }

    private static HttpRequestMessage AuthorizedRequest(
        string path,
        Guid userId,
        Guid tenantId,
        string role)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static string NewEmail() => $"test-hu005-{Guid.NewGuid():N}@example.com";

    private sealed record AccessSetup(Guid UserId, Guid TenantId, Guid BranchId);
}
