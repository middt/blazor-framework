using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Api;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Dependency;
using Middt.Framework.Common.Email;
using Middt.Framework.Common.Log;
using Middt.Framework.Model.Model.Authentication;
using Middt.UBKS.WebApi.config.Helper;
using Swashbuckle.AspNetCore.Filters;

namespace Middt.Sample.Api
{

    public class Startup : BaseApiStartup
    {
        public override void CustomConfigure(IApplicationBuilder app)
        {
            // throw new NotImplementedException();
        }

        public override void CustomConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IBaseConfiguration, ConfigurationHelper>();
            services.AddSingleton<IBaseLog, Log4Net>();
            services.AddSingleton<IEmailSender, ExchangeEmailSender>();

            //   var sp = services.BuildServiceProvider();
            //   services.AddDbContext<Frameworkv2Context>(options =>
            //options.UseSqlServer(sp.GetService<BaseConfiguration>().Get<DBSettings>().TestDB));

            //services.AddHangfire(_ => _.UseSqlServerStorage(ConfigurationHelper.Instance.Get<DBSettings>().HANGFIREDB));
            //services.AddDbContext<UBKS_DevelopmentContext>(ServiceLifetime.Transient);

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            //services.Configure<MvcOptions>(x => x.Filters.Add(new ValidateModelAttribute()));

            services.AddSwaggerExamplesFromAssemblyOf<LoginRequestModelSwaggerExample>();
            FrameworkDependencyHelper.Instance.LoadServiceCollection(services);

        }

        public override void CustomSignalRHub(IApplicationBuilder app)
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<TestHub>("/hubs/testhub");
            });
        }
    }
}
