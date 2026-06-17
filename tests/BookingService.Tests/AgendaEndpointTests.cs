using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingService.Data;
using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingDomainService = BookingService.Domain.Service;

namespace BookingService.Tests;

public sealed class AgendaEndpointTests(BookingApiFactory factory)
    : IClassFixture<BookingApiFactory>, IAsyncLifetime
{
    private readonly BookingApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly TimeZoneInfo BranchTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var tenantIds = await dbContext.Tenants
            .Where(t => t.Slug.StartsWith("agenda-tests-"))
            .Select(t => t.TenantId)
            .ToListAsync();

        await dbContext.ReservationEventOutbox
            .Where(o => tenantIds.Contains(o.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ReservationHistory
            .Where(h => tenantIds.Contains(h.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ResourceBlocks
            .Where(b => tenantIds.Contains(b.TenantId))
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
        await dbContext.ResourceSchedules
            .Where(s => tenantIds.Contains(s.TenantId))
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
    public async Task GetAgenda_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync(
            $"/admin/agenda?branchId={Guid.NewGuid()}&date=2026-07-01");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_CannotGetAgenda_ReturnsForbidden()
    {
        var setup = await CreateAgendaSetupAsync();
        using var request = AgendaRequest(setup.BranchId, AgendaDate(), "client", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanGetAgendaForBranchInOwnTenant_ReturnsReservationsAndBlocks()
    {
        var setup = await CreateAgendaSetupAsync();
        var date = AgendaDate();
        await SeedReservationAsync(setup, date);
        await SeedBlockAsync(setup, date);

        using var request = AgendaRequest(setup.BranchId, date, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(setup.BranchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal(1, data.GetProperty("reservations").GetArrayLength());
        Assert.Equal(1, data.GetProperty("blocks").GetArrayLength());

        var reservation = data.GetProperty("reservations")[0];
        Assert.EndsWith("09:00:00-04:00", reservation.GetProperty("startAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("09:30:00-04:00", reservation.GetProperty("endAt").GetString(), StringComparison.Ordinal);

        var block = data.GetProperty("blocks")[0];
        Assert.EndsWith("12:00:00-04:00", block.GetProperty("startAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("14:00:00-04:00", block.GetProperty("endAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantAdmin_CannotGetAgendaForBranchInOtherTenant_ReturnsForbidden()
    {
        var setup = await CreateAgendaSetupAsync();
        using var request = AgendaRequest(setup.BranchId, AgendaDate(), "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CanGetAgendaForOwnBranch()
    {
        var setup = await CreateAgendaSetupAsync();
        var date = AgendaDate();
        await SeedReservationAsync(setup, date);

        using var request = AgendaRequest(
            setup.BranchId, date, "branch_admin", Guid.NewGuid(),
            tenantId: setup.TenantId, claimBranchId: setup.BranchId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, payload!.RootElement.GetProperty("data").GetProperty("reservations").GetArrayLength());
    }

    [Fact]
    public async Task BranchAdmin_CannotGetAgendaForOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateAgendaSetupAsync();
        using var request = AgendaRequest(
            setup.BranchId, AgendaDate(), "branch_admin", Guid.NewGuid(),
            tenantId: setup.TenantId, claimBranchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAgenda_FilterByResource_ReturnsOnlyMatchingReservations()
    {
        var setup = await CreateAgendaSetupAsync();
        var date = AgendaDate();
        await SeedReservationAsync(setup, date);

        // Filter by a different (non-existent) resource → 0 results
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/agenda?branchId={setup.BranchId}&date={date:yyyy-MM-dd}&resourceId={Guid.NewGuid()}");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, payload!.RootElement.GetProperty("data").GetProperty("reservations").GetArrayLength());
    }

    [Fact]
    public async Task GetAgenda_FilterByStatus_ReturnsOnlyMatchingReservations()
    {
        var setup = await CreateAgendaSetupAsync();
        var date = AgendaDate();
        await SeedReservationAsync(setup, date, status: "CONFIRMED");
        await SeedReservationAsync(setup, date, status: "CANCELLED", offsetHours: 2);

        // Filter CONFIRMED only → 1 result
        using var confirmed = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/agenda?branchId={setup.BranchId}&date={date:yyyy-MM-dd}&status=CONFIRMED");
        confirmed.Headers.Add("X-Test-Role", "tenant_admin");
        confirmed.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        confirmed.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var confirmedResponse = await _client.SendAsync(confirmed);
        using var confirmedPayload = await confirmedResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);
        Assert.Equal(1, confirmedPayload!.RootElement.GetProperty("data").GetProperty("reservations").GetArrayLength());
    }

    [Fact]
    public async Task GetAgenda_WithInvalidDate_ReturnsBadRequest()
    {
        var setup = await CreateAgendaSetupAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/agenda?branchId={setup.BranchId}&date=16-06-2026");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", setup.TenantId.ToString());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetAgenda_WhenBranchNotFound_ReturnsNotFound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/agenda?branchId={Guid.NewGuid()}&date=2026-07-01");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", Guid.NewGuid().ToString());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<AgendaSetup> CreateAgendaSetupAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant agenda",
            Slug = $"agenda-tests-{Guid.NewGuid():N}",
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
            Name = "Sucursal agenda",
            Address = "Av. Agenda 1",
            Phone = "+59170000900",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var service = new BookingDomainService
        {
            ServiceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Servicio agenda",
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
            Name = "Recurso agenda",
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

        return new AgendaSetup(tenant.TenantId, branch.BranchId, service.ServiceId, resource.ResourceId);
    }

    private async Task SeedReservationAsync(
        AgendaSetup setup,
        DateOnly date,
        string status = "CONFIRMED",
        int offsetHours = 0)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9 + offsetHours)));
        var startAt = TimeZoneInfo.ConvertTimeToUtc(localStart, BranchTz);
        dbContext.Reservations.Add(new Reservation
        {
            ReservationId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ClientUserId = Guid.NewGuid(),
            ServiceId = setup.ServiceId,
            ResourceId = setup.ResourceId,
            CreatedByUserId = Guid.NewGuid(),
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30),
            Status = status,
            ChannelOrigin = "web",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedBlockAsync(AgendaSetup setup, DateOnly date)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(12)));
        var startAt = TimeZoneInfo.ConvertTimeToUtc(localStart, BranchTz);
        dbContext.ResourceBlocks.Add(new ResourceBlock
        {
            BlockId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ResourceId = setup.ResourceId,
            StartAt = startAt,
            EndAt = startAt.AddHours(2),
            Reason = "Mantenimiento",
            BlockType = "manual",
            Status = "ACTIVE",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private static HttpRequestMessage AgendaRequest(
        Guid branchId,
        DateOnly date,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? claimBranchId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/agenda?branchId={branchId}&date={date:yyyy-MM-dd}");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (claimBranchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", claimBranchId.Value.ToString());
        return request;
    }

    private static DateOnly AgendaDate() =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BranchTz).DateTime).AddDays(15);

    private sealed record AgendaSetup(
        Guid TenantId, Guid BranchId, Guid ServiceId, Guid ResourceId);
}
