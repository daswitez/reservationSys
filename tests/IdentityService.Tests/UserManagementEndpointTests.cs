using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class UserManagementEndpointTests(IdentityApiFactory factory)
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
            .Where(access => access.User.Email.StartsWith("test-user-crud-"))
            .ExecuteDeleteAsync();
        await dbContext.UserRoles
            .Where(userRole => userRole.User.Email.StartsWith("test-user-crud-"))
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.Email.StartsWith("test-user-crud-"))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("user-crud-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TenantAdmin_ListUsers_OnlyReturnsOwnTenant()
    {
        var ownTenantId = await CreateTenantAsync();
        var otherTenantId = await CreateTenantAsync();
        var ownUser = await CreateUserAsync(ownTenantId, "branch_admin");
        var otherUser = await CreateUserAsync(otherTenantId, "branch_admin");
        using var request = AuthorizedRequest(HttpMethod.Get, "/users", Guid.NewGuid(), ownTenantId, "tenant_admin");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        var ids = payload.RootElement.GetProperty("data").GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("userId").GetGuid())
            .ToArray();
        Assert.Contains(ownUser.UserId, ids);
        Assert.DoesNotContain(otherUser.UserId, ids);
    }

    [Fact]
    public async Task TenantAdmin_CannotReadOrUpdateUserFromAnotherTenant()
    {
        var ownTenantId = await CreateTenantAsync();
        var otherTenantId = await CreateTenantAsync();
        var otherUser = await CreateUserAsync(otherTenantId, "branch_admin");
        using var getRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/users/{otherUser.UserId}",
            Guid.NewGuid(),
            ownTenantId,
            "tenant_admin");
        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/users/{otherUser.UserId}",
            Guid.NewGuid(),
            ownTenantId,
            "tenant_admin",
            ValidProfile(otherUser.Email));

        using var getResponse = await _client.SendAsync(getRequest);
        using var updateResponse = await _client.SendAsync(updateRequest);

        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanEditAndBlockUserInOwnTenant()
    {
        var tenantId = await CreateTenantAsync();
        var user = await CreateUserAsync(tenantId, "branch_admin");
        var newEmail = NewEmail();
        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/users/{user.UserId}",
            Guid.NewGuid(),
            tenantId,
            "tenant_admin",
            ValidProfile(newEmail));

        using var updateResponse = await _client.SendAsync(updateRequest);
        using var statusRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/users/{user.UserId}/status",
            Guid.NewGuid(),
            tenantId,
            "tenant_admin",
            new { status = "blocked" });
        using var statusResponse = await _client.SendAsync(statusRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await dbContext.Users.SingleAsync(candidate => candidate.UserId == user.UserId);
        Assert.Equal(newEmail, persisted.Email);
        Assert.Equal("Nombre Editado", persisted.FirstName);
        Assert.Equal("blocked", persisted.Status);
        Assert.True(persisted.AuthVersion > 1);
    }

    [Fact]
    public async Task DeleteUser_PerformsLogicalDeactivation()
    {
        var tenantId = await CreateTenantAsync();
        var user = await CreateUserAsync(tenantId, "branch_admin");
        using var request = AuthorizedRequest(
            HttpMethod.Delete,
            $"/users/{user.UserId}",
            Guid.NewGuid(),
            tenantId,
            "tenant_admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await dbContext.Users.SingleAsync(candidate => candidate.UserId == user.UserId);
        Assert.Equal("inactive", persisted.Status);
    }

    [Fact]
    public async Task Administrator_CannotDeactivateOwnAccount()
    {
        var tenantId = await CreateTenantAsync();
        var admin = await CreateUserAsync(tenantId, "tenant_admin");
        using var request = AuthorizedRequest(
            HttpMethod.Patch,
            $"/users/{admin.UserId}/status",
            admin.UserId,
            tenantId,
            "tenant_admin",
            new { status = "inactive" });

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Client_CanEditOwnProfileButCannotUseAdministrativeList()
    {
        var client = await CreateUserAsync(null, "client");
        var newEmail = NewEmail();
        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            "/users/me",
            client.UserId,
            null,
            "client",
            ValidProfile(newEmail));
        using var listRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/users",
            client.UserId,
            null,
            "client");

        using var updateResponse = await _client.SendAsync(updateRequest);
        using var listResponse = await _client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
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
            Name = "Empresa User CRUD",
            Slug = $"user-crud-{tenantId:N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return tenantId;
    }

    private async Task<User> CreateUserAsync(Guid? tenantId, string roleCode)
    {
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
            LastName = "CRUD",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.UserRoles.Add(new UserRole { User = user, Role = role, CreatedAt = now });
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        Guid userId,
        Guid? tenantId,
        string role,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Role", role);

        if (tenantId.HasValue)
        {
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static object ValidProfile(string email) => new
    {
        firstName = "Nombre Editado",
        lastName = "Apellido",
        email,
        phone = "+59171111111"
    };

    private static string NewEmail() => $"test-user-crud-{Guid.NewGuid():N}@example.com";
}
