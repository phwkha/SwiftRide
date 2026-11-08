using TripService.Domain.Enums;
using TripService.Domain.States;
using Xunit;

namespace TripService.Tests;

public class TripStateTests
{
    [Fact]
    public void RequestedState_Accept_ReturnsDriverAcceptedState()
    {
        var state = new RequestedState().Accept();

        Assert.IsType<DriverAcceptedState>(state);
        Assert.Equal(TripStatus.DriverAccepted, state.Status);
    }

    [Fact]
    public void RequestedState_PickUp_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new RequestedState().PickUp());
    }

    [Fact]
    public void PickedUpState_Cancel_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new PickedUpState().Cancel());
    }

    [Fact]
    public void PaidState_Complete_ReturnsCompletedState()
    {
        var state = new PaidState().Complete();

        Assert.IsType<CompletedState>(state);
        Assert.Equal(TripStatus.Completed, state.Status);
    }
}
