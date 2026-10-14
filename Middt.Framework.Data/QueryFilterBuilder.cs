using System.Collections.Concurrent;
using System.Linq.Dynamic.Core;
using System.Reflection;
using Middt.Framework.Common.Database.Attributes;

namespace Middt.Framework.Common.Database;

/// <summary>
/// Translates a populated search model into a LINQ filter, driven by the
/// <c>Query*</c> attributes on its properties. All values are passed as parameters
/// (never concatenated into the expression) and property metadata is cached per type.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>null values, empty strings, read-only properties and <see cref="QueryIsNoFilterAttribute"/> are skipped.</item>
/// <item>strings use <see cref="QueryStringAttribute"/> (default: Contains).</item>
/// <item>numbers use <see cref="QueryIntAttribute"/> (default: Equal); zero is skipped unless <see cref="QueryIsZeroAttribute"/> is set.</item>
/// <item>dates use <see cref="QueryDateAttribute"/> (default: Equal); <c>default(DateTime)</c> is skipped.</item>
/// <item>non-nullable <see cref="bool"/> properties always filter; declare them as <c>bool?</c> to make them optional.</item>
/// </list>
/// </remarks>
public static class QueryFilterBuilder
{
    private static readonly ConcurrentDictionary<Type, FilterProperty[]> FilterCache = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, string>> SortCache = new();

    public static IQueryable<T> ApplyFilter<T>(IQueryable<T> query, T? filter)
    {
        if (filter is null)
        {
            return query;
        }

        foreach (var property in FilterCache.GetOrAdd(typeof(T), BuildFilterProperties))
        {
            var value = property.Info.GetValue(filter);
            var predicate = BuildPredicate(property, value);
            if (predicate is not null)
            {
                query = query.Where(predicate, value);
            }
        }

        return query;
    }

    /// <summary>
    /// Validates a client supplied sort property against the public properties of <typeparamref name="T"/>
    /// (case-insensitive) and returns its canonical name, or <c>null</c> when it is unknown.
    /// </summary>
    public static string? ResolveSortProperty<T>(string? sortProperty)
    {
        if (string.IsNullOrWhiteSpace(sortProperty))
        {
            return null;
        }

        var properties = SortCache.GetOrAdd(typeof(T), type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.Name, StringComparer.OrdinalIgnoreCase));

        return properties.TryGetValue(sortProperty, out var name) ? name : null;
    }

    private static string? BuildPredicate(FilterProperty property, object? value) => value switch
    {
        null => null,
        string s when s.Length == 0 => null,
        string => StringPredicate(property),
        DateTime d when d == default => null,
        DateTime or DateTimeOffset or DateOnly => ComparePredicate(property.Target, property.DateSearchType ?? DateSearchType.Equal),
        bool => $"{property.Target} == @0",
        _ when IsNumber(value) => NumberPredicate(property, value),
        _ => null,
    };

    private static string StringPredicate(FilterProperty property) => property.StringSearchType switch
    {
        StringSearchType.StartsWith => $"{property.Target}.StartsWith(@0)",
        StringSearchType.EndsWith => $"{property.Target}.EndsWith(@0)",
        StringSearchType.Equal => $"{property.Target} == @0",
        _ => $"{property.Target}.Contains(@0)",
    };

    private static string? NumberPredicate(FilterProperty property, object value)
    {
        if (!property.IncludeZero && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            return null;
        }

        var searchType = property.IntSearchType switch
        {
            IntSearchType.GreatThan => DateSearchType.GreatThan,
            IntSearchType.GreaterThanOrEqual => DateSearchType.GreaterThanOrEqual,
            IntSearchType.LessThan => DateSearchType.LessThan,
            IntSearchType.LessThanOrEqual => DateSearchType.LessThanOrEqual,
            _ => DateSearchType.Equal,
        };

        return ComparePredicate(property.Target, searchType);
    }

    private static string ComparePredicate(string target, DateSearchType searchType) => searchType switch
    {
        DateSearchType.GreatThan => $"{target} > @0",
        DateSearchType.GreaterThanOrEqual => $"{target} >= @0",
        DateSearchType.LessThan => $"{target} < @0",
        DateSearchType.LessThanOrEqual => $"{target} <= @0",
        _ => $"{target} == @0",
    };

    private static bool IsNumber(object value) => value is
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static FilterProperty[] BuildFilterProperties(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .Where(p => !p.IsDefined(typeof(QueryIsNoFilterAttribute), inherit: true))
        .Select(p =>
        {
            var date = p.GetCustomAttribute<QueryDateAttribute>(inherit: true);
            return new FilterProperty(
                p,
                Target: string.IsNullOrEmpty(date?.PropertyName) ? p.Name : date.PropertyName,
                StringSearchType: p.GetCustomAttribute<QueryStringAttribute>(inherit: true)?.SearchType,
                IntSearchType: p.GetCustomAttribute<QueryIntAttribute>(inherit: true)?.SearchType,
                DateSearchType: date?.SearchType,
                IncludeZero: p.IsDefined(typeof(QueryIsZeroAttribute), inherit: true));
        })
        .ToArray();

    private sealed record FilterProperty(
        PropertyInfo Info,
        string Target,
        StringSearchType? StringSearchType,
        IntSearchType? IntSearchType,
        DateSearchType? DateSearchType,
        bool IncludeZero);
}
