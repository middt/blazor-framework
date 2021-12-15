using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Api;
using Middt.Framework.Api.Cache;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Dependency;
using Middt.Framework.Common.Email;
using Middt.Framework.Common.Log;
using Middt.Framework.Model.Model.Authentication;
using Middt.Framework.Model.Model.Configuration;
using Middt.Framework.Plugin.MassTransit;
using Middt.Framework.Plugin.Redis;
using Middt.Sample.Api.config.Helper;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.Filters;
using System.Collections.Generic;

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

            //services.AddSingleton<BaseRedisConnection>();
            //services.AddSingleton<BaseRedislock>();

            var sp = services.BuildServiceProvider();

            ConfigurationOptions config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                SyncTimeout = 500000
            };

            List<RedisModel> listRedisModel = sp.GetService<IBaseConfiguration>().Get<RedisSettings>().RedisModelList;
            foreach (RedisModel redisModel in listRedisModel)
            {
                config.EndPoints.Add(redisModel.Host, redisModel.Port);
            }

            services.AddStackExchangeRedisCache(action =>
            {
                action.ConfigurationOptions = config;
            });
            services.AddSignalR().AddStackExchangeRedis(action =>
            {
                action.Configuration = config;
            });



            services.AddMiddtRedisCache(sp.GetService<IBaseConfiguration>().Get<RedisSettings>());
            
            

            // 
            // services.AddSingleton<IMemoryCache, MemoryCache>();
            services.AddSingleton<IDistributedCache, RedisCache>();
            services.AddSingleton<IBaseCache, BaseDistributedCache>();

            //   var sp = services.BuildServiceProvider();
            //   services.AddDbContext<Frameworkv2Context>(options =>
            //options.UseSqlServer(sp.GetService<BaseConfiguration>().Get<DBSettings>().TestDB));
            
            services.AddSingleton<BaseMassTransitConnection>();


            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

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
