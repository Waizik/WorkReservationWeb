using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class VirtualSlotBookingTests
{
    private const string ServiceOfferId = "srv_consultation";

    [Fact]
    public async Task GetAvailableSlots_ReturnsVirtualSlotsFromSchedule()
    {
        var service = new InMemoryReservationPlatformService();

        var slots = await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None);

        Assert.NotEmpty(slots);
        Assert.All(slots, slot =>
        {
            Assert.Equal(ServiceOfferId, slot.ServiceOfferId);
            Assert.Equal(string.Empty, slot.Etag);
            Assert.Equal(0, slot.ReservedCount);
            Assert.Equal("Available", slot.Status);
            Assert.True(slot.StartUtc > DateTimeOffset.UtcNow);
        });
    }

    [Fact]
    public async Task CreateReservation_ForVirtualSlot_MaterializesSlotAndCreatesReservation()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();

        var result = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "Virtual User", "virtual@example.com", null),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ReservationCreateOutcome.Created, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.UpdatedSlotEtag));

        var materialized = await service.GetReservationSlotAsync(ServiceOfferId, slot.Id, CancellationToken.None);
        Assert.NotNull(materialized);
        Assert.Equal(1, materialized.ReservedCount);
        Assert.Equal(result.UpdatedSlotEtag, materialized.Etag);

        var reservation = Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
        Assert.Equal(slot.Id, reservation.SlotId);
    }

    [Fact]
    public async Task CreateReservation_ForSlotOutsideSchedule_ReturnsValidationFailed()
    {
        var service = new InMemoryReservationPlatformService();

        var result = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, "slot_209901010800", string.Empty, "User", "user@example.com", null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ReservationCreateOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task CreateReservation_OnClosedOverrideDate_ReturnsValidationFailed()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var schedule = await service.GetSlotScheduleAsync(ServiceOfferId, CancellationToken.None);
        Assert.NotNull(schedule);

        var closedDate = slot.StartUtc.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd");
        await service.UpsertSlotScheduleAsync(
            schedule with
            {
                Overrides = new Dictionary<string, SlotScheduleOverrideDto>
                {
                    [closedDate] = new(true, null)
                }
            },
            CancellationToken.None);

        var result = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, string.Empty, "User", "user@example.com", null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ReservationCreateOutcome.ValidationFailed, result.Outcome);

        var remainingSlots = await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None);
        Assert.DoesNotContain(remainingSlots, candidate => candidate.Id == slot.Id);
    }

    [Fact]
    public async Task CreateReservation_ForVirtualSlot_WithStaleEtag_ReturnsConflict()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();

        var result = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, "stale-etag", "User", "user@example.com", null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ReservationCreateOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task ScheduleChange_HidesMaterializedSlotThatNoLongerMatches_ButKeepsReservation()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();

        var booking = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", "user@example.com", null),
            CancellationToken.None);
        Assert.True(booking.Success);

        var schedule = await service.GetSlotScheduleAsync(ServiceOfferId, CancellationToken.None);
        Assert.NotNull(schedule);

        // Shift the whole schedule to different times: the booked slot stops being offered.
        await service.UpsertSlotScheduleAsync(
            schedule with { Times = ["17:00"] },
            CancellationToken.None);

        var slots = await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None);
        Assert.DoesNotContain(slots, candidate => candidate.Id == slot.Id);

        var reservation = Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
        Assert.Equal(slot.Id, reservation.SlotId);
    }
}
