namespace Middt.Framework.Api.Configuration.Model;

public class GeneralSettings
{
    public const string SectionName = nameof(GeneralSettings);

    public string? PathBase { get; set; }
    /// <summary>Idle timeout: the session ends after this many minutes without activity.</summary>
    public int SessionTimeoutInMinute { get; set; } = 10;

    /// <summary>Absolute session lifetime in minutes, regardless of activity (default 12 hours).</summary>
    public int SessionAbsoluteTimeoutInMinute { get; set; } = 12 * 60;
    public string? Name { get; set; }
    public string? Logo { get; set; }
}
