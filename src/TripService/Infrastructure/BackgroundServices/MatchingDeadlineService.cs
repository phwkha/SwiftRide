using Microsoft.EntityFrameworkCore;
using TripService.Application.Services;
using TripService.Domain.Enums;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.BackgroundServices;

public sealed class MatchingDeadlineService(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<MatchingDeadlineService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueTripsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not scan matching deadlines.");
            }
        }
    }

    private async Task ProcessDueTripsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var now = clock.GetUtcNow();
        var dueTrips = await db.Trips
            .Where(trip => trip.Status == TripStatus.Requested && trip.FareConfirmedAt != null &&
                (trip.OfferExpiresAt <= now || trip.MatchingDeadline <= now))
            .OrderBy(trip => trip.OfferExpiresAt ?? trip.MatchingDeadline)
            .Select(trip => trip.Id)
            .Take(100)
            .ToListAsync(ct);

        foreach (var tripId in dueTrips)
        {
            await using var tripScope = scopeFactory.CreateAsyncScope();
            var trips = tripScope.ServiceProvider.GetRequiredService<TripAppService>();
            try
            {
                await trips.ProcessMatchingDeadlineAsync(tripId, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A driver action, cancellation, or another worker updated the trip.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not handle matching deadline for trip {TripId}.", tripId);
            }
        }
    }
}
