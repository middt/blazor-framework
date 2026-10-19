using RabbitMQ.Client;

namespace Middt.Demo.Api;

/// <summary>
/// Pre-declares sample queues in RabbitMQ so that messaging demo workflows can send and defer messages.
/// </summary>
public sealed class RabbitMqQueueInitializer(
    IConfiguration configuration,
    ILogger<RabbitMqQueueInitializer> logger) : BackgroundService
{
    private static readonly string[] RequiredQueues = ["order-processing", "order-reminders"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var messaging = configuration.GetConnectionString("messaging");
        if (string.IsNullOrWhiteSpace(messaging))
        {
            return;
        }

        // Retry a few times in case RabbitMQ is still initializing during compose startup
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                var factory = new ConnectionFactory { Uri = new Uri(messaging) };
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                foreach (var queue in RequiredQueues)
                {
                    await channel.QueueDeclareAsync(
                        queue: queue,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        cancellationToken: stoppingToken);
                }

                logger.LogInformation("Sample RabbitMQ queues declared successfully: {Queues}", string.Join(", ", RequiredQueues));
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "RabbitMQ queue initialization attempt {Attempt}/10 failed; retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
