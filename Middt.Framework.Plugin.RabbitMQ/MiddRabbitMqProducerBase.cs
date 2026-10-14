using System.Text.Json;
using Microsoft.Extensions.Logging;
using Middt.Framework.Common.Helper;
using Middt.Framework.Common.Model.Data;
using RabbitMQ.Client;

namespace Middt.Framework.Plugin.RabbitMQ;

/// <summary>Publishes JSON messages of type <typeparamref name="T"/> to the configured exchange.</summary>
public abstract class MiddRabbitMqProducerBase<T>(IConnectionFactory connectionFactory, ExchangeTypes exchangeType, ILogger logger)
    : MiddtRabbitMqConnection(connectionFactory, exchangeType, logger)
{
    private const string PublishFailedMessage = "Message could not be published.";

    public async Task<BaseResponseModel> PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonHelper.Options);
            var properties = new BasicProperties
            {
                AppId = AppId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            };

            await Channel.BasicPublishAsync(
                exchange: RefinedExchangeName,
                routingKey: RefinedRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return BaseResponseModel.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Publishing failures are reported through the response envelope.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Logger.LogError(ex, "Error publishing message to exchange {Exchange} with routing key {RoutingKey}", RefinedExchangeName, RefinedRoutingKey);
            return BaseResponseModel.Failure(PublishFailedMessage);
        }
    }
}
