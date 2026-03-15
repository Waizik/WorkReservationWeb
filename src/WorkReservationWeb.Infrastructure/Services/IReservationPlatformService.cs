using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Services;

public interface IReservationPlatformService
{
    Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken);

    Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken);

    Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken);
}
