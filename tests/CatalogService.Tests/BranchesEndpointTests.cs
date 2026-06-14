using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Data;
using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Tests;

public sealed class BranchesEndpointTests(CatalogApiFactory factory)
    : IClassFixture<CatalogApiFactory>, IAsyncLifetime
{
    private readonly CatalogApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenantIds = await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("test-catalog-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.Branches
            .Where(branch => tenantIds.Contains(branch.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenantIds.Contains(tenant.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/branches", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithClientRole_ReturnsForbidden()
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/branches",
            Guid.NewGuid(),
            "client",
            ValidCreateRequest());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCompleteBranchLifecycle()
    {
        var tenant = await CreateTenantAsync();
        using var createRequest = AuthorizedRequest(
            HttpMethod.Post,
            "/branches",
            tenant.TenantId,
            body: ValidCreateRequest());
        using var createResponse = await _client.SendAsync(createRequest);
        using var createPayload = await createResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createPayload);
        var branchId = createPayload.RootElement.GetProperty("data").GetProperty("branchId").GetGuid();
        Assert.Equal(tenant.TenantId, createPayload.RootElement.GetProperty("data").GetProperty("tenantId").GetGuid());
        Assert.Equal("active", createPayload.RootElement.GetProperty("data").GetProperty("status").GetString());

        using var listRequest = AuthorizedRequest(HttpMethod.Get, "/branches?status=active", tenant.TenantId);
        using var listResponse = await _client.SendAsync(listRequest);
        using var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(
            listPayload!.RootElement.GetProperty("data").EnumerateArray(),
            branch => branch.GetProperty("branchId").GetGuid() == branchId);

        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/branches/{branchId}",
            tenant.TenantId,
            body: new
            {
                name = "Sucursal Norte",
                address = "Av. Norte 456",
                phone = "+59171111111",
                emailContact = "norte@test.com",
                timezone = "America/La_Paz",
                status = "inactive"
            });
        using var updateResponse = await _client.SendAsync(updateRequest);
        using var updatePayload = await updateResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Sucursal Norte", updatePayload!.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("inactive", updatePayload.RootElement.GetProperty("data").GetProperty("status").GetString());

        using var activateRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/branches/{branchId}/status",
            tenant.TenantId,
            body: new { status = "active" });
        using var activateResponse = await _client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/branches/{branchId}", tenant.TenantId);
        using var deleteResponse = await _client.SendAsync(deleteRequest);
        using var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal("inactive", deletePayload!.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetById_FromAnotherTenant_ReturnsNotFound()
    {
        var owner = await CreateTenantAsync();
        var otherTenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(owner.TenantId, "Sucursal privada");
        using var request = AuthorizedRequest(HttpMethod.Get, $"/branches/{branchId}", otherTenant.TenantId);

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("BRANCH_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublicList_ReturnsOnlyActiveBranchesFromActiveTenant()
    {
        var tenant = await CreateTenantAsync();
        var activeBranchId = await CreateBranchAsync(tenant.TenantId, "Sucursal activa");
        _ = await CreateBranchAsync(tenant.TenantId, "Sucursal inactiva", "inactive");

        using var response = await _client.GetAsync($"/public/tenants/{tenant.Slug}/branches");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = payload!.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(branches);
        Assert.Equal(activeBranchId, branches[0].GetProperty("branchId").GetGuid());

        await SetTenantStatusAsync(tenant.TenantId, "inactive");
        using var inactiveTenantResponse = await _client.GetAsync($"/public/tenants/{tenant.Slug}/branches");
        Assert.Equal(HttpStatusCode.NotFound, inactiveTenantResponse.StatusCode);
    }

    [Theory]
    [InlineData("Zona/Que_No_Existe", "active")]
    [InlineData("America/La_Paz", "blocked")]
    public async Task Create_WithInvalidData_ReturnsValidationError(string timezone, string status)
    {
        var tenant = await CreateTenantAsync();
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/branches",
            tenant.TenantId,
            body: ValidCreateRequest(timezone, status));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsCompleteBranchEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/branches", out var collection));
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/branches/{branchId}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/branches/{branchId}/status", out var status));
        Assert.True(status.TryGetProperty("patch", out _));
        Assert.True(paths.TryGetProperty("/public/tenants/{tenantSlug}/branches", out _));
    }

    private async Task<CatalogTenant> CreateTenantAsync(string status = "active")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant de catalogo",
            Slug = $"test-catalog-{Guid.NewGuid():N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant;
    }

    private async Task<Guid> CreateBranchAsync(Guid tenantId, string name, string status = "active")
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/branches",
            tenantId,
            body: ValidCreateRequest(status: status, name: name));
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        response.EnsureSuccessStatusCode();
        return payload!.RootElement.GetProperty("data").GetProperty("branchId").GetGuid();
    }

    private async Task SetTenantStatusAsync(Guid tenantId, string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await dbContext.Tenants.SingleAsync(candidate => candidate.TenantId == tenantId);
        tenant.Status = status;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private static object ValidCreateRequest(
        string timezone = "America/La_Paz",
        string? status = null,
        string name = "Sucursal Centro") => new
        {
            name,
            address = "Av. Principal 123",
            phone = "+59170000000",
            emailContact = "centro@test.com",
            timezone,
            status
        };

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        Guid tenantId,
        string role = "tenant_admin",
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-Tenant-Id", tenantId.ToString());

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
