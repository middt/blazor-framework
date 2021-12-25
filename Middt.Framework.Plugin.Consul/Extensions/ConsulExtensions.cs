using Consul;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Plugin.Consul.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Consul.Extensions
{
    public static class ConsulExtensions
    {
        public static IServiceCollection AddConsulConfig(this IServiceCollection services, ConsulSettings consulSettings)
        {
            services.AddSingleton<IConsulClient, ConsulClient>(p => new ConsulClient(consulConfig =>
            {
                consulConfig.Address = new Uri(consulSettings.ConsoleHost);
            }));
            return services;
        }

        public static IApplicationBuilder UseConsul(this IApplicationBuilder app, ConsulSettings consulSettings)
        {
            var consulClient = app.ApplicationServices.GetRequiredService<IConsulClient>();
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("AppExtensions");
            var lifetime = app.ApplicationServices.GetRequiredService<IApplicationLifetime>();

            IConfiguration configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();

            var server = app.ApplicationServices.GetRequiredService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();

            if (!(app.ServerFeatures is FeatureCollection features)) return app;

            var addresses = features.Get<IServerAddressesFeature>();
            var address = addresses.Addresses.First();

            var uri = new Uri(address);


            AgentCheckRegistration httpCheck = new AgentCheckRegistration()
            {
                // TODO : Added test purpose change commented

#if DEBUG
                HTTP = $"{uri.Scheme}://host.docker.internal:{uri.Port}/{consulSettings.HealthyCheckURL}",
#else

                HTTP = $"{uri.Scheme}://{uri.Host}:{uri.Port}/{consulSettings.HealthyCheckURL}",
#endif


                Notes = "Checks hc",
                Timeout = TimeSpan.FromSeconds(3),
                Interval = TimeSpan.FromSeconds(10)
            };

            var registration = new AgentServiceRegistration()
            {
                ID = $"{consulSettings.ServiceName}-{uri.Port}",
                // servie name  
                Name = consulSettings.ServiceName,
                Address = uri.Host,
                Port = uri.Port,
                Checks = new[] { httpCheck }
            };

            logger.LogInformation("Registering with Consul");
            consulClient.Agent.ServiceDeregister(registration.ID).ConfigureAwait(true);
            consulClient.Agent.ServiceRegister(registration).ConfigureAwait(true);

            lifetime.ApplicationStopping.Register(() =>
            {
                logger.LogInformation("Unregistering from Consul");
                consulClient.Agent.ServiceDeregister(registration.ID).ConfigureAwait(true);
            });

            return app;
        }
    }
}
