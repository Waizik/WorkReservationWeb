using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Services;

public interface IReservationPlatformService
{
    Task<ServiceOfferDto?> GetServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken);

    Task<ReservationSlotDto?> GetReservationSlotAsync(string serviceOfferId, string slotId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken);

    Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken);

    Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken);

    Task<bool> DeleteServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken);
}
