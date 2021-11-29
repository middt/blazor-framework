using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Suges.Framework.Blazor.Web;
using Suges.Framework.Blazor.Web.Base.Notification;
using Suges.Framework.Blazor.Web.Log;
using Suges.Framework.Blazor.Web.Security;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Notification;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Security.Refit;
using Suges.Template.BlazorServer.config.Helper;
using Suges.Template.BlazorServer.Model;
using Suges.Template.BlazorServer.Service;
using Suges.Template.BlazorServer.SignalR;
using Suges.Template.Common.Service;
using Syncfusion.Blazor;
namespace Suges.Template.BlazorServer
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
            services.AddScoped<WeatherForecastService>();
            services.AddScoped<Table1SecureService>();
            services.AddScoped<Table1Service>();
            // Restservisler Bolumu

            // Signalr
            services.AddScoped<TestSignalRClient>();
        }
    }
}
