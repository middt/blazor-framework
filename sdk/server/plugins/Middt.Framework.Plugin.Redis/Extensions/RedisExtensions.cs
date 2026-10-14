using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Middt.Framework.Plugin.Redis;

public static class RedisExtensions
{
    /// <summary>Registers the Redis connection, lock and cache, binding <see cref="RedisSettings.SectionName"/>.</summary>
    public static IServiceCollection AddMiddtRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RedisSettings>()
            .Bind(configuration.GetSection(RedisSettings.SectionName));

        return services.AddMiddtRedisCacheCore();
    }

    /// <summary>Registers the Redis connection, lock and cache, configuring settings in code.</summary>
    public static IServiceCollection AddMiddtRedisCache(this IServiceCollection services, Action<RedisSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RedisSettings>().Configure(configure);

        return services.AddMiddtRedisCacheCore();
    }

    private static IServiceCollection AddMiddtRedisCacheCore(this IServiceCollection services)
    {
        services.AddOptions<RedisSettings>()
            .Validate(s => !string.IsNullOrWhiteSpace(s.ConnectionString), "RedisSettings.ConnectionString must be set.")
            .ValidateOnStart();

        services.AddSingleton<MiddtRedisConnection>();
        services.AddSingleton<MiddtRedislock>();
        services.AddSingleton<MiddtRedisCache>();
        return services;
    }
}
