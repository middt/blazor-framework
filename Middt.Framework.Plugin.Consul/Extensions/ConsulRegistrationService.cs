using System.Net;
using Consul;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Plugin.Consul.Settings;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Middt.Framework.Plugin.Consul.Extensions;

/// <summary>
/// Registers the application in Consul once the server has started listening and deregisters it on shutdown.
/// </summary>
public sealed partial class ConsulRegistrationService(
    IConsulClient consulClient,
    IServer server,
    IHostApplicationLifetime lifetime,
    IOptions<ConsulSettings> options,
    ILogger<ConsulRegistrationService> logger) : BackgroundService
{
    private readonly ConsulSettings _settings = options.Value;
    private string? _registeredServiceId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()).ConfigureAwait(false))
        {
            try
            {
                await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var address = ResolveServerAddress();
        if (address is null)
        {
            LogNoServerAddress();
            return;
        }

        var host = string.IsNullOrWhiteSpace(_settings.ServiceAddress) ? address.Value.Host : _settings.ServiceAddress;
        var port = address.Value.Port;
        var serviceId = string.IsNullOrWhiteSpace(_settings.ServiceId) ? $"{_settings.ServiceName}-{host}-{port}" : _settings.ServiceId;

        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = _settings.ServiceName,
            Address = host,
            Port = port,
            Checks =
            [
                new AgentCheckRegistration
                {
                    HTTP = $"{address.Value.Scheme}://{host}:{port}/{_settings.HealthyCheckURL.TrimStart('/')}",
                    Notes = "Checks hc",
                    Timeout = TimeSpan.FromSeconds(3),
                    Interval = TimeSpan.FromSeconds(10),
                },
            ],
        };

        try
        {
            LogRegistering(serviceId, host, port);
            await consulClient.Agent.ServiceRegister(registration, stoppingToken).ConfigureAwait(false);
            _registeredServiceId = serviceId;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // A Consul outage must not crash the application.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRegistrationFailed(ex, serviceId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_registeredServiceId is not { } serviceId)
        {
            return;
        }

        try
        {
            LogDeregistering(serviceId);
            await consulClient.Agent.ServiceDeregister(serviceId, cancellationToken).ConfigureAwait(false);
            _registeredServiceId = null;
        }
#pragma warning disable CA1031 // Deregistration is best effort.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeregistrationFailed(ex, serviceId);
        }
    }

    private (string Scheme, string Host, int Port)? ResolveServerAddress()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count == 0)
        {
            return null;
        }

        var bindings = addresses.Select(BindingAddress.Parse).ToList();
        var binding = bindings.FirstOrDefault(b => string.Equals(b.Scheme, "http", StringComparison.OrdinalIgnoreCase)) ?? bindings[0];

        var host = binding.Host;
        if (host is "+" or "*" or "[::]" or "0.0.0.0")
        {
            host = Dns.GetHostName();
        }

        return (binding.Scheme, host, binding.Port);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registering service {ServiceId} ({Host}:{Port}) with Consul")]
    private partial void LogRegistering(string serviceId, string host, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deregistering service {ServiceId} from Consul")]
    private partial void LogDeregistering(string serviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No server address available; skipping Consul registration")]
    private partial void LogNoServerAddress();

    [LoggerMessage(Level = LogLevel.Error, Message = "Consul registration of {ServiceId} failed")]
    private partial void LogRegistrationFailed(Exception exception, string serviceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consul deregistration of {ServiceId} failed")]
    private partial void LogDeregistrationFailed(Exception exception, string serviceId);
}
