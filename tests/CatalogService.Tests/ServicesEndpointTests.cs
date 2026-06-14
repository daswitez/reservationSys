using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Data;
using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Tests;

public sealed class ServicesEndpointTests(CatalogApiFactory factory)
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
            .Where(tenant => tenant.Slug.StartsWith("test-services-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.Services
            .Where(service => tenantIds.Contains(service.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(tenant => tenantIds.Contains(tenant.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/services", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithClientRole_ReturnsForbidden()
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/services",
            Guid.NewGuid(),
            "client",
            ValidCreateRequest());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCompleteServiceLifecycle()
    {
        var tenant = await CreateTenantAsync();
        using var createRequest = AuthorizedRequest(
            HttpMethod.Post,
            "/services",
            tenant.TenantId,
            body: ValidCreateRequest());
        using var createResponse = await _client.SendAsync(createRequest);
        using var createPayload = await createResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createPayload);
        var data = createPayload.RootElement.GetProperty("data");
        var serviceId = data.GetProperty("serviceId").GetGuid();
        Assert.Equal(tenant.TenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(30, data.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(50m, data.GetProperty("referencePrice").GetDecimal());
        Assert.Equal("presencial", data.GetProperty("modality").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());

        using var listRequest = AuthorizedRequest(HttpMethod.Get, "/services?status=active", tenant.TenantId);
        using var listResponse = await _client.SendAsync(listRequest);
        using var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(
            listPayload!.RootElement.GetProperty("data").EnumerateArray(),
            service => service.GetProperty("serviceId").GetGuid() == serviceId);

        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/services/{serviceId}",
            tenant.TenantId,
            body: new
            {
                name = "Corte premium",
                description = "Servicio actualizado",
                durationMinutes = 45,
                referencePrice = 75.50m,
                modality = "Virtual",
                status = "inactive"
            });
        using var updateResponse = await _client.SendAsync(updateRequest);
        using var updatePayload = await updateResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Corte premium", updatePayload!.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("virtual", updatePayload.RootElement.GetProperty("data").GetProperty("modality").GetString());
        Assert.Equal("inactive", updatePayload.RootElement.GetProperty("data").GetProperty("status").GetString());

        using var activateRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/services/{serviceId}/status",
            tenant.TenantId,
            body: new { status = "active" });
        using var activateResponse = await _client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(HttpMethod.Delete, $"/services/{serviceId}", tenant.TenantId);
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
        var serviceId = await CreateServiceAsync(owner.TenantId, "Servicio privado");
        using var request = AuthorizedRequest(HttpMethod.Get, $"/services/{serviceId}", otherTenant.TenantId);

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SERVICE_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublicList_ReturnsOnlyActiveServicesFromActiveTenant()
    {
        var tenant = await CreateTenantAsync();
        var activeServiceId = await CreateServiceAsync(tenant.TenantId, "Servicio activo");
        _ = await CreateServiceAsync(tenant.TenantId, "Servicio inactivo", "inactive");

        using var response = await _client.GetAsync($"/public/tenants/{tenant.Slug}/services");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var services = payload!.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(services);
        Assert.Equal(activeServiceId, services[0].GetProperty("serviceId").GetGuid());

        using var deactivateRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/services/{activeServiceId}/status",
            tenant.TenantId,
            body: new { status = "inactive" });
        using var deactivateResponse = await _client.SendAsync(deactivateRequest);
        deactivateResponse.EnsureSuccessStatusCode();

        using var afterDeactivationResponse = await _client.GetAsync($"/public/tenants/{tenant.Slug}/services");
        using var afterDeactivationPayload = await afterDeactivationResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Empty(afterDeactivationPayload!.RootElement.GetProperty("data").EnumerateArray());
    }

    [Theory]
    [InlineData(0, 50, "active")]
    [InlineData(-1, 50, "active")]
    [InlineData(30, -1, "active")]
    [InlineData(30, 50, "blocked")]
    public async Task Create_WithInvalidData_ReturnsValidationError(
        int durationMinutes,
        int referencePrice,
        string status)
    {
        var tenant = await CreateTenantAsync();
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/services",
            tenant.TenantId,
            body: ValidCreateRequest(durationMinutes, referencePrice, status));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsCompleteServiceEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/services", out var collection));
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/services/{serviceId}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/services/{serviceId}/status", out var status));
        Assert.True(status.TryGetProperty("patch", out _));
        Assert.True(paths.TryGetProperty("/public/tenants/{tenantSlug}/services", out _));
    }

    private async Task<CatalogTenant> CreateTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant de servicios",
            Slug = $"test-services-{Guid.NewGuid():N}",
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

    private async Task<Guid> CreateServiceAsync(Guid tenantId, string name, string? status = null)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/services",
            tenantId,
            body: ValidCreateRequest(status: status, name: name));
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        response.EnsureSuccessStatusCode();
        return payload!.RootElement.GetProperty("data").GetProperty("serviceId").GetGuid();
    }

    private static object ValidCreateRequest(
        int durationMinutes = 30,
        decimal referencePrice = 50,
        string? status = null,
        string name = "Corte de cabello") => new
        {
            name,
            description = "Corte clasico o moderno",
            durationMinutes,
            referencePrice,
            modality = "presencial",
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
