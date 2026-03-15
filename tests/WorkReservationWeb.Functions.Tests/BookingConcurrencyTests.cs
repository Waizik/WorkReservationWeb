using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public class BookingConcurrencyTests
{
    [Fact]
    public async Task CreateReservation_WithSameEtag_AllowsOnlyOneSuccessWhenCapacityIsOne()
    {
        var service = new InMemoryReservationPlatformService();

        // For this initial test phase, we reuse seeded slots and force two requests to the same slot.
        var seededServiceId = "srv_consultation";
        var seededSlots = await service.GetAvailableSlotsAsync(seededServiceId, CancellationToken.None);
        var targetSlot = seededSlots.First();

        var req1 = new CreateReservationRequestDto(
            seededServiceId,
            targetSlot.Id,
            targetSlot.Etag,
            "User One",
            "one@example.com",
            null);

        var req2 = req1 with { CustomerName = "User Two", CustomerEmail = "two@example.com" };

        var results = await Task.WhenAll(
            service.CreateReservationAsync(req1, CancellationToken.None),
            service.CreateReservationAsync(req2, CancellationToken.None));

        Assert.Equal(1, results.Count(x => x.Success));
        Assert.Equal(1, results.Count(x => x.Outcome == ReservationCreateOutcome.Conflict));
    }

    [Fact]
    public async Task CreateReservation_WhenCapacityIsTwo_ThirdAttemptReturnsConflict()
    {
        var service = new InMemoryReservationPlatformService();
        var seededServiceId = "srv_consultation";

        var initialSlot = (await service.GetAvailableSlotsAsync(seededServiceId, CancellationToken.None)).First();

        var first = await service.CreateReservationAsync(
            new CreateReservationRequestDto(
                seededServiceId,
                initialSlot.Id,
                initialSlot.Etag,
                "First",
                "first@example.com",
                null),
            CancellationToken.None);

        Assert.True(first.Success);

        var slotAfterFirst = (await service.GetAvailableSlotsAsync(seededServiceId, CancellationToken.None))
            .First(x => x.Id == initialSlot.Id);

        var second = await service.CreateReservationAsync(
            new CreateReservationRequestDto(
                seededServiceId,
                slotAfterFirst.Id,
                slotAfterFirst.Etag,
                "Second",
                "second@example.com",
                null),
            CancellationToken.None);

        Assert.True(second.Success);

        var third = await service.CreateReservationAsync(
            new CreateReservationRequestDto(
                seededServiceId,
                slotAfterFirst.Id,
                second.UpdatedSlotEtag!,
                "Third",
                "third@example.com",
                null),
            CancellationToken.None);

        Assert.False(third.Success);
        Assert.Equal(ReservationCreateOutcome.Conflict, third.Outcome);
    }
}
