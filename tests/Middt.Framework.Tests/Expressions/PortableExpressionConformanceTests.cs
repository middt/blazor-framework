using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Middt.Framework.Expressions;
using Middt.Framework.Expressions.Portable;

namespace Middt.Framework.Tests.Expressions;

/// <summary>
/// Runs the shared conformance suite (spec/expressions.conformance.json) against the .NET implementation; the TypeScript
/// client (sdk/client/ui-core) runs the same file, so both frontends evaluate layouts identically.
/// </summary>
public sealed class PortableExpressionConformanceTests
{
    private static readonly string SuiteJson = File.ReadAllText(Path.Combine(RepositoryRoot(), "spec", "expressions.conformance.json"));

    // Parsed per use: JsonObject materializes lazily and is not safe for concurrent reads by parallel test cases.
    private static JsonObject Suite => (JsonObject)JsonNode.Parse(SuiteJson)!;

    public static TheoryData<int, string> Cases()
    {
        var data = new TheoryData<int, string>();
        var cases = Suite["cases"]!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]!.AsObject();
            data.Add(i, (string?)c["expr"] ?? (string?)c["template"] ?? (string?)c["truthy"] ?? string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_matches_the_specification(int index, string text)
    {
        var suite = Suite;
        var c = suite["cases"]![index]!.AsObject();
        var engine = new PortableExpression(suite["variables"]!.AsArray().Select(v => (string)v!));
        var context = Context(suite);
        var isTemplate = c.ContainsKey("template");

        if (c["error"]?.GetValue<bool>() == true)
        {
            List<string?> errors = isTemplate ? [.. engine.ValidateTemplate(JsonValue.Create(text))] : [engine.Validate(text)];
            errors.Should().ContainSingle().Which.Should().NotBeNull($"'{text}' must be rejected");
            return;
        }

        (isTemplate ? engine.ValidateTemplate(JsonValue.Create(text)) : new[] { engine.Validate(text) }.OfType<string>()).Should().BeEmpty();
        var actual = isTemplate ? engine.Render(JsonValue.Create(text), context)
            : c.ContainsKey("truthy") ? JsonValue.Create(engine.EvaluateCondition(text, context))
            : engine.EvaluateToNode(text, context);

        Normalize(actual).Should().Be(Normalize(c["expected"]), $"'{text}'");
    }

    private static ExpressionContext Context(JsonObject suite)
    {
        var source = suite["context"]!.AsObject();
        var user = source["user"]!.AsObject();
        var context = new ExpressionContext
        {
            User = new ExpressionUser
            {
                Id = (string?)user["id"],
                Name = (string?)user["name"],
                Roles = [.. user["roles"]!.AsArray().Select(r => (string)r!)],
            },
            Now = DateTimeOffset.Parse((string)source["now"]!, CultureInfo.InvariantCulture),
        };

        foreach (var (name, value) in source)
        {
            if (name is not ("user" or "now"))
            {
                context.Set(name, value?.DeepClone());
            }
        }

        return context;
    }

    // Numbers compare by value (15000 == 15000.0); everything else by canonical JSON text.
    private static string Normalize(JsonNode? node) => node switch
    {
        null => "null",
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{Normalize(p.Value)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Normalize)) + "]",
        _ => node.ToJsonString(),
    };

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
