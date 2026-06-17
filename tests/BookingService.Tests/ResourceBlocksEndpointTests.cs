using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingService.Data;
using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Tests;

public sealed class ResourceBlocksEndpointTests(BookingApiFactory factory)
    : IClassFixture<BookingApiFactory>, IAsyncLifetime
{
    private readonly BookingApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var tenantIds = await dbContext.Tenants
            .Where(t => t.Slug.StartsWith("block-tests-"))
            .Select(t => t.TenantId)
            .ToListAsync();

        await dbContext.ReservationEventOutbox
            .Where(o => tenantIds.Contains(o.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ResourceBlocks
            .Where(b => tenantIds.Contains(b.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Resources
            .Where(r => tenantIds.Contains(r.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Branches
            .Where(b => tenantIds.Contains(b.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(t => tenantIds.Contains(t.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CreateBlock_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/resource-blocks", new
        {
            resourceId = Guid.NewGuid(),
            startAt = DateTimeOffset.UtcNow.AddHours(1),
            endAt = DateTimeOffset.UtcNow.AddHours(3),
            reason = "Test sin token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_CannotCreateBlock_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(setup, "client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCreateBlockForResourceInOwnTenant_WithOutbox()
    {
        var adminId = Guid.NewGuid();
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(setup, "tenant_admin", adminId, setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        var blockId = data.GetProperty("blockId").GetGuid();
        Assert.Equal("ACTIVE", data.GetProperty("status").GetString());
        Assert.Equal(setup.ResourceId, data.GetProperty("resourceId").GetGuid());
        Assert.Equal("manual", data.GetProperty("blockType").GetString());
        Assert.Equal("Mantenimiento programado", data.GetProperty("reason").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var outbox = await dbContext.ReservationEventOutbox
            .SingleAsync(o => o.AggregateId == blockId && o.EventType == "ResourceBlockCreated");
        Assert.Equal("PENDING", outbox.Status);
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(blockId, eventPayload.RootElement.GetProperty("blockId").GetGuid());
        Assert.Equal("ACTIVE", eventPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task BranchAdmin_CanCreateBlockForResourceInOwnBranch()
    {
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(
            setup,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            setup.BranchId);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotCreateBlockForResourceInOtherTenant_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(setup, "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotCreateBlockForResourceInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(
            setup,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            Guid.NewGuid());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateBlock_WithOverlappingActiveBlock_ReturnsConflict()
    {
        var setup = await CreateBlockSetupAsync();

        using var first = BlockRequest(setup, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var firstResponse = await _client.SendAsync(first);
        firstResponse.EnsureSuccessStatusCode();

        using var second = BlockRequest(setup, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var secondResponse = await _client.SendAsync(second);
        using var payload = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal("BLOCK_OVERLAP", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateBlock_WhenResourceNotFound_ReturnsNotFound()
    {
        var setup = await CreateBlockSetupAsync();
        using var request = BlockRequest(
            setup with { ResourceId = Guid.NewGuid() },
            "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateBlock_WithEndAtBeforeStartAt_ReturnsBadRequest()
    {
        var setup = await CreateBlockSetupAsync();
        var now = DateTimeOffset.UtcNow;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/resource-blocks");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        request.Content = JsonContent.Create(new
        {
            resourceId = setup.ResourceId,
            startAt = now.AddHours(3),
            endAt = now.AddHours(1),
            reason = "Rango invalido"
        });
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelBlock_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PatchAsync(
            $"/resource-blocks/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCancelActiveBlock_WithOutbox()
    {
        var adminId = Guid.NewGuid();
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = CancelBlockRequest(blockId, "tenant_admin", adminId, setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CANCELLED", payload!.RootElement.GetProperty("data").GetProperty("status").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var outbox = await dbContext.ReservationEventOutbox
            .SingleAsync(o => o.AggregateId == blockId && o.EventType == "ResourceBlockCancelled");
        Assert.Equal("PENDING", outbox.Status);
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(blockId, eventPayload.RootElement.GetProperty("blockId").GetGuid());
        Assert.Equal(adminId, eventPayload.RootElement.GetProperty("cancelledByUserId").GetGuid());
        Assert.Equal("CANCELLED", eventPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Client_CannotCancelBlock_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = CancelBlockRequest(blockId, "client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotCancelBlockInOtherTenant_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = CancelBlockRequest(blockId, "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotCancelBlockInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = CancelBlockRequest(
            blockId,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Client_CannotGetBlock_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = GetBlockRequest(blockId, "client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotGetBlockInOtherTenant_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = GetBlockRequest(blockId, "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotGetBlockInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup);

        using var request = GetBlockRequest(
            blockId,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelBlock_AlreadyCancelledBlock_ReturnsConflict()
    {
        var setup = await CreateBlockSetupAsync();
        var blockId = await CreateBlockInDbAsync(setup, status: "CANCELLED");

        using var request = CancelBlockRequest(blockId, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BLOCK_NOT_CANCELLABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelBlock_NonExistentBlock_ReturnsNotFound()
    {
        using var request = CancelBlockRequest(Guid.NewGuid(), "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateBlockInDbAsync(BlockSetup setup, string status = "ACTIVE")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var block = new ResourceBlock
        {
            BlockId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ResourceId = setup.ResourceId,
            StartAt = now.AddDays(2).ToUniversalTime(),
            EndAt = now.AddDays(2).AddHours(4).ToUniversalTime(),
            Reason = "Bloqueo de prueba",
            BlockType = "manual",
            Status = status,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.ResourceBlocks.Add(block);
        await dbContext.SaveChangesAsync();
        return block.BlockId;
    }

    private static HttpRequestMessage CancelBlockRequest(
        Guid blockId,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/resource-blocks/{blockId}/cancel");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        return request;
    }

    private static HttpRequestMessage GetBlockRequest(
        Guid blockId,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/resource-blocks/{blockId}");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        return request;
    }

    private async Task<BlockSetup> CreateBlockSetupAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant bloqueos",
            Slug = $"block-tests-{Guid.NewGuid():N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var branch = new Branch
        {
            BranchId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Sucursal bloqueos",
            Address = "Av. Bloqueos 1",
            Phone = "+59170000800",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var resource = new Resource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            BranchId = branch.BranchId,
            Name = "Recurso bloqueo",
            ResourceType = "profesional",
            Capacity = 1,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Tenants.Add(tenant);
        dbContext.Branches.Add(branch);
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        return new BlockSetup(tenant.TenantId, branch.BranchId, resource.ResourceId);
    }

    private static HttpRequestMessage BlockRequest(
        BlockSetup setup,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new HttpRequestMessage(HttpMethod.Post, "/resource-blocks");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        request.Content = JsonContent.Create(new
        {
            resourceId = setup.ResourceId,
            startAt = now.AddDays(1),
            endAt = now.AddDays(1).AddHours(2),
            reason = "Mantenimiento programado",
            blockType = "manual"
        });
        return request;
    }

    private sealed record BlockSetup(Guid TenantId, Guid BranchId, Guid ResourceId);
}
