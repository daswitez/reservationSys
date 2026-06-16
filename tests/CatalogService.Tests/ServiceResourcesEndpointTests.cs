using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Data;
using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Tests;

public sealed class ServiceResourcesEndpointTests(CatalogApiFactory factory)
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
            .Where(tenant => tenant.Slug.StartsWith("test-service-resources-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.ServiceResources
            .Where(link => tenantIds.Contains(link.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Resources
            .Where(resource => tenantIds.Contains(resource.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Services
            .Where(service => tenantIds.Contains(service.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Branches
            .Where(branch => tenantIds.Contains(branch.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenantIds.Contains(tenant.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Upsert_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync(
            $"/services/{Guid.NewGuid()}/resources/{Guid.NewGuid()}",
            ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_WithClientRole_ReturnsForbidden()
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{Guid.NewGuid()}/resources/{Guid.NewGuid()}",
            Guid.NewGuid(),
            "client",
            ValidRequest());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanAssociateManyResourcesAndManyServices()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var firstServiceId = await CreateServiceAsync(tenant.TenantId, "Servicio 1");
        var secondServiceId = await CreateServiceAsync(tenant.TenantId, "Servicio 2");
        var firstResourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Recurso 1");
        var secondResourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Recurso 2");

        using var firstRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{firstServiceId}/resources/{firstResourceId}",
            tenant.TenantId,
            body: ValidRequest(priority: 1));
        using var firstResponse = await _client.SendAsync(firstRequest);
        using var firstPayload = await firstResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(firstServiceId, firstPayload!.RootElement.GetProperty("data").GetProperty("serviceId").GetGuid());
        Assert.Equal(firstResourceId, firstPayload.RootElement.GetProperty("data").GetProperty("resourceId").GetGuid());

        using var secondRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{firstServiceId}/resources/{secondResourceId}",
            tenant.TenantId,
            body: ValidRequest(priority: 2));
        using var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using var thirdRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{secondServiceId}/resources/{firstResourceId}",
            tenant.TenantId,
            body: ValidRequest(priority: 1));
        using var thirdResponse = await _client.SendAsync(thirdRequest);
        Assert.Equal(HttpStatusCode.Created, thirdResponse.StatusCode);

        using var listRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/services/{firstServiceId}/resources?status=active",
            tenant.TenantId);
        using var listResponse = await _client.SendAsync(listRequest);
        using var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(2, listPayload!.RootElement.GetProperty("data").EnumerateArray().Count());

        using var compatibleRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/services/{firstServiceId}/compatible-resources?branchId={branchId}",
            tenant.TenantId);
        using var compatibleResponse = await _client.SendAsync(compatibleRequest);
        using var compatiblePayload = await compatibleResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, compatibleResponse.StatusCode);
        var resources = compatiblePayload!.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, resources.Length);
        Assert.Contains(resources, resource => resource.GetProperty("resourceId").GetGuid() == firstResourceId);
        Assert.Contains(resources, resource => resource.GetProperty("resourceId").GetGuid() == secondResourceId);
    }

    [Fact]
    public async Task CompatibleResources_ReturnsOnlyActiveLinksAndActiveResourcesForActiveService()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var serviceId = await CreateServiceAsync(tenant.TenantId, "Servicio activo");
        var activeResourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Activo");
        var blockedResourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Bloqueado", "blocked");
        var inactiveResourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Inactivo", "inactive");

        await AssociateAsync(tenant.TenantId, serviceId, activeResourceId);
        await AssociateAsync(tenant.TenantId, serviceId, blockedResourceId);
        await AssociateAsync(tenant.TenantId, serviceId, inactiveResourceId);

        using var deactivateLinkRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/services/{serviceId}/resources/{activeResourceId}/status",
            tenant.TenantId,
            body: new { status = "inactive" });
        using var deactivateLinkResponse = await _client.SendAsync(deactivateLinkRequest);
        deactivateLinkResponse.EnsureSuccessStatusCode();

        using var compatibleRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/services/{serviceId}/compatible-resources?branchId={branchId}",
            tenant.TenantId);
        using var compatibleResponse = await _client.SendAsync(compatibleRequest);
        using var compatiblePayload = await compatibleResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, compatibleResponse.StatusCode);
        Assert.Empty(compatiblePayload!.RootElement.GetProperty("data").EnumerateArray());

        using var reactivateLinkRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{serviceId}/resources/{activeResourceId}",
            tenant.TenantId,
            body: ValidRequest(priority: 3));
        using var reactivateLinkResponse = await _client.SendAsync(reactivateLinkRequest);
        using var reactivatePayload = await reactivateLinkResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, reactivateLinkResponse.StatusCode);
        Assert.Equal("active", reactivatePayload!.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(3, reactivatePayload.RootElement.GetProperty("data").GetProperty("priority").GetInt32());

        using var compatibleAfterReactivateResponse = await _client.SendAsync(AuthorizedRequest(
            HttpMethod.Get,
            $"/services/{serviceId}/compatible-resources?branchId={branchId}",
            tenant.TenantId));
        using var compatibleAfterReactivatePayload =
            await compatibleAfterReactivateResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var resources = compatibleAfterReactivatePayload!.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(resources);
        Assert.Equal(activeResourceId, resources[0].GetProperty("resourceId").GetGuid());
    }

    [Fact]
    public async Task Delete_DeactivatesAssociation()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var serviceId = await CreateServiceAsync(tenant.TenantId, "Servicio baja");
        var resourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Recurso baja");
        await AssociateAsync(tenant.TenantId, serviceId, resourceId);

        using var deleteRequest = AuthorizedRequest(
            HttpMethod.Delete,
            $"/services/{serviceId}/resources/{resourceId}",
            tenant.TenantId);
        using var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var compatibleRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/services/{serviceId}/compatible-resources",
            tenant.TenantId);
        using var compatibleResponse = await _client.SendAsync(compatibleRequest);
        using var compatiblePayload = await compatibleResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Empty(compatiblePayload!.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Upsert_WithResourceFromAnotherTenant_ReturnsResourceNotFound()
    {
        var owner = await CreateTenantAsync();
        var otherTenant = await CreateTenantAsync();
        var ownerBranchId = await CreateBranchAsync(owner.TenantId);
        var otherServiceId = await CreateServiceAsync(otherTenant.TenantId, "Servicio ajeno");
        var foreignResourceId = await CreateResourceAsync(owner.TenantId, ownerBranchId, "Recurso ajeno");

        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{otherServiceId}/resources/{foreignResourceId}",
            otherTenant.TenantId,
            body: ValidRequest());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("RESOURCE_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0, "active")]
    [InlineData(1, "blocked")]
    public async Task Upsert_WithInvalidData_ReturnsValidationError(int priority, string status)
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var serviceId = await CreateServiceAsync(tenant.TenantId, "Servicio validacion");
        var resourceId = await CreateResourceAsync(tenant.TenantId, branchId, "Recurso validacion");
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{serviceId}/resources/{resourceId}",
            tenant.TenantId,
            body: ValidRequest(priority: priority, status: status));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsCompleteServiceResourceEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/services/{serviceId}/resources", out var collection));
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/services/{serviceId}/compatible-resources", out var compatible));
        Assert.True(compatible.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/services/{serviceId}/resources/{resourceId}", out var item));
        Assert.True(item.TryGetProperty("post", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/services/{serviceId}/resources/{resourceId}/status", out var status));
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
            Name = "Tenant de asociaciones",
            Slug = $"test-service-resources-{Guid.NewGuid():N}",
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

    private async Task<Guid> CreateBranchAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var branch = new Branch
        {
            BranchId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Sucursal asociaciones",
            Address = "Av. Asociaciones 123",
            Phone = "+59170000400",
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

    private async Task<Guid> CreateServiceAsync(Guid tenantId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var service = new Service
        {
            ServiceId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = "Servicio de prueba",
            DurationMinutes = 30,
            ReferencePrice = 50,
            Modality = "presencial",
            RequiresConfirmation = false,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync();
        return service.ServiceId;
    }

    private async Task<Guid> CreateResourceAsync(
        Guid tenantId,
        Guid branchId,
        string name,
        string status = "active")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var resource = new Resource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Name = name,
            ResourceType = "silla",
            Description = "Recurso de prueba",
            Capacity = 1,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();
        return resource.ResourceId;
    }

    private async Task AssociateAsync(Guid tenantId, Guid serviceId, Guid resourceId)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"/services/{serviceId}/resources/{resourceId}",
            tenantId,
            body: ValidRequest());
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static object ValidRequest(
        bool required = true,
        int priority = 1,
        string? status = null) => new
        {
            required,
            priority,
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
