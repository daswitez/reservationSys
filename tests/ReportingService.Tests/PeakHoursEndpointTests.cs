using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class PeakHoursEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private readonly List<(Guid TenantId, Guid BranchId, LocalDate Date)> _insertedPartitions = [];

    private static readonly LocalDate TestDate = new(2099, 9, 1);
    private static readonly LocalDate TestDate2 = new(2099, 9, 2);
    private const string TestDateStr = "2099-09-01";
    private const string TestDate2Str = "2099-09-02";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_insertedPartitions.Count == 0) return;

        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        foreach (var (tenantId, branchId, date) in _insertedPartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_peak_hours_by_branch_day WHERE tenant_id = ? AND branch_id = ? AND report_date = ?",
                tenantId, branchId, date));
        }
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPeakHours_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync(
            $"/reports/peak-hours?branchId={Guid.NewGuid()}&date={TestDateStr}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CannotGetPeakHours_ReturnsForbidden()
    {
        using var request = BuildRequest("client", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid(), date: TestDateStr);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_CannotGetPeakHoursForOtherBranch_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var request = BuildRequest("branch_admin", Guid.NewGuid(),
            tenantId: tenantId,
            branchId: Guid.NewGuid(),      // different from claim
            claimBranchId: Guid.NewGuid(),
            date: TestDateStr);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPeakHours_WithoutBranchId_ReturnsBadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/reports/peak-hours?date={TestDateStr}");
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
    public async Task GetPeakHours_WithoutDateParam_ReturnsBadRequest()
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
    public async Task GetPeakHours_WithInvalidDate_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), branchId: Guid.NewGuid(), date: "01-09-2099");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    // ── OK — empty ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithNoData_ReturnsEmptyHoursList()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal(TestDateStr, data.GetProperty("periodFrom").GetString());
        Assert.Equal(TestDateStr, data.GetProperty("periodTo").GetString());
        Assert.Equal(0, data.GetProperty("hours").GetArrayLength());
    }

    // ── OK — data present ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithData_ReturnsHoursSortedByHour()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Seed hours out of order to verify sorting
        await SeedPeakHourAsync(tenantId, branchId, TestDate,
            hourOfDay: 14, created: 3, attended: 2, cancelled: 1);
        await SeedPeakHourAsync(tenantId, branchId, TestDate,
            hourOfDay: 9, created: 5, attended: 4, cancelled: 0);
        await SeedPeakHourAsync(tenantId, branchId, TestDate,
            hourOfDay: 11, created: 7, attended: 6, cancelled: 1);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hours = payload!.RootElement.GetProperty("data").GetProperty("hours");
        Assert.Equal(3, hours.GetArrayLength());

        // Sorted by hourOfDay ascending
        Assert.Equal(9, hours[0].GetProperty("hourOfDay").GetInt32());
        Assert.Equal(5, hours[0].GetProperty("totalCreated").GetInt32());
        Assert.Equal(4, hours[0].GetProperty("totalAttended").GetInt32());
        Assert.Equal(0, hours[0].GetProperty("totalCancelled").GetInt32());

        Assert.Equal(11, hours[1].GetProperty("hourOfDay").GetInt32());
        Assert.Equal(14, hours[2].GetProperty("hourOfDay").GetInt32());
    }

    [Fact]
    public async Task GetPeakHours_WithDateRange_AggregatesByHourAcrossDays()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Hour 10 appears on both days → totals must be summed
        await SeedPeakHourAsync(tenantId, branchId, TestDate,
            hourOfDay: 10, created: 4, attended: 3, cancelled: 1);
        await SeedPeakHourAsync(tenantId, branchId, TestDate2,
            hourOfDay: 10, created: 6, attended: 5, cancelled: 1);
        // Hour 15 only on day 2
        await SeedPeakHourAsync(tenantId, branchId, TestDate2,
            hourOfDay: 15, created: 2, attended: 2, cancelled: 0);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId,
            queryExtra: $"&dateFrom={TestDateStr}&dateTo={TestDate2Str}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(TestDateStr, data.GetProperty("periodFrom").GetString());
        Assert.Equal(TestDate2Str, data.GetProperty("periodTo").GetString());

        var hours = data.GetProperty("hours");
        Assert.Equal(2, hours.GetArrayLength());

        var h10 = hours.EnumerateArray().Single(h => h.GetProperty("hourOfDay").GetInt32() == 10);
        Assert.Equal(10, h10.GetProperty("totalCreated").GetInt32());   // 4 + 6
        Assert.Equal(8, h10.GetProperty("totalAttended").GetInt32());   // 3 + 5
        Assert.Equal(2, h10.GetProperty("totalCancelled").GetInt32()); // 1 + 1

        var h15 = hours.EnumerateArray().Single(h => h.GetProperty("hourOfDay").GetInt32() == 15);
        Assert.Equal(2, h15.GetProperty("totalCreated").GetInt32());
    }

    [Fact]
    public async Task BranchAdmin_CanGetPeakHoursForOwnBranch()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await SeedPeakHourAsync(tenantId, branchId, TestDate,
            hourOfDay: 8, created: 2, attended: 2, cancelled: 0);

        using var request = BuildRequest("branch_admin", Guid.NewGuid(),
            tenantId: tenantId, branchId: branchId,
            claimBranchId: branchId, date: TestDateStr);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, payload!.RootElement.GetProperty("data").GetProperty("hours").GetArrayLength());
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
        var url = $"/reports/peak-hours?placeholder=1{branchParam}{dateParam}{queryExtra}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (claimBranchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", claimBranchId.Value.ToString());
        return request;
    }

    private async Task SeedPeakHourAsync(
        Guid tenantId, Guid branchId, LocalDate date,
        int hourOfDay, int created, int attended, int cancelled)
    {
        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        const string cql = """
            INSERT INTO report_peak_hours_by_branch_day
              (tenant_id, branch_id, report_date, hour_of_day,
               total_created, total_attended, total_cancelled, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            cql, tenantId, branchId, date, hourOfDay,
            created, attended, cancelled,
            DateTimeOffset.UtcNow));

        var key = (tenantId, branchId, date);
        if (!_insertedPartitions.Contains(key))
            _insertedPartitions.Add(key);
    }
}
