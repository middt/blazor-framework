using System.Collections.Concurrent;

namespace Middt.Identity;

/// <summary>A message delivered by the <see cref="DevelopmentMailbox"/>.</summary>
public sealed record IdentityMailMessage(Guid Id, string To, string Subject, string Text, DateTimeOffset SentAt);

/// <summary>
/// Development <see cref="IIdentityMessageSender"/>: keeps the latest messages in memory for the <c>/dev/mailbox</c> page
/// instead of sending them. Registered with <c>TryAdd</c> – register a real sender for production.
/// </summary>
public sealed class DevelopmentMailbox(TimeProvider time) : IIdentityMessageSender
{
    private const int Capacity = 50;

    private readonly ConcurrentQueue<IdentityMailMessage> messages = new();

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<IdentityMailMessage> Messages => [.. messages.Reverse()];

    public Task SendAsync(string email, string subject, string text, CancellationToken cancellationToken = default)
    {
        messages.Enqueue(new IdentityMailMessage(Guid.NewGuid(), email, subject, text, time.GetUtcNow()));
        while (messages.Count > Capacity && messages.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }
}
