using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class TenantAdminEndpointTests(IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private readonly IdentityApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.UserRoles
            .Where(userRole => userRole.User.Email.StartsWith("test-hu002-"))
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.Email.StartsWith("test-hu002-"))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("hu002-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CreateTenantAdmin_WithoutToken_ReturnsUnauthorized()
    {
        var tenantId = await CreateTenantAsync();

        using var response = await _client.PostAsJsonAsync(
            "/users/admin",
            ValidRequest(tenantId, NewEmail()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantAdmin_WithTenantAdminToken_ReturnsForbidden()
    {
        var tenantId = await CreateTenantAsync();
        using var request = AuthorizedCreateRequest(
            tenantId,
            NewEmail(),
            "tenant_admin");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantAdmin_WithUnknownTenant_ReturnsNotFound()
    {
        using var request = AuthorizedCreateRequest(
            Guid.NewGuid(),
            NewEmail(),
            "super_admin");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "TENANT_NOT_FOUND",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateTenantAdmin_AssociatesTenantRoleAndHashedPassword()
    {
        var tenantId = await CreateTenantAsync();
        var email = NewEmail();
        using var request = AuthorizedCreateRequest(tenantId, email, "super_admin");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(tenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal("tenant_admin", data.GetProperty("roles")[0].GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleAsync(candidate => candidate.Email == email);

        Assert.Equal(tenantId, user.TenantId);
        Assert.NotEqual("Password123", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123", user.PasswordHash));
        Assert.Collection(
            user.UserRoles,
            userRole => Assert.Equal("tenant_admin", userRole.Role.Code));
    }

    [Fact]
    public async Task CreateTenantAdmin_WithDuplicateEmailInTenant_ReturnsConflict()
    {
        var tenantId = await CreateTenantAsync();
        var email = NewEmail();
        using var firstRequest = AuthorizedCreateRequest(tenantId, email, "super_admin");
        using var secondRequest = AuthorizedCreateRequest(
            tenantId,
            email.ToUpperInvariant(),
            "super_admin");

        using var firstResponse = await _client.SendAsync(firstRequest);
        using var secondResponse = await _client.SendAsync(secondRequest);
        using var payload = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "USER_EMAIL_ALREADY_EXISTS",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_WithCreatedTenantAdmin_ReturnsJwtWithRequiredClaims()
    {
        var tenantId = await CreateTenantAsync();
        var email = NewEmail();
        using var createRequest = AuthorizedCreateRequest(tenantId, email, "super_admin");
        using var createResponse = await _client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        using var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "Password123"
        });
        using var payload = await loginResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(payload);
        var data = payload.RootElement.GetProperty("data");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            data.GetProperty("accessToken").GetString());
        var user = data.GetProperty("user");

        Assert.Equal(tenantId, user.GetProperty("tenantId").GetGuid());
        Assert.Equal("tenant_admin", user.GetProperty("roles")[0].GetString());
        Assert.Equal(user.GetProperty("userId").GetGuid().ToString(), token.Payload["user_id"]);
        Assert.Equal(tenantId.ToString(), token.Payload["tenant_id"]);
        Assert.Contains(token.Claims, claim => claim.Type == "roles" && claim.Value == "tenant_admin");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var tenantId = await CreateTenantAsync();
        var email = NewEmail();
        using var createRequest = AuthorizedCreateRequest(tenantId, email, "super_admin");
        using var createResponse = await _client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        using var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "PasswordIncorrecto"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
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
            Name = "Empresa HU-002",
            Slug = $"hu002-{tenantId:N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return tenantId;
    }

    private static object ValidRequest(Guid tenantId, string email) => new
    {
        tenantId,
        firstName = "Admin",
        lastName = "Empresa",
        email,
        phone = "+59170000000",
        password = "Password123"
    };

    private static HttpRequestMessage AuthorizedCreateRequest(
        Guid tenantId,
        string email,
        string role)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/users/admin")
        {
            Content = JsonContent.Create(ValidRequest(tenantId, email))
        };
        request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static string NewEmail() => $"test-hu002-{Guid.NewGuid():N}@example.com";
}
