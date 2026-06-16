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

public sealed class AvailabilityEndpointTests(BookingApiFactory factory)
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
            .Where(tenant => tenant.Slug.StartsWith("booking-availability-"))
            .Select(tenant => tenant.TenantId)
            .ToListAsync();

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
    public async Task GetAvailability_WithActiveCompatibleResource_ReturnsSlots()
    {
        var setup = await CreateAvailabilitySetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, date));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(setup.BranchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal(setup.ServiceId, data.GetProperty("serviceId").GetGuid());
        Assert.Equal(15, data.GetProperty("slotMinutes").GetInt32());
        var slots = data.GetProperty("availableSlots").EnumerateArray().ToList();
        Assert.Equal(3, slots.Count);
        Assert.All(slots, slot => Assert.Equal(setup.ResourceId, slot.GetProperty("resourceId").GetGuid()));
        Assert.EndsWith("09:00:00-04:00", slots[0].GetProperty("startAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("09:30:00-04:00", slots[0].GetProperty("endAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("09:15:00-04:00", slots[1].GetProperty("startAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("09:30:00-04:00", slots[2].GetProperty("startAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAvailability_WithConfirmedReservation_RemovesOverlappingSlots()
    {
        var setup = await CreateAvailabilitySetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        await CreateReservationAsync(setup, date, "09:30", "10:00");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, date));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slots = payload!.RootElement.GetProperty("data").GetProperty("availableSlots").EnumerateArray().ToList();
        Assert.Single(slots);
        Assert.EndsWith("09:00:00-04:00", slots[0].GetProperty("startAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAvailability_WithActiveBlock_RemovesBlockedSlots()
    {
        var setup = await CreateAvailabilitySetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        await CreateResourceBlockAsync(setup, date, "09:00", "10:00");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, date));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(payload!.RootElement.GetProperty("data").GetProperty("availableSlots").EnumerateArray());
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("inactive")]
    public async Task GetAvailability_WithNonActiveCompatibleResource_DoesNotReturnThatResource(string resourceStatus)
    {
        var setup = await CreateAvailabilitySetupAsync();
        var date = FutureDate();
        await CreateScheduleAsync(setup, date, "09:00", "10:00");
        var nonActiveResourceId = await CreateAdditionalResourceAsync(setup, resourceStatus);
        await CreateScheduleAsync(setup with { ResourceId = nonActiveResourceId }, date, "09:00", "10:00");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, date));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resourceIds = payload!.RootElement.GetProperty("data").GetProperty("availableSlots")
            .EnumerateArray()
            .Select(slot => slot.GetProperty("resourceId").GetGuid())
            .Distinct()
            .ToList();
        Assert.Equal([setup.ResourceId], resourceIds);
    }

    [Fact]
    public async Task GetAvailability_ForToday_DoesNotReturnPastSlots()
    {
        var setup = await CreateAvailabilitySetupAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BranchTimeZone).DateTime);
        await CreateScheduleAsync(setup, today, "00:00", "23:59");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, today));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var now = DateTimeOffset.UtcNow;
        Assert.All(
            payload!.RootElement.GetProperty("data").GetProperty("availableSlots").EnumerateArray(),
            slot => Assert.True(slot.GetProperty("startAt").GetDateTimeOffset().ToUniversalTime() > now));
    }

    [Fact]
    public async Task GetAvailability_WithInvalidDate_ReturnsValidationError()
    {
        var setup = await CreateAvailabilitySetupAsync();

        using var response = await _client.GetAsync(
            $"/availability?tenantSlug={setup.TenantSlug}&branchId={setup.BranchId}&serviceId={setup.ServiceId}&date=16-06-2026");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetAvailability_WithInactiveTenant_ReturnsTenantNotFound()
    {
        var setup = await CreateAvailabilitySetupAsync(tenantStatus: "inactive");

        using var response = await _client.GetAsync(AvailabilityUrl(setup, FutureDate()));
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("TENANT_NOT_FOUND", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_ContainsAvailabilityEndpoint()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/availability", out var availability));
        Assert.True(availability.TryGetProperty("get", out _));
    }

    private async Task<AvailabilitySetup> CreateAvailabilitySetupAsync(string tenantStatus = "active")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var tenant = new CatalogTenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Tenant disponibilidad",
            Slug = $"booking-availability-{Guid.NewGuid():N}",
            MainCategory = "Servicios",
            Timezone = "America/La_Paz",
            Status = tenantStatus,
            CreatedAt = now,
            UpdatedAt = now
        };
        var branch = new Branch
        {
            BranchId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Sucursal disponibilidad",
            Address = "Av. Disponibilidad 123",
            Phone = "+59170000600",
            Timezone = "America/La_Paz",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        var service = new BookingDomainService
        {
            ServiceId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = "Corte disponibilidad",
            Description = "Servicio para disponibilidad",
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
            Name = "Profesional disponibilidad",
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

        return new AvailabilitySetup(tenant.TenantId, tenant.Slug, branch.BranchId, service.ServiceId, resource.ResourceId);
    }

    private async Task<Guid> CreateAdditionalResourceAsync(AvailabilitySetup setup, string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var resource = new Resource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            Name = $"Recurso {status}",
            ResourceType = "silla",
            Capacity = 1,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        dbContext.ServiceResources.Add(new ServiceResource
        {
            TenantId = setup.TenantId,
            ServiceId = setup.ServiceId,
            ResourceId = resource.ResourceId,
            Required = true,
            Priority = 2,
            Status = "active",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return resource.ResourceId;
    }

    private async Task CreateScheduleAsync(
        AvailabilitySetup setup,
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

    private async Task CreateReservationAsync(AvailabilitySetup setup, DateOnly date, string startTime, string endTime)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        dbContext.Reservations.Add(new Reservation
        {
            ReservationId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ClientUserId = Guid.NewGuid(),
            ServiceId = setup.ServiceId,
            ResourceId = setup.ResourceId,
            StartAt = ToBranchInstant(date, startTime).ToUniversalTime(),
            EndAt = ToBranchInstant(date, endTime).ToUniversalTime(),
            Status = "CONFIRMED",
            ChannelOrigin = "web",
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task CreateResourceBlockAsync(AvailabilitySetup setup, DateOnly date, string startTime, string endTime)
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

    private static DateOnly FutureDate() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BranchTimeZone).DateTime).AddDays(7);

    private static DateTimeOffset ToBranchInstant(DateOnly date, string time)
    {
        var parsedTime = TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        var localDateTime = DateTime.SpecifyKind(date.ToDateTime(parsedTime), DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, BranchTimeZone.GetUtcOffset(localDateTime));
    }

    private static string AvailabilityUrl(AvailabilitySetup setup, DateOnly date) =>
        $"/availability?tenantSlug={setup.TenantSlug}&branchId={setup.BranchId}&serviceId={setup.ServiceId}&date={date:yyyy-MM-dd}";

    private static short ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)dayOfWeek;

    private sealed record AvailabilitySetup(
        Guid TenantId,
        string TenantSlug,
        Guid BranchId,
        Guid ServiceId,
        Guid ResourceId);
}
