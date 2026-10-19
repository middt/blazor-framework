using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Middt.Framework.Ui.Server.Persistence;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Server.Runtime;
using Middt.Framework.Ui.Validation;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Server.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the layout store, validator and static renderer:
    /// <code>
    /// builder.Services.AddMiddtUi(o => o
    ///     .UseDbContext(db => db.UseNpgsql(connectionString))
    ///     .AddLayoutsFromDirectory("Layouts"));
    /// app.MapMiddtUi();
    /// </code>
    /// </summary>
    public static IServiceCollection AddMiddtUi(this IServiceCollection services, Action<MiddtUiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MiddtUiOptions();
        configure(options);
        if (options.ConfigureDbContext is null)
        {
            throw new InvalidOperationException("Configure the layout database with options.UseDbContext(...).");
        }

        services.Configure(configure);
        services.AddDbContext<UiDbContext>(options.ConfigureDbContext);
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<MiddtUiOptions>>().Value.Widgets);
        services.TryAddSingleton(sp => new UiLayoutValidator(sp.GetRequiredService<UiWidgetCatalog>()));
        services.AddScoped<UiLayoutStore>();
        services.AddSingleton<UiHtmlRenderer>();
        services.AddHostedService<UiStartup>();
        return services;
    }
}
