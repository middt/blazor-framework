namespace Middt.Framework.Plugin.Consul.Settings;

/// <summary>Consul registration settings, bound from the <c>ConsulSettings</c> configuration section.</summary>
public class ConsulSettings
{
    public const string SectionName = nameof(ConsulSettings);

    /// <summary>Service name registered in Consul.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Consul agent address, e.g. <c>http://localhost:8500</c>.</summary>
    public string ConsoleHost { get; set; } = string.Empty;

    /// <summary>Relative path of the health endpoint polled by Consul.</summary>
    public string HealthyCheckURL { get; set; } = "health";

    /// <summary>
    /// Host name/IP Consul should use to reach this service (e.g. <c>host.docker.internal</c>).
    /// When empty, the host of the server's listening address is used (wildcards resolve to the machine name).
    /// </summary>
    public string? ServiceAddress { get; set; }

    /// <summary>Explicit service id. Defaults to <c>{ServiceName}-{host}-{port}</c>.</summary>
    public string? ServiceId { get; set; }
}
