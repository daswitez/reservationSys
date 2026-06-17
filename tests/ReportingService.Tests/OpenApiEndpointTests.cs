using System.Net.Http.Json;
using System.Text.Json;

namespace ReportingService.Tests;

public sealed class OpenApiEndpointTests(ReportingApiFactory factory)
    : IClassFixture<ReportingApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task OpenApi_ContainsReportingEndpoints()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        response.EnsureSuccessStatusCode();
        var paths = payload!.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/reports/daily-summary", out var dailySummary));
        Assert.True(dailySummary.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/reports/resources/occupancy", out var occupancy));
        Assert.True(occupancy.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/reports/services/top", out var topServices));
        Assert.True(topServices.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/reports/peak-hours", out var peakHours));
        Assert.True(peakHours.TryGetProperty("get", out _));

        Assert.True(paths.TryGetProperty("/internal/report-events", out var reportEvents));
        Assert.True(reportEvents.TryGetProperty("post", out _));
    }
}
