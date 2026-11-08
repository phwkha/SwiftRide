using TripService.Domain.Entities;

namespace TripService.Domain.Interfaces;

public interface ITripReader
{
    Task<Trip?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default);

    Task<Trip?> GetActiveTripByUserAsync(
        Guid userId,
        CancellationToken ct = default);
}