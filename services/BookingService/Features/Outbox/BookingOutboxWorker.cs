using System.Net.Mime;
using System.Text;
using BookingService.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Features.Outbox;

public sealed class BookingOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<BookingOutboxWorker> logger) : BackgroundService
{
    private readonly int _batchSize = Math.Max(1, configuration.GetValue("Outbox:BatchSize", 20));
    private readonly int _maxAttempts = Math.Max(1, configuration.GetValue("Outbox:MaxAttempts", 5));
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(
        Math.Max(1, configuration.GetValue("Outbox:PollingIntervalSeconds", 5)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error inesperado procesando outbox de Booking.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var reportingClient = httpClientFactory.CreateClient("ReportingEvents");

        var events = await dbContext.ReservationEventOutbox
            .Where(entity => entity.Status == "PENDING")
            .OrderBy(entity => entity.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(cancellationToken);

        foreach (var outboxEvent in events)
        {
            outboxEvent.Status = "PROCESSING";
            outboxEvent.LastError = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                using var content = new StringContent(
                    outboxEvent.Payload,
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json);
                using var response = await reportingClient.PostAsync(
                    "/internal/report-events",
                    content,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    outboxEvent.Status = "PROCESSED";
                    outboxEvent.ProcessedAt = DateTimeOffset.UtcNow;
                    outboxEvent.LastError = null;
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    MarkFailedOrPending(
                        outboxEvent,
                        $"Reporting respondio {(int)response.StatusCode}: {responseBody}");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                MarkFailedOrPending(outboxEvent, exception.Message);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void MarkFailedOrPending(Domain.ReservationEventOutbox outboxEvent, string error)
    {
        outboxEvent.Attempts += 1;
        outboxEvent.LastError = error.Length > 2000 ? error[..2000] : error;
        outboxEvent.Status = outboxEvent.Attempts >= _maxAttempts ? "FAILED" : "PENDING";
    }
}
