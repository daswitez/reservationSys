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
    public async Task GetById_ClientOwner_ReturnsReservation()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = GetByIdRequest(reservationId, "client", clientUserId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(reservationId, data.GetProperty("reservationId").GetGuid());
        Assert.Equal(TimeSpan.FromHours(-4), data.GetProperty("startAt").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.FromHours(-4), data.GetProperty("endAt").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task GetById_OtherClient_ReturnsForbidden()
    {
        var ownerClientId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, ownerClientId);

        using var request = GetByIdRequest(reservationId, "client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_TenantAdminSameTenant_ReturnsReservation()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = GetByIdRequest(
            reservationId,
            "tenant_admin",
            Guid.NewGuid(),
            tenantId: setup.TenantId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_TenantAdminOtherTenant_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = GetByIdRequest(
            reservationId,
            "tenant_admin",
            Guid.NewGuid(),
            tenantId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetById_BranchAdminSameBranch_ReturnsReservation()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = GetByIdRequest(
            reservationId,
            "branch_admin",
            Guid.NewGuid(),
            tenantId: setup.TenantId,
            branchId: setup.BranchId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_BranchAdminOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = GetByIdRequest(
            reservationId,
            "branch_admin",
            Guid.NewGuid(),
            tenantId: setup.TenantId,
            branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PatchAsJsonAsync(
            $"/reservations/{Guid.NewGuid()}/cancel",
            new { reason = "sin token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_CanCancelOwnConfirmedReservation_WithHistoryAndOutbox()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = CancelRequest(reservationId, "client", clientUserId, "Cancelado por el cliente");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("CANCELLED", data.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.FromHours(-4), data.GetProperty("startAt").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.FromHours(-4), data.GetProperty("endAt").GetDateTimeOffset().Offset);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var history = await dbContext.ReservationHistory
            .SingleAsync(h => h.ReservationId == reservationId && h.Action == "CANCELLED");
        Assert.Equal("CONFIRMED", history.PreviousStatus);
        Assert.Equal("CANCELLED", history.NewStatus);
        Assert.Equal(clientUserId, history.UserId);
        Assert.Equal("Cancelado por el cliente", history.Reason);

        var outbox = await dbContext.ReservationEventOutbox
            .SingleAsync(o => o.AggregateId == reservationId && o.EventType == "ReservationCancelled");
        Assert.Equal("PENDING", outbox.Status);
    }

    [Fact]
    public async Task Client_CannotCancelAnotherClientsReservation_ReturnsForbidden()
    {
        var ownerClientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, ownerClientId);

        using var request = CancelRequest(reservationId, "client", otherClientId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanCancelReservationInOwnTenant()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = CancelRequest(reservationId, "tenant_admin", Guid.NewGuid(), tenantId: setup.TenantId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotCancelReservationInOtherTenant_ReturnsForbidden()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = CancelRequest(reservationId, "tenant_admin", Guid.NewGuid(), tenantId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotCancelReservationInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = CancelRequest(
            reservationId,
            "branch_admin",
            Guid.NewGuid(),
            tenantId: setup.TenantId,
            branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledReservation_ReturnsConflict()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId, status: "CANCELLED");

        using var request = CancelRequest(reservationId, "client", clientUserId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESERVATION_NOT_CANCELLABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_NonExistentReservation_ReturnsNotFound()
    {
        using var request = CancelRequest(Guid.NewGuid(), "client", Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Attend_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PatchAsync(
            $"/reservations/{Guid.NewGuid()}/attend", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanAttendConfirmedReservation_WithHistoryAndOutbox()
    {
        var adminId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = AttendRequest(reservationId, "tenant_admin", adminId, setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ATTENDED", payload!.RootElement.GetProperty("data").GetProperty("status").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var history = await dbContext.ReservationHistory
            .SingleAsync(h => h.ReservationId == reservationId && h.Action == "ATTENDED");
        Assert.Equal("CONFIRMED", history.PreviousStatus);
        Assert.Equal("ATTENDED", history.NewStatus);
        Assert.Equal(adminId, history.UserId);

        var outbox = await dbContext.ReservationEventOutbox
            .SingleAsync(o => o.AggregateId == reservationId && o.EventType == "ReservationAttended");
        Assert.Equal("PENDING", outbox.Status);
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(reservationId, eventPayload.RootElement.GetProperty("reservationId").GetGuid());
        Assert.Equal("ATTENDED", eventPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Client_CannotAttendReservation_ReturnsForbidden()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = AttendRequest(reservationId, "client", clientUserId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotAttendReservationInOtherTenant_ReturnsForbidden()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = AttendRequest(reservationId, "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotAttendReservationInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = AttendRequest(
            reservationId,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Attend_AlreadyAttendedReservation_ReturnsConflict()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId, status: "ATTENDED");

        using var request = AttendRequest(reservationId, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESERVATION_NOT_ATTENDABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Attend_CancelledReservation_ReturnsConflict()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId, status: "CANCELLED");

        using var request = AttendRequest(reservationId, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESERVATION_NOT_ATTENDABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Attend_NonExistentReservation_ReturnsNotFound()
    {
        using var request = AttendRequest(Guid.NewGuid(), "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoShow_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.PatchAsync(
            $"/reservations/{Guid.NewGuid()}/no-show", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanMarkNoShowOnConfirmedReservation_WithHistoryAndOutbox()
    {
        var adminId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = NoShowRequest(reservationId, "tenant_admin", adminId, setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NO_SHOW", payload!.RootElement.GetProperty("data").GetProperty("status").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var history = await dbContext.ReservationHistory
            .SingleAsync(h => h.ReservationId == reservationId && h.Action == "NO_SHOW");
        Assert.Equal("CONFIRMED", history.PreviousStatus);
        Assert.Equal("NO_SHOW", history.NewStatus);
        Assert.Equal(adminId, history.UserId);

        var outbox = await dbContext.ReservationEventOutbox
            .SingleAsync(o => o.AggregateId == reservationId && o.EventType == "ReservationNoShow");
        Assert.Equal("PENDING", outbox.Status);
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(reservationId, eventPayload.RootElement.GetProperty("reservationId").GetGuid());
        Assert.Equal("NO_SHOW", eventPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Client_CannotMarkNoShow_ReturnsForbidden()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = NoShowRequest(reservationId, "client", clientUserId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotMarkNoShowInOtherTenant_ReturnsForbidden()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId);

        using var request = NoShowRequest(reservationId, "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotMarkNoShowInOtherBranch_ReturnsForbidden()
    {
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, Guid.NewGuid());

        using var request = NoShowRequest(
            reservationId,
            "branch_admin",
            Guid.NewGuid(),
            setup.TenantId,
            branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoShow_AlreadyNoShowReservation_ReturnsConflict()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId, status: "NO_SHOW");

        using var request = NoShowRequest(reservationId, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESERVATION_NOT_NO_SHOWABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task NoShow_CancelledReservation_ReturnsConflict()
    {
        var clientUserId = Guid.NewGuid();
        var setup = await CreateReservationSetupAsync();
        var reservationId = await CreateReservationInDbAsync(setup, clientUserId, status: "CANCELLED");

        using var request = NoShowRequest(reservationId, "tenant_admin", Guid.NewGuid(), setup.TenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("RESERVATION_NOT_NO_SHOWABLE", payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task NoShow_NonExistentReservation_ReturnsNotFound()
    {
        using var request = NoShowRequest(Guid.NewGuid(), "tenant_admin", Guid.NewGuid(), Guid.NewGuid());
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        Assert.True(paths.TryGetProperty("/reservations/{reservationId}", out var reservationById));
        Assert.True(reservationById.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/reservations/{reservationId}/cancel", out var cancel));
        Assert.True(cancel.TryGetProperty("patch", out _));
        Assert.True(paths.TryGetProperty("/reservations/{reservationId}/attend", out var attend));
        Assert.True(attend.TryGetProperty("patch", out _));
        Assert.True(paths.TryGetProperty("/reservations/{reservationId}/no-show", out var noShow));
        Assert.True(noShow.TryGetProperty("patch", out _));
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

    private static HttpRequestMessage CancelRequest(
        Guid reservationId,
        string role,
        Guid userId,
        string? reason = null,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/reservations/{reservationId}/cancel");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        request.Content = JsonContent.Create(new { reason });
        return request;
    }

    private static HttpRequestMessage GetByIdRequest(
        Guid reservationId,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/reservations/{reservationId}");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        return request;
    }

    private static HttpRequestMessage NoShowRequest(
        Guid reservationId,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/reservations/{reservationId}/no-show");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        return request;
    }

    private static HttpRequestMessage AttendRequest(
        Guid reservationId,
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/reservations/{reservationId}/attend");
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (branchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", branchId.Value.ToString());
        return request;
    }

    private async Task<Guid> CreateReservationInDbAsync(
        ReservationSetup setup,
        Guid clientUserId,
        string status = "CONFIRMED")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddDays(9).ToUniversalTime();
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            TenantId = setup.TenantId,
            BranchId = setup.BranchId,
            ClientUserId = clientUserId,
            ServiceId = setup.ServiceId,
            ResourceId = setup.ResourceId,
            CreatedByUserId = clientUserId,
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30),
            Status = status,
            ChannelOrigin = "web",
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();
        return reservation.ReservationId;
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
