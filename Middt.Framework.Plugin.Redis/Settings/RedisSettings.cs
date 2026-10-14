namespace Middt.Framework.Plugin.Redis;

/// <summary>Redis connection settings, bound from the <c>RedisSettings</c> configuration section.</summary>
public class RedisSettings
{
    public const string SectionName = nameof(RedisSettings);

    /// <summary>StackExchange.Redis configuration string, e.g. <c>localhost:6379,abortConnect=false</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Redis logical database used by <see cref="MiddtRedisCache"/> (-1 = connection default).</summary>
    public int Database { get; set; } = -1;
}
