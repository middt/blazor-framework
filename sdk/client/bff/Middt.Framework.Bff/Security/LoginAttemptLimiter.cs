using System.Threading.RateLimiting;
using Middt.Framework.Bff.DependencyInjection;

namespace Middt.Framework.Bff.Security;

/// <summary>
/// Second login limiter, chained after the per-IP rate limiting policy: failed attempts per normalised username across
/// all client IPs (<see cref="MiddtBffOptions.LoginAttemptsPerUsernamePerMinute"/>). Limits distributed guessing against
/// one account without throttling legitimate sign-ins (successful logins do not count); it works on the bound (validated)
/// form field, so no request body is read by middleware.
/// </summary>
public sealed class LoginAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string>? limiter;

    public LoginAttemptLimiter(MiddtBffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.LoginAttemptsPerUsernamePerMinute > 0)
        {
            limiter = PartitionedRateLimiter.Create<string, string>(username => RateLimitPartition.GetFixedWindowLimiter(
                username,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.LoginAttemptsPerUsernamePerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        }
    }

    /// <summary>Case and surrounding whitespace do not create new partitions.</summary>
    public static string Normalize(string username) => username.Trim().ToUpperInvariant();

    /// <summary><c>false</c> when <paramref name="username"/> has used up its failed attempts of the current window.</summary>
    public bool IsAllowed(string username)
    {
        if (limiter is null)
        {
            return true;
        }

        // A zero permit request only checks availability.
        using var lease = limiter.AttemptAcquire(Normalize(username), permitCount: 0);
        return lease.IsAcquired;
    }

    /// <summary>Counts a failed attempt of <paramref name="username"/>.</summary>
    public void RecordFailure(string username)
    {
        if (limiter is not null)
        {
            limiter.AttemptAcquire(Normalize(username)).Dispose();
        }
    }

    public void Dispose() => limiter?.Dispose();
}
