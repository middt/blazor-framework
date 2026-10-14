using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Middt.Framework.Common.Database.Attributes;

namespace Middt.Framework.Common.Database;

/// <summary>
/// Translates a populated search model into a LINQ filter, driven by the
/// <c>Query*</c> attributes on its properties. Predicates are built as expression trees whose values become SQL
/// parameters (never literals or expression text), and property metadata is cached per type.
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
            if (BuildPredicate<T>(property, value) is { } predicate)
            {
                query = query.Where(predicate);
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

    /// <summary>
    /// Builds <c>x =&gt; x.Target op holder.Value</c>. The value is read from a holder object (like a C# closure), so EF Core
    /// sends it as a SQL parameter: one cached query / SQL plan per filter shape instead of one per distinct search value.
    /// </summary>
    private static Expression<Func<T, bool>>? BuildPredicate<T>(FilterProperty property, object? value)
    {
        var searchType = value switch
        {
            null => (Operator?)null,
            string s when s.Length == 0 => null,
            string => property.StringSearchType switch
            {
                StringSearchType.StartsWith => Operator.StartsWith,
                StringSearchType.EndsWith => Operator.EndsWith,
                StringSearchType.Equal => Operator.Equal,
                _ => Operator.Contains,
            },
            DateTime d when d == default => null,
            DateTime or DateTimeOffset or DateOnly => Compare(property.DateSearchType ?? DateSearchType.Equal),
            bool => Operator.Equal,
            _ when IsNumber(value) => NumberOperator(property, value),
            _ => null,
        };

        if (searchType is not { } op)
        {
            return null;
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        var member = Expression.PropertyOrField(parameter, property.Target);
        var memberType = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        var converted = value!.GetType() == memberType ? value : ConvertValue(value, memberType, property.Target);

        var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(memberType), converted)!;
        Expression operand = Expression.Property(Expression.Constant(holder), nameof(ValueHolder<int>.Value));
        if (operand.Type != member.Type)
        {
            operand = Expression.Convert(operand, member.Type);
        }

        Expression body = op switch
        {
            Operator.Contains => Expression.Call(member, StringContains, operand),
            Operator.StartsWith => Expression.Call(member, StringStartsWith, operand),
            Operator.EndsWith => Expression.Call(member, StringEndsWith, operand),
            Operator.GreaterThan => Expression.GreaterThan(member, operand),
            Operator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, operand),
            Operator.LessThan => Expression.LessThan(member, operand),
            Operator.LessThanOrEqual => Expression.LessThanOrEqual(member, operand),
            _ => Expression.Equal(member, operand),
        };

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static object ConvertValue(object value, Type targetType, string target)
    {
        try
        {
            return value switch
            {
                DateTime d when targetType == typeof(DateTimeOffset) => new DateTimeOffset(d),
                DateTime d when targetType == typeof(DateOnly) => DateOnly.FromDateTime(d),
                DateTimeOffset d when targetType == typeof(DateTime) => d.UtcDateTime,
                _ => Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"Filter value of type {value.GetType().Name} cannot be compared with '{target}' ({targetType.Name}).", ex);
        }
    }

    private static Operator? NumberOperator(FilterProperty property, object value)
    {
        if (!property.IncludeZero && Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            return null;
        }

        return property.IntSearchType switch
        {
            IntSearchType.GreaterThan => Operator.GreaterThan,
            IntSearchType.GreaterThanOrEqual => Operator.GreaterThanOrEqual,
            IntSearchType.LessThan => Operator.LessThan,
            IntSearchType.LessThanOrEqual => Operator.LessThanOrEqual,
            _ => Operator.Equal,
        };
    }

    private static Operator Compare(DateSearchType searchType) => searchType switch
    {
        DateSearchType.GreaterThan => Operator.GreaterThan,
        DateSearchType.GreaterThanOrEqual => Operator.GreaterThanOrEqual,
        DateSearchType.LessThan => Operator.LessThan,
        DateSearchType.LessThanOrEqual => Operator.LessThanOrEqual,
        _ => Operator.Equal,
    };

    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StringEndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

    private enum Operator
    {
        Equal,
        Contains,
        StartsWith,
        EndsWith,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    /// <summary>Closure-like holder: EF Core turns member access on it into a query parameter.</summary>
    private sealed class ValueHolder<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

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
