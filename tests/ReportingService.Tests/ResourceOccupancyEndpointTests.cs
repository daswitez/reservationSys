using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class ResourceOccupancyEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Track inserts for cleanup: list of (tenantId, branchId, date) partitions
    private readonly List<(Guid TenantId, Guid BranchId, LocalDate Date)> _insertedPartitions = [];

    private static readonly LocalDate TestDate = new(2099, 11, 1);
    private static readonly LocalDate TestDate2 = new(2099, 11, 2);
    private const string TestDateStr = "2099-11-01";
    private const string TestDate2Str = "2099-11-02";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_insertedPartitions.Count == 0) return;

        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        foreach (var (tenantId, branchId, date) in _insertedPartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_resource_occupancy_by_day WHERE tenant_id = ? AND branch_id = ? AND report_date = ?",
                tenantId, branchId, date));
        }
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOccupancy_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync(
            $"/reports/resources/occupancy?branchId={Guid.NewGuid()}&date={TestDateStr}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CannotGetOccupancy_ReturnsForbidden()
    {
        using var request = BuildRequest("client", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotGetOccupancyForOtherBranch_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var claimBranch = Guid.NewGuid();

        using var request = BuildRequest("branch_admin", Guid.NewGuid(),
            tenantId: tenantId,
            branchId: Guid.NewGuid(),        // different from claim
            claimBranchId: claimBranch);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOccupancy_WithoutBranchId_ReturnsBadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/reports/resources/occupancy?date={TestDateStr}");
        request.Headers.Add("X-Test-Role", "tenant_admin");
        request.Headers.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Tenant-Id", Guid.NewGuid().ToString());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOccupancy_WithoutDateParams_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOccupancy_WithInvalidDate_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid(), date: "01-11-2099");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetOccupancy_WhenDateToBeforeDateFrom_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid(),
            queryExtra: "&dateFrom=2099-11-05&dateTo=2099-11-01");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    // ── OK — empty result ─────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithNoData_ReturnsEmptyList()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, payload!.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(0, payload.RootElement.GetProperty("data").GetArrayLength());
    }

    // ── OK — data present ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithData_ReturnsOccupancyList()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        await SeedOccupancyAsync(tenantId, branchId, resourceId, TestDate,
            "Silla 1", "sala", reservations: 5, attended: 3, cancelled: 1, noShow: 1,
            reservedMinutes: 150, blockedMinutes: 30);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());

        var item = data[0];
        Assert.Equal(resourceId, item.GetProperty("resourceId").GetGuid());
        Assert.Equal("Silla 1", item.GetProperty("resourceName").GetString());
        Assert.Equal("sala", item.GetProperty("resourceType").GetString());
        Assert.Equal(TestDateStr, item.GetProperty("date").GetString());
        Assert.Equal(5, item.GetProperty("totalReservations").GetInt32());
        Assert.Equal(3, item.GetProperty("totalAttended").GetInt32());
        Assert.Equal(1, item.GetProperty("totalCancelled").GetInt32());
        Assert.Equal(1, item.GetProperty("totalNoShow").GetInt32());
        Assert.Equal(150, item.GetProperty("reservedMinutes").GetInt32());
        Assert.Equal(30, item.GetProperty("blockedMinutes").GetInt32());

        // Verify no personal client data is exposed
        Assert.False(item.TryGetProperty("clientUserId", out _));
        Assert.False(item.TryGetProperty("clientName", out _));
        Assert.False(item.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task GetOccupancy_WithDateRange_ReturnsMultipleDays()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        await SeedOccupancyAsync(tenantId, branchId, resourceId, TestDate,
            "Silla 2", "sala", reservations: 3, attended: 2, cancelled: 0, noShow: 1,
            reservedMinutes: 90, blockedMinutes: 0);

        await SeedOccupancyAsync(tenantId, branchId, resourceId, TestDate2,
            "Silla 2", "sala", reservations: 4, attended: 3, cancelled: 1, noShow: 0,
            reservedMinutes: 120, blockedMinutes: 0);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId,
            queryExtra: $"&dateFrom={TestDateStr}&dateTo={TestDate2Str}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());

        var dates = data.EnumerateArray()
            .Select(i => i.GetProperty("date").GetString())
            .ToHashSet();
        Assert.Contains(TestDateStr, dates);
        Assert.Contains(TestDate2Str, dates);
    }

    [Fact]
    public async Task BranchAdmin_CanGetOccupancyForOwnBranch()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        await SeedOccupancyAsync(tenantId, branchId, resourceId, TestDate,
            "Recurso BA", "profesional", reservations: 2, attended: 2, cancelled: 0, noShow: 0,
            reservedMinutes: 60, blockedMinutes: 0);

        using var request = BuildRequest("branch_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId,
            claimBranchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, payload!.RootElement.GetProperty("data").GetArrayLength());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null,
        Guid? claimBranchId = null,
        string? date = null,
        string queryExtra = "")
    {
        var dateParam = date is not null ? $"&date={date}" : string.Empty;
        var branchParam = branchId.HasValue ? $"&branchId={branchId.Value}" : string.Empty;
        var url = $"/reports/resources/occupancy?placeholder=1{branchParam}{dateParam}{queryExtra}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (claimBranchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", claimBranchId.Value.ToString());
        return request;
    }

    private async Task SeedOccupancyAsync(
        Guid tenantId, Guid branchId, Guid resourceId, LocalDate date,
        string resourceName, string resourceType,
        int reservations, int attended, int cancelled, int noShow,
        int reservedMinutes, int blockedMinutes)
    {
        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        const string cql = """
            INSERT INTO report_resource_occupancy_by_day
              (tenant_id, branch_id, resource_id, report_date,
               resource_name, resource_type,
               total_reservations, total_attended, total_cancelled, total_no_show,
               reserved_minutes, blocked_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            cql, tenantId, branchId, resourceId, date,
            resourceName, resourceType,
            reservations, attended, cancelled, noShow,
            reservedMinutes, blockedMinutes,
            DateTimeOffset.UtcNow));

        _insertedPartitions.Add((tenantId, branchId, date));
    }
}
