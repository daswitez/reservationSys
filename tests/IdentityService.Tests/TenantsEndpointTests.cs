using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

public sealed class TenantsEndpointTests(IdentityApiFactory factory)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private readonly IdentityApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("test-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CreateTenant_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/tenants", ValidRequest("test-sin-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListPublic_WithoutToken_ReturnsOnlyActiveTenants()
    {
        var activeSlug = $"test-public-active-{Guid.NewGuid():N}";
        var inactiveSlug = $"test-public-inactive-{Guid.NewGuid():N}";
        using var activeRequest = AuthorizedRequest(activeSlug, "super_admin");
        using var inactiveRequest = AuthorizedRequest(
            inactiveSlug,
            "super_admin",
            status: "inactive");
        using var activeResponse = await _client.SendAsync(activeRequest);
        using var inactiveResponse = await _client.SendAsync(inactiveRequest);
        activeResponse.EnsureSuccessStatusCode();
        inactiveResponse.EnsureSuccessStatusCode();

        using var response = await _client.GetAsync("/tenants/public");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        var tenants = payload.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Contains(tenants, tenant => tenant.GetProperty("slug").GetString() == activeSlug);
        Assert.DoesNotContain(tenants, tenant => tenant.GetProperty("slug").GetString() == inactiveSlug);
    }

    [Fact]
    public async Task CreateTenant_WithTenantAdminToken_ReturnsForbidden()
    {
        using var request = AuthorizedRequest("test-tenant-admin", "tenant_admin", Guid.NewGuid());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_WithSuperAdmin_CreatesActiveTenantByDefault()
    {
        var slug = $"test-empresa-{Guid.NewGuid():N}";
        using var request = AuthorizedRequest(slug, "super_admin");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(slug, payload.RootElement.GetProperty("data").GetProperty("slug").GetString());
        Assert.Equal("active", payload.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateTenant_WithExplicitInactiveStatus_PreservesStatus()
    {
        var slug = $"test-inactiva-{Guid.NewGuid():N}";
        using var request = AuthorizedRequest(slug, "super_admin", status: "inactive");

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("inactive", payload.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateTenant_WithDuplicateSlug_ReturnsConflict()
    {
        var slug = $"test-duplicada-{Guid.NewGuid():N}";
        using var firstRequest = AuthorizedRequest(slug, "super_admin");
        using var secondRequest = AuthorizedRequest(slug, "super_admin");

        using var firstResponse = await _client.SendAsync(firstRequest);
        using var secondResponse = await _client.SendAsync(secondRequest);
        using var payload = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "TENANT_SLUG_ALREADY_EXISTS",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Slug Invalido", "America/La_Paz")]
    [InlineData("test-slug-valido", "Zona/Que_No_Existe")]
    public async Task CreateTenant_WithInvalidData_ReturnsValidationError(
        string slug,
        string timezone)
    {
        using var request = AuthorizedRequest(slug, "super_admin", timezone: timezone);

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            "VALIDATION_ERROR",
            payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static object ValidRequest(
        string slug,
        string timezone = "America/La_Paz",
        string? status = null) => new
        {
            name = "Empresa de prueba",
            slug,
            mainCategory = "Servicios",
            timezone,
            status
        };

    private static HttpRequestMessage AuthorizedRequest(
        string slug,
        string role,
        Guid? tenantId = null,
        string timezone = "America/La_Paz",
        string? status = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tenants")
        {
            Content = JsonContent.Create(ValidRequest(slug, timezone, status))
        };
        request.Headers.Add("X-Test-Role", role);

        if (tenantId.HasValue)
        {
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        }

        return request;
    }
}
