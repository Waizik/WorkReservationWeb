using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Public;

public sealed class CreateReservationFunction(IReservationPlatformService reservationPlatformService)
{
    [Function("CreateReservation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "public/reservations")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<CreateReservationRequestDto>(cancellationToken);
        if (payload is null)
        {
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Request payload is required."), cancellationToken);
            return badRequest;
        }

        var result = await reservationPlatformService.CreateReservationAsync(payload, cancellationToken);

        var statusCode = result.Outcome switch
        {
            ReservationCreateOutcome.Created => System.Net.HttpStatusCode.Created,
            ReservationCreateOutcome.ValidationFailed => System.Net.HttpStatusCode.BadRequest,
            ReservationCreateOutcome.Conflict => System.Net.HttpStatusCode.Conflict,
            _ => System.Net.HttpStatusCode.BadRequest
        };

        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
