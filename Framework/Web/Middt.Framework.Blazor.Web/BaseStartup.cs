using Radzen;
using Middt.Framework.Blazor.Web.Configuration;
using Middt.Framework.Common.Dependency;
using Middt.Framework.Model.Model.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Middt.Framework.Blazor.Web
{
    public abstract class BaseBlazorStartup
    {
        public abstract void CustomConfigureServices(IServiceCollection services);
        public abstract void CustomConfigure(IApplicationBuilder app);

        protected BaseBlazorConfiguration baseBlazorConfiguration;
        public BaseBlazorStartup()
        {
            baseBlazorConfiguration = new BaseBlazorConfiguration();
        }
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
        public void ConfigureApplication(WebApplication app)
        {
            if (app.Environment.IsDevelopment())
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
