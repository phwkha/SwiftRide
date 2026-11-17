using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SwiftRide.Security;
using TripService.Domain.Interfaces;

namespace TripService.Api.Hubs;

[Authorize]
public class TripHub(ITripReader trips) : Hub
{
    public async Task JoinDriverRoom(string driverId)
    {
        var id = RequireOwnDriverRoom(driverId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"driver_{id:D}");
    }

    public async Task LeaveDriverRoom(string driverId)
    {
        var id = RequireOwnDriverRoom(driverId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"driver_{id:D}");
    }

    public async Task JoinTripRoom(string tripId)
    {
        var id = ParseRoomId(tripId);
        var userId = Context.User.AuthenticatedUserId();
        if (userId is null)
            throw new HubException("UNAUTHORIZED");
        var trip = await trips.GetByIdAsync(id, Context.ConnectionAborted);
        if (trip is null ||
            !((Context.User!.IsInRole("Rider") && trip.RiderId == userId) ||
              (Context.User.IsInRole("Driver") && trip.DriverId == userId)))
            throw new HubException("FORBIDDEN_TRIP_ACCESS");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{id:D}");
    }

    public async Task LeaveTripRoom(string tripId)
    {
        // Leaving removes only this connection and must remain possible after reassignment.
        var id = ParseRoomId(tripId);
        if (Context.User.AuthenticatedUserId() is null)
            throw new HubException("UNAUTHORIZED");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{id:D}");
    }

    private Guid RequireOwnDriverRoom(string value)
    {
        var id = ParseRoomId(value);
        var callerId = Context.User.AuthenticatedUserId();
        if (callerId is null)
            throw new HubException("UNAUTHORIZED");
        if (!Context.User!.IsInRole("Driver") || callerId != id)
            throw new HubException("FORBIDDEN_DRIVER_ACCESS");
        return id;
    }

    private static Guid ParseRoomId(string value)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id : throw new HubException("INVALID_ROOM_ID");
}
