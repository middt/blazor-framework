using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Email;
using Middt.Framework.Model.Configuration;

namespace Middt.Framework.Common.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers services declared in the <c>DependencySettings</c> configuration section
    /// (configuration driven plug-ins). Unknown types fail fast at startup.
    /// </summary>
    public static IServiceCollection AddServicesFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(DependencySettings.SectionName).Get<DependencySettings>();
        foreach (var dependency in settings?.DependencyModelList ?? [])
        {
            var serviceType = Type.GetType(dependency.ServiceType, throwOnError: true)!;
            var implementationType = Type.GetType(dependency.ImplementationType, throwOnError: true)!;
            services.Add(new ServiceDescriptor(serviceType, implementationType, dependency.Lifetime));
        }

        return services;
    }

    public static IServiceCollection AddMiddtEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
