using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Client.DependencyInjection;
using Middt.Framework.Client.Http;
using Middt.Framework.Blazor.Ui.Rendering;

namespace Middt.Framework.Blazor.Ui;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Blazor renderer of the server driven UI: widget specs + components (display widgets and the
    /// interactive <c>grid</c> and <c>record</c>), the layout API client and the entity data client. Custom widgets:
    /// <code>builder.Services.AddMiddtUi(w =&gt; w.Add&lt;MyChartWidget&gt;(MyChartWidget.Spec));</code>
    /// The API must know the same custom specs (<c>MiddtUiOptions.AddWidget</c>) to accept layouts that use them.
    /// </summary>
    public static IServiceCollection AddMiddtUi(this IServiceCollection services, Action<UiBlazorWidgets>? configure = null)
    {
        var widgets = UiBlazorWidgets.CreateDefault();
        configure?.Invoke(widgets);

        services.AddSingleton(widgets);
        services.AddSingleton(widgets.Catalog);
        services.AddMiddtApiClient<UiApiClient>();
        services.AddMiddtApiClient<UiDataClient>();
        services.AddScoped<UiLookupCache>();
        return services;
    }
}
