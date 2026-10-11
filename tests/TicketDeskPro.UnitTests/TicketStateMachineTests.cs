using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Services;

namespace TicketDeskPro.UnitTests;

public class TicketStateMachineTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]                 // BR-014
    [InlineData(TicketStatus.InProgress, TicketStatus.AwaitingCustomer)]     // BR-015
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]             // BR-017
    [InlineData(TicketStatus.AwaitingCustomer, TicketStatus.CustomerReplied)]// BR-016
    [InlineData(TicketStatus.CustomerReplied, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]                 // BR-018
    [InlineData(TicketStatus.Resolved, TicketStatus.Reopened)]               // BR-019
    [InlineData(TicketStatus.Reopened, TicketStatus.InProgress)]
    public void Valid_transitions_are_allowed(TicketStatus from, TicketStatus to) =>
        Assert.True(TicketStateMachine.CanTransition(from, to, isAdminForceClose: false));

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]      // must be worked first
    [InlineData(TicketStatus.Open, TicketStatus.AwaitingCustomer)]
    [InlineData(TicketStatus.AwaitingCustomer, TicketStatus.Resolved)] // waiting on customer
    [InlineData(TicketStatus.CustomerReplied, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]        // closed is terminal
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)] // must reopen first
    public void Invalid_transitions_are_rejected(TicketStatus from, TicketStatus to) =>
        Assert.False(TicketStateMachine.CanTransition(from, to, isAdminForceClose: false));

    [Fact]
    public void Admin_can_force_close_from_any_state_but_not_reopen_closed() // BR-018
    {
        Assert.True(TicketStateMachine.CanTransition(TicketStatus.Open, TicketStatus.Closed, isAdminForceClose: true));
        Assert.True(TicketStateMachine.CanTransition(TicketStatus.AwaitingCustomer, TicketStatus.Closed, isAdminForceClose: true));
        Assert.False(TicketStateMachine.CanTransition(TicketStatus.Closed, TicketStatus.Reopened, isAdminForceClose: true));
    }

    [Fact]
    public void Reopen_window_is_48_hours_from_resolution() // BR-019
    {
        var resolvedAt = DateTime.UtcNow.AddHours(-47);
        Assert.True(TicketStateMachine.CanReopen(TicketStatus.Resolved, resolvedAt, DateTime.UtcNow));

        var tooOld = DateTime.UtcNow.AddHours(-49);
        Assert.False(TicketStateMachine.CanReopen(TicketStatus.Resolved, tooOld, DateTime.UtcNow));

        Assert.False(TicketStateMachine.CanReopen(TicketStatus.Closed, resolvedAt, DateTime.UtcNow));
    }
}
