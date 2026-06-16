using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Data;
using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Tests;

public sealed class ResourcesEndpointTests(CatalogApiFactory factory)
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
            .Where(tenant => tenant.Slug.StartsWith("test-resources-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.Resources
            .Where(resource => tenantIds.Contains(resource.TenantId))
            .ExecuteDeleteAsync();
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
        using var response = await _client.PostAsJsonAsync("/resources", ValidCreateRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithClientRole_ReturnsForbidden()
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            Guid.NewGuid(),
            "client",
            ValidCreateRequest(Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCompleteResourceLifecycle()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId, "Sucursal recursos");
        var secondBranchId = await CreateBranchAsync(tenant.TenantId, "Sucursal recursos secundaria");
        using var createRequest = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            tenant.TenantId,
            body: ValidCreateRequest(branchId));
        using var createResponse = await _client.SendAsync(createRequest);
        using var createPayload = await createResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createPayload);
        var data = createPayload.RootElement.GetProperty("data");
        var resourceId = data.GetProperty("resourceId").GetGuid();
        Assert.Equal(tenant.TenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal("silla", data.GetProperty("resourceType").GetString());
        Assert.Equal(1, data.GetProperty("capacity").GetInt32());
        Assert.Equal("active", data.GetProperty("status").GetString());

        using var listRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/resources?branchId={branchId}&status=active",
            tenant.TenantId);
        using var listResponse = await _client.SendAsync(listRequest);
        using var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(
            listPayload!.RootElement.GetProperty("data").EnumerateArray(),
            resource => resource.GetProperty("resourceId").GetGuid() == resourceId);

        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/resources/{resourceId}",
            tenant.TenantId,
            body: new
            {
                branchId = secondBranchId,
                name = "Sala Norte",
                resourceType = "Sala",
                description = "Sala privada",
                capacity = 4,
                status = "blocked"
            });
        using var updateResponse = await _client.SendAsync(updateRequest);
        using var updatePayload = await updateResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(secondBranchId, updatePayload!.RootElement.GetProperty("data").GetProperty("branchId").GetGuid());
        Assert.Equal("sala", updatePayload.RootElement.GetProperty("data").GetProperty("resourceType").GetString());
        Assert.Equal(4, updatePayload.RootElement.GetProperty("data").GetProperty("capacity").GetInt32());
        Assert.Equal("blocked", updatePayload.RootElement.GetProperty("data").GetProperty("status").GetString());

        using var activeListRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/resources?branchId={secondBranchId}&status=active",
            tenant.TenantId);
        using var activeListResponse = await _client.SendAsync(activeListRequest);
        using var activeListPayload = await activeListResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.DoesNotContain(
            activeListPayload!.RootElement.GetProperty("data").EnumerateArray(),
            resource => resource.GetProperty("resourceId").GetGuid() == resourceId);

        using var activateRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/resources/{resourceId}/status",
            tenant.TenantId,
            body: new { status = "active" });
        using var activateResponse = await _client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/resources/{resourceId}", tenant.TenantId);
        using var deleteResponse = await _client.SendAsync(deleteRequest);
        using var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal("inactive", deletePayload!.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Create_WithBranchFromAnotherTenant_ReturnsBranchNotFound()
    {
        var owner = await CreateTenantAsync();
        var otherTenant = await CreateTenantAsync();
        var foreignBranchId = await CreateBranchAsync(owner.TenantId, "Sucursal ajena");
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            otherTenant.TenantId,
            body: ValidCreateRequest(foreignBranchId));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("BRANCH_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetById_FromAnotherTenant_ReturnsNotFound()
    {
        var owner = await CreateTenantAsync();
        var otherTenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(owner.TenantId, "Sucursal privada");
        var resourceId = await CreateResourceAsync(owner.TenantId, branchId);
        using var request = AuthorizedRequest(HttpMethod.Get, $"/resources/{resourceId}", otherTenant.TenantId);

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("RESOURCE_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0, "active")]
    [InlineData(-1, "active")]
    [InlineData(1, "disabled")]
    public async Task Create_WithInvalidData_ReturnsValidationError(int capacity, string status)
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId, "Sucursal validaciones");
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            tenant.TenantId,
            body: ValidCreateRequest(branchId, capacity, status));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("silla")]
    [InlineData("sala")]
    [InlineData("profesional")]
    [InlineData("equipo")]
    public async Task Create_AcceptsExpectedResourceTypes(string resourceType)
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId, $"Sucursal {resourceType}");
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            tenant.TenantId,
            body: ValidCreateRequest(branchId, resourceType: resourceType));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(resourceType, payload!.RootElement.GetProperty("data").GetProperty("resourceType").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsCompleteResourceEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/resources", out var collection));
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/resources/{resourceId}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/resources/{resourceId}/status", out var status));
        Assert.True(status.TryGetProperty("patch", out _));
    }

    private async Task<CatalogTenant> CreateTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant de recursos",
            Slug = $"test-resources-{Guid.NewGuid():N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant;
    }

    private async Task<Guid> CreateBranchAsync(Guid tenantId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var branch = new Branch
        {
            BranchId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Address = "Av. Recursos 123",
            Phone = "+59170000300",
            EmailContact = null,
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Branches.Add(branch);
        await dbContext.SaveChangesAsync();
        return branch.BranchId;
    }

    private async Task<Guid> CreateResourceAsync(Guid tenantId, Guid branchId)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resources",
            tenantId,
            body: ValidCreateRequest(branchId));
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        response.EnsureSuccessStatusCode();
        return payload!.RootElement.GetProperty("data").GetProperty("resourceId").GetGuid();
    }

    private static object ValidCreateRequest(
        Guid branchId,
        int capacity = 1,
        string? status = null,
        string resourceType = "silla") => new
        {
            branchId,
            name = "Recurso de prueba",
            resourceType,
            description = "Recurso creado desde pruebas",
            capacity,
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
