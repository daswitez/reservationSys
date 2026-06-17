using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cassandra;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class TopServicesEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Track inserts for cleanup: list of (tenantId, yearMonth) partitions
    private readonly List<(Guid TenantId, string YearMonth)> _insertedPartitions = [];

    private const string TestMonth = "2099-10";
    private const string TestMonth2 = "2099-11";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_insertedPartitions.Count == 0) return;

        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        foreach (var (tenantId, yearMonth) in _insertedPartitions)
        {
            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM report_service_summary_by_month WHERE tenant_id = ? AND year_month = ?",
                tenantId, yearMonth));
        }
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopServices_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync(
            $"/reports/services/top?month={TestMonth}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Client_CannotGetTopServices_ReturnsForbidden()
    {
        using var request = BuildRequest("client", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), month: TestMonth);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopServices_WithoutMonthParam_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetTopServices_WithInvalidMonth_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(), month: "10-2099");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetTopServices_WhenMonthToBeforeMonthFrom_ReturnsBadRequest()
    {
        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: Guid.NewGuid(),
            queryExtra: "&monthFrom=2099-11&monthTo=2099-10");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuperAdmin_WithoutTenantId_ReturnsBadRequest()
    {
        using var request = BuildRequest("super_admin", Guid.NewGuid(), month: TestMonth);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR",
            payload!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    // ── OK — empty ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithNoData_ReturnsEmptyRanking()
    {
        var tenantId = Guid.NewGuid();

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, month: TestMonth);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(TestMonth, data.GetProperty("periodFrom").GetString());
        Assert.Equal(TestMonth, data.GetProperty("periodTo").GetString());
        Assert.Equal(0, data.GetProperty("services").GetArrayLength());
    }

    // ── OK — data present ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_WithData_ReturnsRankedByTotalCreated()
    {
        var tenantId = Guid.NewGuid();
        var svc1 = Guid.NewGuid();
        var svc2 = Guid.NewGuid();

        // svc2 has more reservations → should rank #1
        await SeedServiceMonthAsync(tenantId, TestMonth, svc1, "Corte Simple", created: 10, cancelled: 1, attended: 8, noShow: 1, minutes: 300);
        await SeedServiceMonthAsync(tenantId, TestMonth, svc2, "Tinte Completo", created: 25, cancelled: 3, attended: 20, noShow: 2, minutes: 750);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId, month: TestMonth);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var services = payload!.RootElement.GetProperty("data").GetProperty("services");
        Assert.Equal(2, services.GetArrayLength());

        var first = services[0];
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal(svc2, first.GetProperty("serviceId").GetGuid());
        Assert.Equal(25, first.GetProperty("totalCreated").GetInt32());
        Assert.Equal(3, first.GetProperty("totalCancelled").GetInt32());

        var second = services[1];
        Assert.Equal(2, second.GetProperty("rank").GetInt32());
        Assert.Equal(svc1, second.GetProperty("serviceId").GetGuid());
    }

    [Fact]
    public async Task GetTopServices_WithMonthRange_AggregatesAcrossMonths()
    {
        var tenantId = Guid.NewGuid();
        var svcId = Guid.NewGuid();

        // Same service in two different months → totals should be summed
        await SeedServiceMonthAsync(tenantId, TestMonth, svcId, "Masaje", created: 10, cancelled: 1, attended: 8, noShow: 1, minutes: 300);
        await SeedServiceMonthAsync(tenantId, TestMonth2, svcId, "Masaje", created: 15, cancelled: 2, attended: 12, noShow: 1, minutes: 450);

        using var request = BuildRequest("tenant_admin", Guid.NewGuid(),
            tenantId: tenantId,
            queryExtra: $"&monthFrom={TestMonth}&monthTo={TestMonth2}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload!.RootElement.GetProperty("data");
        Assert.Equal(TestMonth, data.GetProperty("periodFrom").GetString());
        Assert.Equal(TestMonth2, data.GetProperty("periodTo").GetString());

        var services = data.GetProperty("services");
        Assert.Equal(1, services.GetArrayLength());
        var item = services[0];
        Assert.Equal(1, item.GetProperty("rank").GetInt32());
        Assert.Equal(25, item.GetProperty("totalCreated").GetInt32());   // 10 + 15
        Assert.Equal(3, item.GetProperty("totalCancelled").GetInt32());  // 1 + 2
        Assert.Equal(750, item.GetProperty("totalReservedMinutes").GetInt32()); // 300 + 450
    }

    [Fact]
    public async Task BranchAdmin_CanGetTopServicesForOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        var svcId = Guid.NewGuid();

        await SeedServiceMonthAsync(tenantId, TestMonth, svcId, "Pedicure", created: 5, cancelled: 0, attended: 5, noShow: 0, minutes: 150);

        using var request = BuildRequest("branch_admin", Guid.NewGuid(),
            tenantId: tenantId, month: TestMonth);
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, payload!.RootElement.GetProperty("data").GetProperty("services").GetArrayLength());
    }

    [Fact]
    public async Task SuperAdmin_WithTenantId_CanReadAnyTenantData()
    {
        var tenantId = Guid.NewGuid();
        var svcId = Guid.NewGuid();

        await SeedServiceMonthAsync(tenantId, TestMonth, svcId, "Facial", created: 8, cancelled: 1, attended: 6, noShow: 1, minutes: 240);

        // super_admin passes tenantId as query param, not as claim header
        using var request = BuildRequest("super_admin", Guid.NewGuid(),
            month: TestMonth, queryExtra: $"&tenantId={tenantId}");
        using var response = await _client.SendAsync(request);
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, payload!.RootElement.GetProperty("data").GetProperty("services").GetArrayLength());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(
        string role,
        Guid userId,
        Guid? tenantId = null,
        string? month = null,
        string queryExtra = "")
    {
        var monthParam = month is not null ? $"&month={month}" : string.Empty;
        var url = $"/reports/services/top?placeholder=1{monthParam}{queryExtra}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        if (tenantId.HasValue)
            request.Headers.Add("X-Test-Tenant-Id", tenantId.Value.ToString());
        return request;
    }

    private async Task SeedServiceMonthAsync(
        Guid tenantId, string yearMonth, Guid serviceId, string serviceName,
        int created, int cancelled, int attended, int noShow, int minutes)
    {
        var session = factory.Services.GetRequiredService<Cassandra.ISession>();
        const string cql = """
            INSERT INTO report_service_summary_by_month
              (tenant_id, year_month, service_id, service_name,
               total_created, total_cancelled, total_attended, total_no_show,
               total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            cql, tenantId, yearMonth, serviceId, serviceName,
            created, cancelled, attended, noShow, minutes,
            DateTimeOffset.UtcNow));

        if (!_insertedPartitions.Contains((tenantId, yearMonth)))
            _insertedPartitions.Add((tenantId, yearMonth));
    }
}
