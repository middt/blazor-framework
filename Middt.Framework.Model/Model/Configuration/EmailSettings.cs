namespace Middt.Framework.Api.Configuration.Model;

public class EmailSettings
{
    public const string SectionName = nameof(EmailSettings);

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string From { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool DefaultCredentials { get; set; }
    public bool EnableSsl { get; set; } = true;
}
