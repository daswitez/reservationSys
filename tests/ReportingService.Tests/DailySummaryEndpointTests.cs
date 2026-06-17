using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class DailySummaryEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Track inserts for cleanup
    private Guid? _insertedTenantId;
    private (Guid TenantId, Guid BranchId)? _insertedBranchKey;

    // Fixed test date — far future avoids any collision with real data
    private static readonly LocalDate TestCassandraDate = new(2099, 12, 1);
    private const string TestDateStr = "2099-12-01";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Only connect to Cassandra if this test instance actually inserted rows
        if (!_insertedTenantId.HasValue && !_insertedBranchKey.HasValue)
            return;

        var session = factory.Services.GetRequiredService<Cassandra.ISession>();

        if (_insertedTenantId.HasValue)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_daily_summary_by_tenant WHERE tenant_id = ?",
                _insertedTenantId.Value));
        }

        if (_insertedBranchKey.HasValue)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_daily_summary_by_branch WHERE tenant_id = ? AND branch_id = ?",
                _insertedBranchKey.Value.TenantId,
                _insertedBranchKey.Value.BranchId));
        }
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDailySummary_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync($"/reports/daily-summary?date={TestDateStr}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CannotGetDailySummary_ReturnsForbidden()
    {
        using var request = BuildRequest("client", Guid.NewGuid(), tenantId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BranchAdmin_WithOtherBranchId_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var claimBranch = Guid.NewGuid();

        using var request = BuildRequest(
            "branch_admin", Guid.NewGuid(),
            tenantId: tenantId,
            claimBranchId: claimBranch,
            queryExtra: $"&branchId={Guid.NewGuid()}"); // different branch in query

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDailySummary_WithInvalidDate_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), date: "01-12-2099");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuperAdmin_WithoutTenantId_ReturnsBadRequest()
    {
        using var request = BuildRequest("super_admin", Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    // ── PENDING_SYNC (no data in Cassandra) ──────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithNoData_ReturnsPendingSyncStatus()
    {
        var tenantId = Guid.NewGuid(); // never inserted → no Cassandra row

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(), tenantId: tenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("PENDING_SYNC", data.GetProperty("dataStatus").GetString());
        Assert.Equal(0, data.GetProperty("totalCreated").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task GetDailySummary_WithBranchId_WhenNoData_ReturnsPendingSync()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, queryExtra: $"&branchId={branchId}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("PENDING_SYNC", data.GetProperty("dataStatus").GetString());
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
    }

    // ── OK (data present in Cassandra) ───────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithData_ReturnsOkStatusAndCounters()
    {
        var tenantId = Guid.NewGuid();
        _insertedTenantId = tenantId;

        await SeedTenantSummaryAsync(tenantId,
            created: 10, confirmed: 8, cancelled: 1, attended: 5, noShow: 2,
            reservedMinutes: 300);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(), tenantId: tenantId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("OK", data.GetProperty("dataStatus").GetString());
        Assert.Equal(tenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(TestDateStr, data.GetProperty("date").GetString());
        Assert.Equal(10, data.GetProperty("totalCreated").GetInt32());
        Assert.Equal(8, data.GetProperty("totalConfirmed").GetInt32());
        Assert.Equal(1, data.GetProperty("totalCancelled").GetInt32());
        Assert.Equal(5, data.GetProperty("totalAttended").GetInt32());
        Assert.Equal(2, data.GetProperty("totalNoShow").GetInt32());
        Assert.Equal(300, data.GetProperty("totalReservedMinutes").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task GetDailySummary_WithBranchId_WhenDataExists_ReturnsOkStatus()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        _insertedBranchKey = (tenantId, branchId);

        await SeedBranchSummaryAsync(tenantId, branchId, "Sucursal Test",
            created: 5, confirmed: 4, cancelled: 0, attended: 3, noShow: 1,
            reservedMinutes: 150);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, queryExtra: $"&branchId={branchId}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("OK", data.GetProperty("dataStatus").GetString());
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal("Sucursal Test", data.GetProperty("branchName").GetString());
        Assert.Equal(5, data.GetProperty("totalCreated").GetInt32());
        Assert.Equal(3, data.GetProperty("totalAttended").GetInt32());
    }

    [Fact]
    public async Task SuperAdmin_WithTenantId_CanReadAnyTenantData()
    {
        var tenantId = Guid.NewGuid();
        _insertedTenantId = tenantId;

        await SeedTenantSummaryAsync(tenantId,
            created: 7, confirmed: 6, cancelled: 0, attended: 4, noShow: 2,
            reservedMinutes: 200);

        using var request = BuildRequest("super_admin", Guid.NewGuid(),
            queryExtra: $"&tenantId={tenantId}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("OK", data.GetProperty("dataStatus").GetString());
        Assert.Equal(tenantId, data.GetProperty("tenantId").GetGuid());
        Assert.Equal(7, data.GetProperty("totalCreated").GetInt32());
    }

    [Fact]
    public async Task BranchAdmin_WithoutBranchId_DefaultsToClaimBranch()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        _insertedBranchKey = (tenantId, branchId);

        await SeedBranchSummaryAsync(tenantId, branchId, "Mi Sucursal",
            created: 3, confirmed: 3, cancelled: 0, attended: 2, noShow: 1,
            reservedMinutes: 90);

        // No branchId in query — controller should default to claim branchId
        using var request = BuildRequest(
            "branch_admin", Guid.NewGuid(),
            tenantId: tenantId,
            claimBranchId: branchId);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal("OK", data.GetProperty("dataStatus").GetString());
        Assert.Equal(branchId, data.GetProperty("branchId").GetGuid());
        Assert.Equal(3, data.GetProperty("totalCreated").GetInt32());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? claimBranchId = null,
        string date = TestDateStr,
        string queryExtra = "")
    {
        var url = $"/reports/daily-summary?date={date}{queryExtra}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        if (claimBranchId.HasValue)
            request.Headers.Add("X-Test-Branch-Id", claimBranchId.Value.ToString());
        return request;
    }

    private async Task SeedTenantSummaryAsync(
        Guid tenantId,
        int created, int confirmed, int cancelled, int attended, int noShow,
        int reservedMinutes)
    {
        var session = factory.Services.GetRequiredService<ISession>();
        const string cql = """
            INSERT INTO report_daily_summary_by_tenant
              (tenant_id, report_date, total_created, total_confirmed, total_cancelled,
               total_attended, total_no_show, total_blocked_minutes, total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            cql, tenantId, TestCassandraDate,
            created, confirmed, cancelled, attended, noShow,
            0, reservedMinutes,
            DateTimeOffset.UtcNow));
    }

    private async Task SeedBranchSummaryAsync(
        Guid tenantId, Guid branchId, string branchName,
        int created, int confirmed, int cancelled, int attended, int noShow,
        int reservedMinutes)
    {
        var session = factory.Services.GetRequiredService<ISession>();
        const string cql = """
            INSERT INTO report_daily_summary_by_branch
              (tenant_id, branch_id, report_date, branch_name, total_created, total_confirmed,
               total_cancelled, total_attended, total_no_show, total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            cql, tenantId, branchId, TestCassandraDate,
            branchName, created, confirmed, cancelled, attended, noShow,
            reservedMinutes,
            DateTimeOffset.UtcNow));
    }
}
