namespace Middt.Framework.Api.Configuration.Model;

public class ApiSettings
{
    public const string SectionName = nameof(ApiSettings);

    /// <summary>Base address of the backend API, e.g. <c>https://localhost:5001</c>.</summary>
    public string URL { get; set; } = string.Empty;

    /// <summary>Default API version segment used by generated clients.</summary>
    public string Version { get; set; } = "1.0";
}
