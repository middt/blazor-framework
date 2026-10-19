using System.ComponentModel.DataAnnotations;

namespace Middt.Framework.Rebus;

/// <summary>Configuration section <c>RebusSettings</c>.</summary>
public sealed class RebusSettings
{
    public const string SectionName = nameof(RebusSettings);

    /// <summary>RabbitMQ connection string, e.g. <c>amqp://user:password@host:5672</c>. Ignored when <see cref="UseInMemoryTransport"/> is set.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Queue this endpoint receives from (one queue per service, shared by its instances).</summary>
    [Required]
    public string InputQueueName { get; set; } = string.Empty;

    /// <summary>Messages that fail this many times are moved to <see cref="ErrorQueueName"/>.</summary>
    [Range(1, 100)]
    public int MaxDeliveryAttempts { get; set; } = 5;

    public string ErrorQueueName { get; set; } = "error";

    [Range(0, 100)]
    public int NumberOfWorkers { get; set; } = 1;

    [Range(1, 1000)]
    public int MaxParallelism { get; set; } = 5;

    /// <summary>In-process transport for development and tests (messages are lost on restart).</summary>
    public bool UseInMemoryTransport { get; set; }

    /// <summary>
    /// Send routes: assembly qualified message type → owner queue, e.g.
    /// <c>"MyApp.Messages.CreateInvoice, MyApp.Messages": "billing"</c>.
    /// </summary>
    public Dictionary<string, string> Routes { get; set; } = [];
}
