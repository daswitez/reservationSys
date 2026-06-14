using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Tests;

public sealed class LoginEndpointTests(IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string JwtSecret = "dev_secret_change_me_before_production";
    private const string JwtIssuer = "reservas-mvp";
    private const string JwtAudience = "reservas-mvp-web";
    private readonly IdentityApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.UserRoles
            .Where(userRole => userRole.User.Email.StartsWith("test-hu004-"))
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.Email.StartsWith("test-hu004-"))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("hu004-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsValidJwtAndUpdatesLastLogin()
    {
        var user = await CreateUserAsync("active", "tenant_admin");
        var beforeLogin = DateTimeOffset.UtcNow;

        using var response = await LoginAsync(user.Email, "Password123");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(response.Headers.Pragma, header => header.Name == "no-cache");
        Assert.NotNull(payload);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(3600, data.GetProperty("expiresIn").GetInt32());

        var principal = ValidateToken(data.GetProperty("accessToken").GetString());
        Assert.Equal(user.UserId.ToString(), principal.FindFirstValue("user_id"));
        Assert.Equal(user.TenantId?.ToString(), principal.FindFirstValue("tenant_id"));
        Assert.Equal("tenant_admin", principal.FindFirstValue("roles"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persistedUser = await dbContext.Users.SingleAsync(candidate => candidate.UserId == user.UserId);
        Assert.NotNull(persistedUser.LastLoginAt);
        Assert.True(persistedUser.LastLoginAt >= beforeLogin);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("blocked")]
    public async Task Login_WithNonActiveUser_ReturnsUnauthorizedAndDoesNotUpdateLastLogin(string status)
    {
        var user = await CreateUserAsync(status, "client");

        using var response = await LoginAsync(user.Email, "Password123");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "INVALID_CREDENTIALS",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persistedUser = await dbContext.Users.SingleAsync(candidate => candidate.UserId == user.UserId);
        Assert.Null(persistedUser.LastLoginAt);
    }

    [Theory]
    [InlineData("PasswordIncorrecto")]
    [InlineData("")]
    public async Task Login_WithInvalidPassword_DoesNotReturnToken(string password)
    {
        var user = await CreateUserAsync("active", "client");

        using var response = await LoginAsync(user.Email, password);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
        Assert.NotNull(payload);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.False(payload.RootElement.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        using var response = await LoginAsync(NewEmail(), "Password123");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "INVALID_CREDENTIALS",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<User> CreateUserAsync(string status, string roleCode)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant
        {
            TenantId = tenantId,
            Name = "Empresa HU-004",
            Slug = $"hu004-{tenantId:N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

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
            LastName = "Login",
            Status = status,
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
        return user;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/auth/login", new { email, password });

    private static ClaimsPrincipal ValidateToken(string? accessToken)
    {
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = JwtIssuer,
            ValidAudience = JwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            RoleClaimType = "roles",
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        var tokenHandler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = tokenHandler.ValidateToken(
            accessToken,
            validationParameters,
            out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        return principal;
    }

    private static string NewEmail() => $"test-hu004-{Guid.NewGuid():N}@example.com";
}
