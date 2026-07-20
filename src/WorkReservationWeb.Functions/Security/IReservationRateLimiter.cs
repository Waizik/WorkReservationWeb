namespace WorkReservationWeb.Functions.Security;

public interface IReservationRateLimiter
{
    bool TryAcquire(string clientKey);
}
