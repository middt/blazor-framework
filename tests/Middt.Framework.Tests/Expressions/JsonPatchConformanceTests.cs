using System.Text.Json.Nodes;
using Middt.Framework.Expressions;

namespace Middt.Framework.Tests.Expressions;

/// <summary>
/// Runs the shared JSON Patch suite (spec/json-patch.conformance.json) against <see cref="JsonPatch"/>; the TypeScript client
/// (sdk/client/ui-core) runs the same diff cases, so a form a frontend sends as a patch means the same thing on the server.
/// </summary>
public sealed class JsonPatchConformanceTests
{
    private static readonly string SuiteJson = File.ReadAllText(Path.Combine(RepositoryRoot(), "spec", "json-patch.conformance.json"));

    // Parsed per use: JsonObject materializes lazily and is not safe for concurrent reads by parallel test cases.
    private static JsonObject Suite => (JsonObject)JsonNode.Parse(SuiteJson)!;

    public static TheoryData<string> DiffCases() => Names("diff");

    public static TheoryData<string> ApplyCases() => Names("apply");

    private static TheoryData<string> Names(string section)
    {
        var data = new TheoryData<string>();
        foreach (var c in Suite[section]!.AsArray())
        {
            data.Add((string)c!["name"]!);
        }

        return data;
    }

    private static JsonObject Case(string section, string name) => Suite[section]!.AsArray().Single(c => (string)c!["name"]! == name)!.AsObject();

    [Theory]
    [MemberData(nameof(DiffCases))]
    public void Diff_produces_the_specified_patch_and_it_replays(string name)
    {
        var c = Case("diff", name);
        var from = c["from"]!.AsObject();
        var to = c["to"]!.AsObject();

        var patch = JsonPatch.Diff(from, to);

        patch.ToJsonString().Should().Be(c["patch"]!.ToJsonString());
        var replayed = (JsonObject)from.DeepClone();
        JsonPatch.ApplyStrict(replayed, patch);
        JsonNode.DeepEquals(replayed, to).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ApplyCases))]
    public void ApplyStrict_follows_RFC_6902(string name)
    {
        var c = Case("apply", name);
        var document = c["document"]!.AsObject();
        var before = document.ToJsonString();
        var act = () => JsonPatch.ApplyStrict(document, c["patch"]!.AsArray());

        if (c["result"] is JsonObject result)
        {
            act();
            JsonNode.DeepEquals(document, result).Should().BeTrue(document.ToJsonString());
            if (c["resultText"] is JsonValue text)
            {
                document.ToJsonString().Should().Be(text.GetValue<string>(), "member order is kept");
            }

            return;
        }

        var failure = act.Should().Throw<JsonPatchException>().Which;
        failure.IsTestFailure.Should().Be(c["conflict"]?.GetValue<bool>() == true);
        document.ToJsonString().Should().Be(before, "a failing patch leaves the document unchanged");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
