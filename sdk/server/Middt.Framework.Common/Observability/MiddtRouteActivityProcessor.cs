using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace Middt.Framework.Common.Observability;

/// <summary>
/// Normalizes HTTP server/client span names (<see cref="Activity.DisplayName"/>) and <c>http.route</c> tags
/// into grouped route templates (e.g. <c>GET /api/workflows/{workflow}/instances</c>), and enriches PostgreSQL (<c>Npgsql</c>)
/// database spans with the SQL verb and table name (e.g. <c>DB SELECT wf.instances</c>).
/// </summary>
public sealed partial class MiddtRouteActivityProcessor : BaseProcessor<Activity>
{
    [GeneratedRegex(@"\{([^}:]+):[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteConstraintRegex();

    [GeneratedRegex(@"\b(?:(SELECT)\b[\s\S]*?\bFROM|(INSERT)\s+INTO|(UPDATE)|(DELETE)\s+FROM)\s+([""\w]+(?:\.[""\w]+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlOperationTableRegex();

    /// <inheritdoc />
    public override void OnEnd(Activity data)
    {
        if (data is null)
        {
            return;
        }

        // Enrich PostgreSQL / database spans (e.g. from Npgsql ActivitySource) with SQL verb + table name.
        if (string.Equals(data.Source.Name, "Npgsql", StringComparison.OrdinalIgnoreCase) ||
            data.GetTagItem("db.system") is not null ||
            data.GetTagItem("db.system.name") is not null)
        {
            EnrichDatabaseSpan(data);
            return;
        }

        var method = data.GetTagItem("http.request.method") as string
            ?? data.GetTagItem("http.method") as string;
        if (string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        var urlPath = data.GetTagItem("url.path") as string;
        if (string.IsNullOrWhiteSpace(urlPath))
        {
            var fullUrl = data.GetTagItem("url.full") as string
                ?? data.GetTagItem("http.url") as string;
            if (!string.IsNullOrWhiteSpace(fullUrl) && Uri.TryCreate(fullUrl, UriKind.Absolute, out var parsedUri))
            {
                urlPath = parsedUri.AbsolutePath;
            }
        }

        if (string.IsNullOrWhiteSpace(urlPath))
        {
            return;
        }

        var existingRoute = data.GetTagItem("http.route") as string;
        var normalizedRoute = NormalizeHttpRoute(urlPath, existingRoute);

        data.SetTag("url.path", urlPath);
        data.SetTag("http.route", normalizedRoute);
        data.DisplayName = $"{method.Trim().ToUpperInvariant()} {normalizedRoute}";
    }

    internal static void EnrichDatabaseSpan(Activity data)
    {
        var statement = data.GetTagItem("db.statement") as string
            ?? data.GetTagItem("db.query.text") as string;
        if (string.IsNullOrWhiteSpace(statement))
        {
            return;
        }

        var span = statement.AsSpan().TrimStart();
        while (span.StartsWith("--", StringComparison.Ordinal))
        {
            var newline = span.IndexOf('\n');
            if (newline < 0)
            {
                return;
            }

            span = span[(newline + 1)..].TrimStart();
        }

        var spaceIdx = span.IndexOfAny(" \t\r\n;".AsSpan());
        var firstVerb = (spaceIdx > 0 ? span[..spaceIdx] : span).ToString().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(firstVerb))
        {
            return;
        }

        data.SetTag("db.operation.name", firstVerb);

        var matches = SqlOperationTableRegex().Matches(statement);
        if (matches.Count > 0)
        {
            var operations = new List<string>(matches.Count);
            var tables = new List<string>(matches.Count);

            foreach (Match match in matches)
            {
                var op = !string.IsNullOrEmpty(match.Groups[1].Value) ? "SELECT"
                    : !string.IsNullOrEmpty(match.Groups[2].Value) ? "INSERT"
                    : !string.IsNullOrEmpty(match.Groups[3].Value) ? "UPDATE"
                    : "DELETE";
                var table = match.Groups[5].Value.Replace("\"", string.Empty, StringComparison.Ordinal);
                var opTable = $"{op} {table}";
                if (!operations.Contains(opTable, StringComparer.OrdinalIgnoreCase))
                {
                    operations.Add(opTable);
                }

                if (!tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    tables.Add(table);
                }
            }

            data.SetTag("db.collection.name", string.Join(", ", tables));
            data.DisplayName = $"DB {string.Join(" + ", operations)}";
        }
        else
        {
            data.DisplayName = $"DB {firstVerb}";
        }
    }

    /// <summary>
    /// Builds a grouped route template (e.g. <c>/api/workflows/{workflow}/instances</c>) from <paramref name="existingRoute"/>
    /// when it is a concrete ASP.NET Core route template, or infers the grouped route template from <paramref name="urlPath"/>
    /// when <paramref name="existingRoute"/> is missing or a catch-all (<c>{**</c> / <c>{*</c>).
    /// </summary>
    public static string NormalizeHttpRoute(string urlPath, string? existingRoute = null)
    {
        if (!string.IsNullOrWhiteSpace(existingRoute) &&
            !existingRoute.Contains("{*", StringComparison.Ordinal))
        {
            return CleanRouteTemplate(existingRoute);
        }

        if (string.IsNullOrWhiteSpace(urlPath))
        {
            return "/";
        }

        var trimmedPath = urlPath.Trim();
        var queryIndex = trimmedPath.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            trimmedPath = trimmedPath[..queryIndex];
        }

        var segments = trimmedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return "/";
        }

        // Group known Middt REST conventions when route template is unavailable (HttpClient or catch-all proxy):
        //   /api/workflows/{workflow}
        //   /api/workflows/{workflow}/instances
        //   /api/workflows/{workflow}/instances/{id}
        //   /api/workflows/{workflow}/instances/{id}/transitions/{transition}
        //   /api/ui/layouts/{key}
        if (segments.Length >= 3 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("workflows", StringComparison.OrdinalIgnoreCase))
        {
            segments[2] = "{workflow}";
            if (segments.Length >= 6 &&
                segments[3].Equals("instances", StringComparison.OrdinalIgnoreCase) &&
                segments[5].Equals("transitions", StringComparison.OrdinalIgnoreCase) &&
                segments.Length >= 7)
            {
                segments[4] = "{id}";
                segments[6] = "{transition}";
            }
        }
        else if (segments.Length >= 4 &&
                 segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                 segments[1].Equals("ui", StringComparison.OrdinalIgnoreCase) &&
                 segments[2].Equals("layouts", StringComparison.OrdinalIgnoreCase) &&
                 !segments[3].Equals("validate", StringComparison.OrdinalIgnoreCase))
        {
            segments[3] = "{key}";
        }

        for (var i = 0; i < segments.Length; i++)
        {
            if (IsDynamicIdentifier(segments[i]))
            {
                segments[i] = "{id}";
            }
        }

        return "/" + string.Join('/', segments);
    }

    private static string CleanRouteTemplate(string routeTemplate)
    {
        var cleaned = RouteConstraintRegex().Replace(routeTemplate.Trim(), "{$1}").Trim('/');
        return string.IsNullOrEmpty(cleaned) ? "/" : "/" + cleaned;
    }

    private static bool IsDynamicIdentifier(string segment)
    {
        if (Guid.TryParse(segment, out _))
        {
            return true;
        }

        if (long.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        return false;
    }
}
