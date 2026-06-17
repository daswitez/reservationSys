using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingService.Data;
using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingDomainService = BookingService.Domain.Service;

namespace BookingService.Tests;

public sealed class ReservationSearchEndpointTests(BookingApiFactory factory)
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
            .Where(t => t.Slug.StartsWith("search-tests-"))
            .Select(t => t.TenantId)
            .ToListAsync();

        await dbContext.ReservationEventOutbox
            .Where(o => tenantIds.Contains(o.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ReservationHistory
            .Where(h => tenantIds.Contains(h.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Reservations
            .Where(r => tenantIds.Contains(r.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ServiceResources
            .Where(sr => tenantIds.Contains(sr.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.BranchServices
            .Where(bs => tenantIds.Contains(bs.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Resources
            .Where(r => tenantIds.Contains(r.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Services
            .Where(s => tenantIds.Contains(s.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Branches
            .Where(b => tenantIds.Contains(b.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Tenants
            .Where(t => tenantIds.Contains(t.TenantId))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Search_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync("/admin/reservations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_CannotSearch_ReturnsForbidden()
    {
        using var request = SearchRequest("client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanSearchReservationsInOwnTenant()
    {
        var setup = await CreateSearchSetupAsync();
        var reservationId = await SeedReservationAsync(setup);

        using var request = SearchRequest("tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.True(data.GetArrayLength() >= 1);
        var found = data.EnumerateArray().Any(i => i.GetProperty("reservationId").GetGuid() == reservationId);
        Assert.True(found);
        // History array is always present (may be empty when seeded directly)
        Assert.Equal(JsonValueKind.Array, data[0].GetProperty("history").ValueKind);
    }

    [Fact]
    public async Task Search_IncludesHistoryWhenReservationWasCancelled()
    {
        var adminId = Guid.NewGuid();
        var setup = await CreateSearchSetupAsync();
        var reservationId = await SeedReservationAsync(setup);

        // Cancel via endpoint → creates CANCELLED history entry
        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Patch, $"/reservations/{reservationId}/cancel");
        cancelRequest.Headers.Add("X-Test-Role", "tenant_admin");
        cancelRequest.Headers.Add("X-Test-User-Id", adminId.ToString());
        cancelRequest.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        cancelRequest.Content = JsonContent.Create(new { reason = "Test de historial" });
        using var cancelResponse = await _client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Search → must include history with CANCELLED action
        using var request = SearchRequest("tenant_admin", Guid.NewGuid(), setup.TenantId,
            queryString: $"?status=CANCELLED");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = payload!.RootElement.GetProperty("data").EnumerateArray().ToList();
        var item = items.Single(i => i.GetProperty("reservationId").GetGuid() == reservationId);
        var history = item.GetProperty("history").EnumerateArray().ToList();
        Assert.Single(history);
        Assert.Equal("CANCELLED", history[0].GetProperty("action").GetString());
        Assert.Equal("Cancelado por el cliente" is string _, history[0].TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task TenantAdmin_CannotSeeReservationsFromOtherTenant()
    {
        var setup = await CreateSearchSetupAsync();
        await SeedReservationAsync(setup);

        // Query with a different tenant_id — should return empty list, not forbidden
        using var request = SearchRequest("tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, payload!.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task BranchAdmin_CanOnlySeeReservationsInOwnBranch()
    {
        var setup = await CreateSearchSetupAsync();
        await SeedReservationAsync(setup);

        // Query with correct branch → sees results
        using var ownRequest = SearchRequest(
            "branch_admin", Guid.NewGuid(),
            tenantId: setup.TenantId, claimBranchId: setup.BranchId);
        using var ownResponse = await _client.SendAsync(ownRequest);
        using var ownPayload = await ownResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.True(ownPayload!.RootElement.GetProperty("data").GetArrayLength() >= 1);

        // Query with different branch → empty
        using var otherRequest = SearchRequest(
            "branch_admin", Guid.NewGuid(),
            tenantId: setup.TenantId, claimBranchId: Guid.NewGuid());
        using var otherResponse = await _client.SendAsync(otherRequest);
        using var otherPayload = await otherResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.Equal(0, otherPayload!.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Search_FilterByStatus_ReturnsOnlyMatching()
    {
        var setup = await CreateSearchSetupAsync();
        var r1 = await SeedReservationAsync(setup, status: "CONFIRMED");
        var r2 = await SeedReservationAsync(setup, status: "CANCELLED");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/admin/reservations?status=CONFIRMED");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = payload!.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.True(items.All(i => i.GetProperty("status").GetString() == "CONFIRMED"));
        Assert.Contains(items, i => i.GetProperty("reservationId").GetGuid() == r1);
        Assert.DoesNotContain(items, i => i.GetProperty("reservationId").GetGuid() == r2);
    }

    [Fact]
    public async Task Search_FilterByClientUserId_ReturnsOnlyMatching()
    {
        var setup = await CreateSearchSetupAsync();
        var targetClient = Guid.NewGuid();
        var r1 = await SeedReservationAsync(setup, clientUserId: targetClient);

        // Filter by targetClient → finds r1
        using var hitRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/reservations?clientUserId={targetClient}");
        hitRequest.Headers.Add("X-Test-Role", "tenant_admin");
        hitRequest.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        hitRequest.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var hitResponse = await _client.SendAsync(hitRequest);
        using var hitPayload = await hitResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, hitResponse.StatusCode);
        var hitItems = hitPayload!.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Single(hitItems);
        Assert.Equal(r1, hitItems[0].GetProperty("reservationId").GetGuid());

        // Filter by a different clientUserId → 0 results
        using var missRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/reservations?clientUserId={Guid.NewGuid()}");
        missRequest.Headers.Add("X-Test-Role", "tenant_admin");
        missRequest.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        missRequest.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var missResponse = await _client.SendAsync(missRequest);
        using var missPayload = await missResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, missResponse.StatusCode);
        Assert.Equal(0, missPayload!.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Search_FilterByDateRange_ReturnsOnlyMatching()
    {
        var setup = await CreateSearchSetupAsync();
        var future = DateTimeOffset.UtcNow.AddDays(30);
        var farFuture = DateTimeOffset.UtcNow.AddDays(60);
        var r1 = await SeedReservationAsync(setup, startAt: future);
        var r2 = await SeedReservationAsync(setup, startAt: farFuture);

        var dateFrom = DateOnly.FromDateTime(future.AddDays(-1).UtcDateTime).ToString("yyyy-MM-dd");
        var dateTo = DateOnly.FromDateTime(future.AddDays(1).UtcDateTime).ToString("yyyy-MM-dd");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/reservations?dateFrom={dateFrom}&dateTo={dateTo}");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = payload!.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("reservationId").GetGuid() == r1);
        Assert.DoesNotContain(items, i => i.GetProperty("reservationId").GetGuid() == r2);
    }

    [Fact]
    public async Task Search_WithInvalidDateFrom_ReturnsBadRequest()
    {
        using var request = SearchRequest("tenant_admin", Guid.NewGuid(), Guid.NewGuid(),
            queryString: "?dateFrom=01-07-2026");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<SearchSetup> CreateSearchSetupAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant search",
            Slug = $"search-tests-{Guid.NewGuid():N}",
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
            Name = "Sucursal search",
            Address = "Av. Search 1",
            Phone = "+59170001000",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var service = new BookingDomainService
        {
            ServiceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Servicio search",
            DurationMinutes = 30,
            ReferencePrice = 50,
            Modality = "presencial",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var resource = new Resource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            BranchId = branch.BranchId,
            Name = "Recurso search",
            ResourceType = "profesional",
            Capacity = 1,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Tenants.Add(tenant);
        dbContext.Branches.Add(branch);
        dbContext.Services.Add(service);
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        return new SearchSetup(tenant.TenantId, branch.BranchId, service.ServiceId, resource.ResourceId);
    }

    private async Task<Guid> SeedReservationAsync(
        SearchSetup setup,
        string status = "CONFIRMED",
        Guid? clientUserId = null,
        DateTimeOffset? startAt = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var start = (startAt ?? now.AddDays(20)).ToUniversalTime();
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ClientUserId = clientUserId ?? Guid.NewGuid(),
            ServiceId = setup.ServiceId,
            ResourceId = setup.ResourceId,
            CreatedByUserId = Guid.NewGuid(),
            StartAt = start,
            EndAt = start.AddMinutes(30),
            Status = status,
            ChannelOrigin = "web",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.ReservationId;
    }

    private static HttpRequestMessage SearchRequest(
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? claimBranchId = null,
        string queryString = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/reservations{queryString}");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (claimBranchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", claimBranchId.Value.ToString());
        return request;
    }

    private sealed record SearchSetup(
        Guid TenantId, Guid BranchId, Guid ServiceId, Guid ResourceId);
}
