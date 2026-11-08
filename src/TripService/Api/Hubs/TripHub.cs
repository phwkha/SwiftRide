using Microsoft.AspNetCore.SignalR;

namespace TripService.Api.Hubs;

public class TripHub : Hub
{
    public async Task JoinDriverRoom(string driverId)
    {
        if (!string.IsNullOrWhiteSpace(driverId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"driver_{driverId.Trim().ToLowerInvariant()}");
        }
    }

    public async Task LeaveDriverRoom(string driverId)
    {
        if (!string.IsNullOrWhiteSpace(driverId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"driver_{driverId.Trim().ToLowerInvariant()}");
        }
    }

    public async Task JoinTripRoom(string tripId)
    {
        if (!string.IsNullOrWhiteSpace(tripId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{tripId.Trim().ToLowerInvariant()}");
        }
    }

    public async Task LeaveTripRoom(string tripId)
    {
        if (!string.IsNullOrWhiteSpace(tripId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{tripId.Trim().ToLowerInvariant()}");
        }
    }
}
