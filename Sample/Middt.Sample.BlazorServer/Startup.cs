using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Blazor.Web;
using Middt.Framework.Blazor.Web.Base.Notification;
using Middt.Framework.Blazor.Web.Log;
using Middt.Framework.Blazor.Web.Security;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Notification;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Security.Refit;
using Middt.Sample.BlazorServer.config.Helper;
using Middt.Sample.BlazorServer.Model;
using Middt.Sample.BlazorServer.Service;
using Middt.Sample.BlazorServer.SignalR;
using Middt.Sample.Common.Service;
using Syncfusion.Blazor;
namespace Middt.Sample.BlazorServer
{
    public class Startup : BaseBlazorStartup
    {


        public override void CustomConfigure(IApplicationBuilder app)
        {
            // throw new System.NotImplementedException();
        }

        public override void CustomConfigureServices(IServiceCollection services)
        {
            // Theme Radzen Bolumu
            services.AddHttpContextAccessor();
            services.AddScoped<ThemeState>();
            services.AddScoped<MenuService>();

            // Framework
            services.AddScoped<INotification, RadzenNotification>();
            services.AddTransient<IBaseLog, BrowserConsoleLog>();
            services.AddSingleton<IBaseConfiguration, ConfigurationHelper>();
            //


            // Security Bolumu

            services.AddSyncfusionBlazor();
            services.AddScoped<IBaseSessionState, BaseProtectedLocalStorage>();
            services.AddScoped<BaseTokenHelper>(x => new BaseTokenHelper(x.GetRequiredService<IBaseSessionState>()));

            services.AddScoped<ITokenService, TokenService>();
            // Security Bolumu


            // Rest Servisler Bolumu
            services.AddScoped<CategoryService>();
            services.AddScoped<ProductService>();
            services.AddScoped<ViewOrderService>();
            services.AddScoped<StoreService>();
            services.AddScoped<CustomerService>();
            services.AddScoped<StaffService>();

            // Restservisler Bolumu

            // Signalr
            services.AddTransient<TestSignalRClient>();
        }
    }
}
