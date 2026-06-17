using System.Net;
using System.Net.Http.Json;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class InternalReportEventsEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<Guid> _eventIds = [];
    private readonly List<Guid> _tenantIds = [];
    private readonly List<(Guid TenantId, Guid BranchId)> _branchPartitions = [];
    private readonly List<(Guid TenantId, string YearMonth)> _servicePartitions = [];
    private readonly List<(Guid TenantId, Guid BranchId, LocalDate ReportDate)> _resourcePartitions = [];

    public async Task InitializeAsync()
    {
        await CleanupAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupAsync();
    }

    [Fact]
    public async Task ReportEvent_ReservationCreated_UpdatesAggregatesAndIsIdempotent()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var reportDate = new LocalDate(2098, 2, 3);
        var yearMonth = "2098-02";

        Track(eventId, tenantId, branchId, yearMonth, reportDate);

        var payload = new
        {
            eventId,
            eventType = "ReservationCreated",
            occurredAt = DateTimeOffset.Parse("2098-02-03T09:01:00Z"),
            tenantId,
            branchId,
            serviceId,
            resourceId,
            reservationId = Guid.NewGuid(),
            startAt = DateTimeOffset.Parse("2098-02-03T09:00:00Z"),
            endAt = DateTimeOffset.Parse("2098-02-03T09:30:00Z"),
            status = "CONFIRMED",
            durationMinutes = 30,
            serviceName = "Corte",
            branchName = "Sucursal Centro",
            resourceName = "Barbero 1"
        };

        using var firstResponse = await _client.PostAsJsonAsync("/internal/report-events", payload);
        using var secondResponse = await _client.PostAsJsonAsync("/internal/report-events", payload);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var session = factory.Services.GetRequiredService<ISession>();

        var tenantRow = await SingleRowAsync(session, """
            SELECT total_created, total_confirmed, total_reserved_minutes
            FROM report_daily_summary_by_tenant
            WHERE tenant_id = ? AND report_date = ?
            """, tenantId, reportDate);
        Assert.Equal(1, tenantRow.GetValue<int>("total_created"));
        Assert.Equal(1, tenantRow.GetValue<int>("total_confirmed"));
        Assert.Equal(30, tenantRow.GetValue<int>("total_reserved_minutes"));

        var branchRow = await SingleRowAsync(session, """
            SELECT branch_name, total_created, total_confirmed, total_reserved_minutes
            FROM report_daily_summary_by_branch
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ?
            """, tenantId, branchId, reportDate);
        Assert.Equal("Sucursal Centro", branchRow.GetValue<string>("branch_name"));
        Assert.Equal(1, branchRow.GetValue<int>("total_created"));
        Assert.Equal(1, branchRow.GetValue<int>("total_confirmed"));
        Assert.Equal(30, branchRow.GetValue<int>("total_reserved_minutes"));

        var serviceRow = await SingleRowAsync(session, """
            SELECT service_name, total_created, total_reserved_minutes
            FROM report_service_summary_by_month
            WHERE tenant_id = ? AND year_month = ? AND service_id = ?
            """, tenantId, yearMonth, serviceId);
        Assert.Equal("Corte", serviceRow.GetValue<string>("service_name"));
        Assert.Equal(1, serviceRow.GetValue<int>("total_created"));
        Assert.Equal(30, serviceRow.GetValue<int>("total_reserved_minutes"));

        var resourceRow = await SingleRowAsync(session, """
            SELECT resource_name, total_reservations, reserved_minutes
            FROM report_resource_occupancy_by_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ? AND resource_id = ?
            """, tenantId, branchId, reportDate, resourceId);
        Assert.Equal("Barbero 1", resourceRow.GetValue<string>("resource_name"));
        Assert.Equal(1, resourceRow.GetValue<int>("total_reservations"));
        Assert.Equal(30, resourceRow.GetValue<int>("reserved_minutes"));

        var peakRow = await SingleRowAsync(session, """
            SELECT branch_name, total_created
            FROM report_peak_hours_by_branch_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ? AND hour_of_day = ?
            """, tenantId, branchId, reportDate, 9);
        Assert.Equal("Sucursal Centro", peakRow.GetValue<string>("branch_name"));
        Assert.Equal(1, peakRow.GetValue<int>("total_created"));

        var processedRow = await SingleRowAsync(session, """
            SELECT event_type
            FROM report_processed_events
            WHERE event_id = ?
            """, eventId);
        Assert.Equal("ReservationCreated", processedRow.GetValue<string>("event_type"));
    }

    private void Track(
        Guid eventId,
        Guid tenantId,
        Guid branchId,
        string yearMonth,
        LocalDate reportDate)
    {
        _eventIds.Add(eventId);
        _tenantIds.Add(tenantId);
        _branchPartitions.Add((tenantId, branchId));
        _servicePartitions.Add((tenantId, yearMonth));
        _resourcePartitions.Add((tenantId, branchId, reportDate));
    }

    private async Task CleanupAsync()
    {
        var session = factory.Services.GetRequiredService<ISession>();

        foreach (var eventId in _eventIds)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_processed_events WHERE event_id = ?",
                eventId));
        }

        foreach (var tenantId in _tenantIds)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_daily_summary_by_tenant WHERE tenant_id = ?",
                tenantId));
        }

        foreach (var (tenantId, branchId) in _branchPartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_daily_summary_by_branch WHERE tenant_id = ? AND branch_id = ?",
                tenantId,
                branchId));
        }

        foreach (var (tenantId, yearMonth) in _servicePartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_service_summary_by_month WHERE tenant_id = ? AND year_month = ?",
                tenantId,
                yearMonth));
        }

        foreach (var (tenantId, branchId, reportDate) in _resourcePartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_resource_occupancy_by_day WHERE tenant_id = ? AND branch_id = ? AND report_date = ?",
                tenantId,
                branchId,
                reportDate));
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_peak_hours_by_branch_day WHERE tenant_id = ? AND branch_id = ? AND report_date = ?",
                tenantId,
                branchId,
                reportDate));
        }
    }

    private static async Task<Row> SingleRowAsync(ISession session, string cql, params object[] values)
    {
        var row = (await session.ExecuteAsync(new SimpleStatement(cql, values))).FirstOrDefault();
        Assert.NotNull(row);
        return row;
    }
}
