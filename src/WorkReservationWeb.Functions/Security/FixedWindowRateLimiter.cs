using System.Threading.RateLimiting;

namespace WorkReservationWeb.Functions.Security;

// Thin adapter over the BCL System.Threading.RateLimiting partitioned limiter (one fixed window
// per client key). Counters live per Functions instance and reset on restart or scale-out, which
// is an accepted trade-off: it avoids paid gateway services and is adequate for a single-instance
// consumption plan.
public sealed class FixedWindowRateLimiter(int limit, TimeSpan window) : IReservationRateLimiter, IAsyncDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(
        clientKey => RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        }),
        StringComparer.Ordinal);

    public bool TryAcquire(string clientKey)
    {
        using var lease = limiter.AttemptAcquire(clientKey);
        return lease.IsAcquired;
    }

    public ValueTask DisposeAsync()
    {
        return limiter.DisposeAsync();
    }
}
