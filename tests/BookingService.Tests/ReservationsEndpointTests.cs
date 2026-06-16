using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingService.Data;
using BookingService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingDomainService = BookingService.Domain.Service;

namespace BookingService.Tests;

public sealed class ReservationsEndpointTests(BookingApiFactory factory)
    : IClassFixture<BookingApiFactory>, IAsyncLifetime
{
    private static readonly TimeZoneInfo BranchTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");
    private readonly BookingApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var tenantIds = await dbContext.Tenants
            .Where(tenant => tenant.Slug.StartsWith("booking-reservations-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

        await dbContext.ReservationEventOutbox
            .Where(outbox => tenantIds.Contains(outbox.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ReservationHistory
            .Where(history => tenantIds.Contains(history.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ResourceBlocks
            .Where(block => tenantIds.Contains(block.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.Reservations
            .Where(reservation => tenantIds.Contains(reservation.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ResourceSchedules
            .Where(schedule => tenantIds.Contains(schedule.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.ServiceResources
            .Where(serviceResource => tenantIds.Contains(serviceResource.TenantId))
            .ExecuteDeleteAsync();
        await dbContext.BranchServices
            .Where(branchService => tenantIds.Contains(branchService.TenantId))
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
    public async Task CreateReservation_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/reservations", new
        {
            branchId = Guid.NewGuid(),
            serviceId = Guid.NewGuid(),
            resourceId = Guid.NewGuid(),
            startAt = ToBranchInstant(FutureDate(), "09:00"),
            notes = "Reserva sin token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_WithTenantAdminRole_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        using var request = AuthorizedRequest("tenant_admin", new
        {
            branchId = setup.BranchId,
            serviceId = setup.ServiceId,
            resourceId = setup.ResourceId,
            startAt = ToBranchInstant(date, "09:00"),
            notes = "Reserva con rol incorrecto"
        });

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Client_CanCreateConfirmedReservation_WithHistoryAndOutbox()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        var startAt = ToBranchInstant(date, "09:00");
        using var request = AuthorizedRequest("client", new
        {
            branchId = setup.BranchId,
            serviceId = setup.ServiceId,
            resourceId = setup.ResourceId,
            startAt,
            notes = "Reserva de prueba"
        }, clientUserId);

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        var reservationId = data.GetProperty("reservationId").GetGuid();
        Assert.Equal(setup.TenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(setup.ResourceId, data.GetProperty("resourceId").GetGuid());
        Assert.Equal(clientUserId, data.GetProperty("clientUserId").GetGuid());
        Assert.Equal("CONFIRMED", data.GetProperty("status").GetString());
        Assert.EndsWith("09:00:00-04:00", data.GetProperty("startAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("09:30:00-04:00", data.GetProperty("endAt").GetString(), StringComparison.Ordinal);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var reservation = await dbContext.Reservations.SingleAsync(entity => entity.ReservationId == reservationId);
        Assert.Equal("CONFIRMED", reservation.Status);
        Assert.Equal(startAt.ToUniversalTime(), reservation.StartAt);

        var history = await dbContext.ReservationHistory.SingleAsync(entity => entity.ReservationId == reservationId);
        Assert.Equal("CREATED", history.Action);
        Assert.Equal("CONFIRMED", history.NewStatus);
        Assert.Equal(clientUserId, history.UserId);

        var outbox = await dbContext.ReservationEventOutbox.SingleAsync(entity => entity.AggregateId == reservationId);
        Assert.Equal("ReservationCreated", outbox.EventType);
        Assert.Equal("PENDING", outbox.Status);
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(reservationId, eventPayload.RootElement.GetProperty("reservationId").GetGuid());
        Assert.Equal("CONFIRMED", eventPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Client_CanCreateReservation_WithoutResourceId_UsesFirstAvailableCompatibleResource()
    {
        var setup = await CreateReservationSetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        using var request = AuthorizedRequest("client", new
        {
            branchId = setup.BranchId,
            serviceId = setup.ServiceId,
            startAt = ToBranchInstant(date, "09:15"),
            notes = "Reserva sin recurso explicito"
        });

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(setup.ResourceId, payload!.RootElement.GetProperty("data").GetProperty("resourceId").GetGuid());
    }

    [Fact]
    public async Task CreateReservation_WhenSlotAlreadyTaken_ReturnsConflict()
    {
        var setup = await CreateReservationSetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        var body = new
        {
            branchId = setup.BranchId,
            serviceId = setup.ServiceId,
            resourceId = setup.ResourceId,
            startAt = ToBranchInstant(date, "09:00"),
            notes = "Reserva duplicada"
        };
        using var firstRequest = AuthorizedRequest("client", body);
        using var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        using var secondRequest = AuthorizedRequest("client", body);
        using var secondResponse = await _client.SendAsync(secondRequest);
        using var payload = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal("SLOT_ALREADY_TAKEN", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateReservation_WhenResourceBlocked_ReturnsConflict()
    {
        var setup = await CreateReservationSetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        await CreateResourceBlockAsync(setup, date, "09:00", "10:00");
        using var request = AuthorizedRequest("client", new
        {
            branchId = setup.BranchId,
            serviceId = setup.ServiceId,
            resourceId = setup.ResourceId,
            startAt = ToBranchInstant(date, "09:00"),
            notes = "Reserva bloqueada"
        });

        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESOURCE_BLOCKED", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsReservationCreationEndpoint()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/reservations", out var reservations));
        Assert.True(reservations.TryGetProperty("post", out _));
    }

    private async Task<ReservationSetup> CreateReservationSetupAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant reservas",
            Slug = $"booking-reservations-{Guid.NewGuid():N}",
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
            Name = "Sucursal reservas",
            Address = "Av. Reservas 123",
            Phone = "+59170000700",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var service = new BookingDomainService
        {
            ServiceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Servicio reservas",
            Description = "Servicio para reservar",
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
            Name = "Recurso reservas",
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

        dbContext.BranchServices.Add(new BranchService
        {
            TenantId = tenant.TenantId,
            BranchId = branch.BranchId,
            ServiceId = service.ServiceId,
            Status = "active",
            CreatedAt = now
        });
        dbContext.ServiceResources.Add(new ServiceResource
        {
            TenantId = tenant.TenantId,
            ServiceId = service.ServiceId,
            ResourceId = resource.ResourceId,
            Required = true,
            Priority = 1,
            Status = "active",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync();

        return new ReservationSetup(tenant.TenantId, branch.BranchId, service.ServiceId, resource.ResourceId);
    }

    private async Task CreateScheduleAsync(
        ReservationSetup setup,
        DateOnly date,
        string startTime,
        string endTime)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        dbContext.ResourceSchedules.Add(new ResourceSchedule
        {
            ScheduleId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ResourceId = setup.ResourceId,
            DayOfWeek = ToIsoDayOfWeek(date.DayOfWeek),
            StartTime = TimeOnly.ParseExact(startTime, "HH:mm", CultureInfo.InvariantCulture),
            EndTime = TimeOnly.ParseExact(endTime, "HH:mm", CultureInfo.InvariantCulture),
            ValidFrom = date,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task CreateResourceBlockAsync(ReservationSetup setup, DateOnly date, string startTime, string endTime)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        dbContext.ResourceBlocks.Add(new ResourceBlock
        {
            BlockId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ResourceId = setup.ResourceId,
            StartAt = ToBranchInstant(date, startTime).ToUniversalTime(),
            EndAt = ToBranchInstant(date, endTime).ToUniversalTime(),
            Reason = "Bloqueo de prueba",
            BlockType = "manual",
            Status = "ACTIVE",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private static HttpRequestMessage AuthorizedRequest(string role, object body, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/reservations");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", (userId ?? Guid.NewGuid()).ToString());
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static DateOnly FutureDate() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BranchTimeZone).DateTime).AddDays(9);

    private static DateTimeOffset ToBranchInstant(DateOnly date, string time)
    {
        var parsedTime = TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        var localDateTime = DateTime.SpecifyKind(date.ToDateTime(parsedTime), DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, BranchTimeZone.GetUtcOffset(localDateTime));
    }

    private static short ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)dayOfWeek;

    private sealed record ReservationSetup(
        Guid TenantId,
        Guid BranchId,
        Guid ServiceId,
        Guid ResourceId);
}
