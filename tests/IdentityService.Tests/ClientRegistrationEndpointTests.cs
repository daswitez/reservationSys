using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class ClientRegistrationEndpointTests(IdentityApiFactory factory)
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
            .Where(userRole => userRole.User.Email.StartsWith("test-hu003-"))
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.Email.StartsWith("test-hu003-"))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("hu003-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RegisterClient_WithValidData_CreatesGlobalUserWithClientRoleAndHashedPassword()
    {
        var email = NewEmail();

        using var response = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(email));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        var data = payload.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("tenantId", out _));
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("client", data.GetProperty("roles")[0].GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleAsync(candidate => candidate.Email == email);

        Assert.Null(user.TenantId);
        Assert.Equal("Daniel", user.FirstName);
        Assert.Equal("Mercado", user.LastName);
        Assert.Equal("+59170000000", user.Phone);
        Assert.NotEqual("Password123", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123", user.PasswordHash));
        Assert.Collection(
            user.UserRoles,
            userRole => Assert.Equal("client", userRole.Role.Code));
    }

    [Fact]
    public async Task RegisterClient_DoesNotRequireTenantOrTenantSlug()
    {
        using var response = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(NewEmail()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RegisterClient_WithInvalidData_ReturnsValidationError()
    {
        using var response = await _client.PostAsJsonAsync("/auth/register-client", new
        {
            firstName = "D",
            email = "email-invalido",
            password = "corta"
        });
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "VALIDATION_ERROR",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RegisterClient_WithDuplicateEmail_ReturnsConflictCaseInsensitive()
    {
        var email = NewEmail();

        using var firstResponse = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(email));
        using var secondResponse = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(email.ToUpperInvariant()));
        using var payload = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "USER_EMAIL_ALREADY_EXISTS",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RegisterClient_EmailConflictsWithExistingTenantUser()
    {
        var email = NewEmail();
        await CreateTenantUserAsync(email);

        using var response = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterClient_CanLoginWithoutTenantClaimAndUseAuthenticatedProfile()
    {
        var email = NewEmail();
        using var registerResponse = await _client.PostAsJsonAsync(
            "/auth/register-client",
            ValidRequest(email));
        registerResponse.EnsureSuccessStatusCode();

        using var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "Password123"
        });
        using var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(loginPayload);
        var loginData = loginPayload.RootElement.GetProperty("data");
        var user = loginData.GetProperty("user");
        Assert.Equal(JsonValueKind.Null, user.GetProperty("tenantId").ValueKind);
        Assert.Equal("client", user.GetProperty("roles")[0].GetString());

        var accessToken = loginData.GetProperty("accessToken").GetString();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == "tenant_id");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Add("X-Test-User-Id", user.GetProperty("userId").GetGuid().ToString());
        meRequest.Headers.Add("X-Test-Role", "client");
        using var meResponse = await _client.SendAsync(meRequest);
        using var mePayload = await meResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.NotNull(mePayload);
        Assert.Equal(
            JsonValueKind.Null,
            mePayload.RootElement.GetProperty("data").GetProperty("tenantId").ValueKind);
    }

    private async Task CreateTenantUserAsync(string email)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var role = await dbContext.Roles.SingleAsync(candidate => candidate.Code == "tenant_admin");
        var tenant = new Tenant
        {
            TenantId = tenantId,
            Name = "Empresa HU-003",
            Slug = $"hu003-{tenantId:N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var user = new User
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            FirstName = "Admin",
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
        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static object ValidRequest(string email) => new
    {
        firstName = "Daniel",
        lastName = "Mercado",
        email,
        phone = "+59170000000",
        password = "Password123"
    };

    private static string NewEmail() => $"test-hu003-{Guid.NewGuid():N}@example.com";
}
