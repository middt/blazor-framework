using Microsoft.Extensions.DependencyInjection;

namespace Middt.Framework.Model.Model.Configuration;

public class DependencyModel
{
    /// <summary>Assembly qualified service type, e.g. <c>MyApp.IFoo, MyApp</c>.</summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>Assembly qualified implementation type, e.g. <c>MyApp.Foo, MyApp</c>.</summary>
    public string ImplementationType { get; set; } = string.Empty;

    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
}

public class DependencySettings
{
    public const string SectionName = nameof(DependencySettings);

    public List<DependencyModel> DependencyModelList { get; set; } = [];
}
