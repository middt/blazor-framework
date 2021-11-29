using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radzen;
using Suges.Framework.Blazor.Web.Configuration;
using Suges.Framework.Common.Dependency;
using Suges.Framework.Model.Model.Configuration;
using System;

namespace Suges.Framework.Blazor.Web
{
    public abstract class BaseBlazorStartup
    {
        public abstract void CustomConfigureServices(IServiceCollection services);
        public abstract void CustomConfigure(IApplicationBuilder app);

        protected BaseBlazorConfiguration baseBlazorConfiguration;
        public BaseBlazorStartup(IConfiguration configuration)
        {
            Configuration = configuration;
            baseBlazorConfiguration = new BaseBlazorConfiguration();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor().AddCircuitOptions(o =>
            {
                o.DetailedErrors = true;
            });

            // services.AddScoped<CircuitHandler>((sp) => new CircuitHandlerService(sp.GetRequiredService<SessionData>()));
            //services.AddScoped<CircuitHandler, CircuitHandlerService>();

            services.AddScoped<DialogService>();
            services.AddScoped<NotificationService>();
            services.AddScoped<TooltipService>();
            services.AddScoped<ContextMenuService>();
            // Theme Bolumu

            AddSettingsService(services);
            CustomConfigureServices(services);

            FrameworkDependencyHelper.Instance.LoadServiceCollection(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                //GeneralSettings generalSettings = baseBlazorConfiguration.Get<GeneralSettings>();

                //endpoints.MapBlazorHub(generalSettings.PathBase);
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });

            CustomConfigure(app);
        }
        protected void AddSettingsService(IServiceCollection services)
        {
            // configden gelen dependencyler ekleniyor.
            DependencySettings dependencySettings = baseBlazorConfiguration.Get<DependencySettings>();
            if (
            dependencySettings != null &&
            dependencySettings.DependencyModelList != null &&
            dependencySettings.DependencyModelList.Count > 0)
            {
                foreach (DependencyModel service in dependencySettings.DependencyModelList)
                {
                    services.Add(new ServiceDescriptor(serviceType: Type.GetType(service.ServiceType),
                                                       implementationType: Type.GetType(service.ImplementationType),
                                                       lifetime: service.Lifetime));
                }
            }
        }
    }
}
