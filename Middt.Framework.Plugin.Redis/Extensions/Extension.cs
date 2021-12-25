using Microsoft.Extensions.DependencyInjection;
using Middt.Framework.Common.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middt.Framework.Plugin.Redis
{
    public static class RedisExtension
    {
        public static void AddMiddtRedisCache(this IServiceCollection services, RedisSettings redisSettings)
        {
            services.AddSingleton(new MiddtRedisConnection(redisSettings));
            services.AddSingleton<MiddtRedislock>();
            services.AddSingleton<MiddtRedisCache>();
        }

    }
}
