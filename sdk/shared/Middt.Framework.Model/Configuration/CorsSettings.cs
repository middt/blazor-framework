namespace Middt.Framework.Api.Configuration.Model;

public class CorsModel
{
    public string Name { get; set; } = string.Empty;
    public string[]? Url { get; set; }
}

public class CorsSettings
{
    public const string SectionName = nameof(CorsSettings);

    public List<CorsModel> CorsModelList { get; set; } = [];
}
