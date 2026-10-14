namespace Middt.Framework.Plugin.MassTransit.Settings;

/// <summary>RabbitMQ transport settings, bound from the <c>RabbitMqSettings</c> configuration section.</summary>
public class RabbitMqSettings
{
    public const string SectionName = nameof(RabbitMqSettings);

    /// <summary>AMQP URI (<c>amqp://user:pass@host:5672/vhost</c>). Takes precedence over <see cref="URL"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Host address (e.g. <c>rabbitmq://localhost/</c>) used when <see cref="ConnectionString"/> is empty.</summary>
    public string? URL { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}
