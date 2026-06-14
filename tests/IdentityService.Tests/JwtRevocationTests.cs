using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class JwtRevocationTests(JwtIdentityApiFactory factory)
    : IClassFixture<JwtIdentityApiFactory>, IAsyncLifetime
{
    private readonly JwtIdentityApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();
    private Guid _userId;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_userId == Guid.Empty)
        {
            return;
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.UserRoles
            .Where(userRole => userRole.UserId == _userId)
            .ExecuteDeleteAsync();
        await dbContext.Users
            .Where(user => user.UserId == _userId)
            .ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("blocked")]
    public async Task IssuedJwt_IsRejectedAfterUserStopsBeingActive(string status)
    {
        var email = $"test-jwt-revocation-{Guid.NewGuid():N}@example.com";
        await CreateClientAsync(email);

        using var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = "Password123"
        });
        using var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonDocument>();
        loginResponse.EnsureSuccessStatusCode();
        Assert.NotNull(loginPayload);
        var accessToken = loginPayload.RootElement
            .GetProperty("data")
            .GetProperty("accessToken")
            .GetString();

        using var validRequest = AuthorizedMeRequest(accessToken);
        using var validResponse = await _client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        await UpdateStatusAsync(status);

        using var revokedRequest = AuthorizedMeRequest(accessToken);
        using var revokedResponse = await _client.SendAsync(revokedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    [Fact]
    public async Task IssuedJwt_IsRejectedAfterPasswordChange()
    {
        var email = $"test-jwt-revocation-{Guid.NewGuid():N}@example.com";
        await CreateClientAsync(email);
        var accessToken = await LoginAsync(email, "Password123");
        using var passwordRequest = new HttpRequestMessage(HttpMethod.Patch, "/users/me/password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "Password123",
                newPassword = "Password456"
            })
        };
        passwordRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var passwordResponse = await _client.SendAsync(passwordRequest);
        Assert.Equal(HttpStatusCode.OK, passwordResponse.StatusCode);

        using var revokedRequest = AuthorizedMeRequest(accessToken);
        using var revokedResponse = await _client.SendAsync(revokedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);

        var replacementToken = await LoginAsync(email, "Password456");
        Assert.False(string.IsNullOrWhiteSpace(replacementToken));
    }

    private async Task CreateClientAsync(string email)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var role = await dbContext.Roles.SingleAsync(candidate => candidate.Code == "client");
        var user = new User
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            FirstName = "JWT",
            LastName = "Revocation",
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
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        _userId = user.UserId;
    }

    private async Task UpdateStatusAsync(string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Users
            .Where(user => user.UserId == _userId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(user => user.Status, status)
                .SetProperty(user => user.UpdatedAt, DateTimeOffset.UtcNow));
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        using var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        response.EnsureSuccessStatusCode();
        Assert.NotNull(payload);
        return payload.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage AuthorizedMeRequest(string? accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
