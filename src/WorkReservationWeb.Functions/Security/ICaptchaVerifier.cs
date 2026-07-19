namespace WorkReservationWeb.Functions.Security;

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken);
}
