using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Data;
using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Tests;

public sealed class ResourceSchedulesEndpointTests(CatalogApiFactory factory)
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
            .Where(tenant => tenant.Slug.StartsWith("test-resource-schedules-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.ResourceSchedules
            .Where(schedule => tenantIds.Contains(schedule.TenantId))
            .ExecuteDeleteAsync();
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
        using var response = await _client.PostAsJsonAsync(
            "/resource-schedules",
            ValidCreateRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithClientRole_ReturnsForbidden()
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resource-schedules",
            Guid.NewGuid(),
            "client",
            ValidCreateRequest(Guid.NewGuid(), Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCompleteResourceScheduleLifecycle()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var resourceId = await CreateResourceAsync(tenant.TenantId, branchId);
        using var createRequest = AuthorizedRequest(
            HttpMethod.Post,
            "/resource-schedules",
            tenant.TenantId,
            body: ValidCreateRequest(branchId, resourceId));
        using var createResponse = await _client.SendAsync(createRequest);
        using var createPayload = await createResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createPayload);
        var data = createPayload.RootElement.GetProperty("data");
        var scheduleId = data.GetProperty("scheduleId").GetGuid();
        Assert.Equal(tenant.TenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal(resourceId, data.GetProperty("resourceId").GetGuid());
        Assert.Equal(1, data.GetProperty("dayOfWeek").GetInt32());
        Assert.Equal("09:00", data.GetProperty("startTime").GetString());
        Assert.Equal("18:00", data.GetProperty("endTime").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());

        using var listRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/resource-schedules?branchId={branchId}&resourceId={resourceId}&dayOfWeek=1&status=active",
            tenant.TenantId);
        using var listResponse = await _client.SendAsync(listRequest);
        using var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(
            listPayload!.RootElement.GetProperty("data").EnumerateArray(),
            schedule => schedule.GetProperty("scheduleId").GetGuid() == scheduleId);

        using var updateRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/resource-schedules/{scheduleId}",
            tenant.TenantId,
            body: new
            {
                branchId,
                resourceId,
                dayOfWeek = 2,
                startTime = "10:00",
                endTime = "16:30",
                validFrom = "2026-06-16",
                validTo = "2026-12-31",
                status = "inactive"
            });
        using var updateResponse = await _client.SendAsync(updateRequest);
        using var updatePayload = await updateResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(2, updatePayload!.RootElement.GetProperty("data").GetProperty("dayOfWeek").GetInt32());
        Assert.Equal("10:00", updatePayload.RootElement.GetProperty("data").GetProperty("startTime").GetString());
        Assert.Equal("16:30", updatePayload.RootElement.GetProperty("data").GetProperty("endTime").GetString());
        Assert.Equal("inactive", updatePayload.RootElement.GetProperty("data").GetProperty("status").GetString());

        using var activateRequest = AuthorizedRequest(
            HttpMethod.Patch,
            $"/resource-schedules/{scheduleId}/status",
            tenant.TenantId,
            body: new { status = "active" });
        using var activateResponse = await _client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        using var deleteRequest = AuthorizedRequest(
            HttpMethod.Delete,
            $"/resource-schedules/{scheduleId}",
            tenant.TenantId);
        using var deleteResponse = await _client.SendAsync(deleteRequest);
        using var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal("inactive", deletePayload!.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(1, "09:00", "09:00", "2026-06-16", null, "active")]
    [InlineData(1, "18:00", "09:00", "2026-06-16", null, "active")]
    [InlineData(0, "09:00", "18:00", "2026-06-16", null, "active")]
    [InlineData(8, "09:00", "18:00", "2026-06-16", null, "active")]
    [InlineData(1, "09:00", "18:00", "2026-06-16", "2026-01-01", "active")]
    [InlineData(1, "09:00", "18:00", "2026-06-16", null, "blocked")]
    public async Task Create_WithInvalidData_ReturnsValidationError(
        short dayOfWeek,
        string startTime,
        string endTime,
        string? validFrom,
        string? validTo,
        string status)
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var resourceId = await CreateResourceAsync(tenant.TenantId, branchId);
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resource-schedules",
            tenant.TenantId,
            body: ValidCreateRequest(branchId, resourceId, dayOfWeek, startTime, endTime, validFrom, validTo, status));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithResourceFromAnotherTenant_ReturnsResourceNotFound()
    {
        var owner = await CreateTenantAsync();
        var otherTenant = await CreateTenantAsync();
        var ownerBranchId = await CreateBranchAsync(owner.TenantId);
        var foreignResourceId = await CreateResourceAsync(owner.TenantId, ownerBranchId);
        var otherBranchId = await CreateBranchAsync(otherTenant.TenantId);
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resource-schedules",
            otherTenant.TenantId,
            body: ValidCreateRequest(otherBranchId, foreignResourceId));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("RESOURCE_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithResourceFromDifferentBranch_ReturnsValidationError()
    {
        var tenant = await CreateTenantAsync();
        var branchId = await CreateBranchAsync(tenant.TenantId);
        var otherBranchId = await CreateBranchAsync(tenant.TenantId);
        var resourceId = await CreateResourceAsync(tenant.TenantId, branchId);
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/resource-schedules",
            tenant.TenantId,
            body: ValidCreateRequest(otherBranchId, resourceId));

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsCompleteResourceScheduleEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/resource-schedules", out var collection));
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/resource-schedules/{scheduleId}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/resource-schedules/{scheduleId}/status", out var status));
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
            Name = "Tenant de horarios",
            Slug = $"test-resource-schedules-{Guid.NewGuid():N}",
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
            Name = "Sucursal horarios",
            Address = "Av. Horarios 123",
            Phone = "+59170000500",
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
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        var resource = new Resource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Name = "Recurso horarios",
            ResourceType = "silla",
            Description = "Recurso con horarios",
            Capacity = 1,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();
        return resource.ResourceId;
    }

    private static object ValidCreateRequest(
        Guid branchId,
        Guid resourceId,
        short dayOfWeek = 1,
        string startTime = "09:00",
        string endTime = "18:00",
        string? validFrom = "2026-06-16",
        string? validTo = null,
        string? status = null) => new
        {
            branchId,
            resourceId,
            dayOfWeek,
            startTime,
            endTime,
            validFrom,
            validTo,
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
