using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;
using Middt.Identity.Ui.Journeys;

namespace Middt.Framework.Tests.Identity.Ui;

public sealed class JourneyInputMapperTests
{
    private static JsonObject Schema(string json) => JsonNode.Parse(json)!.AsObject();

    private static Dictionary<string, StringValues> Form(params (string Name, string[] Values)[] fields) =>
        fields.ToDictionary(f => f.Name, f => new StringValues(f.Values), StringComparer.Ordinal);

    [Fact]
    public void Posted_fields_are_typed_by_the_schema()
    {
        var schema = Schema("""
            {"type":"object","properties":{
              "userName":{"type":"string"},"age":{"type":"integer"},"ratio":{"type":"number"},
              "remember":{"type":"boolean"},"tags":{"type":"array","items":{"type":"string"}}}}
            """);

        var input = JourneyInputMapper.Map(schema, Form(
            ("userName", ["ali"]), ("age", ["42"]), ("ratio", ["1.5"]), ("remember", ["on"]), ("tags", ["a", " b ", ""]),
            ("__action", ["submit"]), ("__RequestVerificationToken", ["x"]), ("_handler", ["journey-submit"]), ("unknown", ["y"])));

        input.ToJsonString().Should().Be("""{"userName":"ali","age":42,"ratio":1.5,"remember":true,"tags":["a","b"]}""");
    }

    [Fact]
    public void Unchecked_booleans_are_false_and_empty_values_are_omitted()
    {
        var schema = Schema("""{"properties":{"code":{"type":"string"},"remember":{"type":"boolean"}},"required":["code"]}""");

        var input = JourneyInputMapper.Map(schema, Form(("code", [""])));

        input.ToJsonString().Should().Be("""{"remember":false}""");
    }

    [Fact]
    public void Unparsable_numbers_stay_strings_for_the_journey_validation()
    {
        var input = JourneyInputMapper.Map(Schema("""{"properties":{"age":{"type":["integer","null"]}}}"""), Form(("age", ["abc"])));

        input["age"]!.GetValue<string>().Should().Be("abc");
    }

    [Fact]
    public void Passwords_keep_their_exact_value()
    {
        var input = JourneyInputMapper.Map(Schema("""{"properties":{"password":{"type":"string"}}}"""), Form(("password", ["  p a s s  "])));

        input["password"]!.GetValue<string>().Should().Be("  p a s s  ");
    }

    [Fact]
    public void Without_a_schema_all_non_reserved_fields_are_strings()
    {
        var input = JourneyInputMapper.Map(null, Form(("a", ["1"]), ("confirmPassword", ["x"]), ("__action", ["go"])));

        input.ToJsonString().Should().Be("""{"a":"1"}""");
    }

    [Fact]
    public void The_clicked_button_overrides_the_hidden_action()
    {
        JourneyInputMapper.ActionOf(Form(("__action", ["approve", "deny"]))).Should().Be("deny");
        JourneyInputMapper.ActionOf(Form(("__action", ["approve"]))).Should().Be("approve");
        JourneyInputMapper.ActionOf(Form(("other", ["x"]))).Should().BeNull();
    }

    [Fact]
    public void Safe_values_never_contain_secrets()
    {
        var values = JourneyInputMapper.SafeValues(Form(
            ("userName", ["ali"]), ("email", ["a@b.c"]), ("password", ["p"]), ("confirmPassword", ["p"]),
            ("newPassword", ["p"]), ("code", ["123456"]), ("totpCode", ["1"]), ("clientSecret", ["s"]), ("__RequestVerificationToken", ["t"])));

        values.Should().BeEquivalentTo(new Dictionary<string, string> { ["userName"] = "ali", ["email"] = "a@b.c" });
    }

    [Theory]
    [InlineData("password", """{"type":"string"}""", true)]
    [InlineData("code", """{"type":"string"}""", true)]
    [InlineData("pin", """{"type":"string","x-sensitive":true}""", true)]
    [InlineData("answer", """{"type":"string","writeOnly":true}""", true)]
    [InlineData("answer", """{"type":"string","format":"password"}""", true)]
    [InlineData("userName", """{"type":"string"}""", false)]
    public void Sensitive_fields_are_detected(string name, string property, bool sensitive) =>
        JourneyInputMapper.IsSensitive(name, Schema(property)).Should().Be(sensitive);

    [Fact]
    public void Fields_describe_inputs_with_autocomplete_hints()
    {
        var fields = JourneyInputMapper.Fields(Schema("""
            {"properties":{
              "userName":{"type":"string","title":"Kullanıcı adı","maxLength":50},
              "password":{"type":"string"},
              "code":{"type":"string"},
              "email":{"type":"string","format":"email"},
              "accept":{"type":"boolean"},
              "plan":{"type":"string","enum":["a","b"]},
              "count":{"type":"integer"}},
             "required":["userName","password"]}
            """));

        fields.Select(f => (f.Name, f.Kind, f.Required, f.Autocomplete)).Should().Equal(
            ("userName", JourneyFieldKind.Text, true, "username"),
            ("password", JourneyFieldKind.Password, true, "current-password"),
            ("code", JourneyFieldKind.Code, false, "one-time-code"),
            ("email", JourneyFieldKind.Email, false, "email"),
            ("accept", JourneyFieldKind.Boolean, false, "off"),
            ("plan", JourneyFieldKind.Enum, false, "off"),
            ("count", JourneyFieldKind.WholeNumber, false, "off"));
        fields[0].Title.Should().Be("Kullanıcı adı");
        fields[0].MaxLength.Should().Be(50);
        fields[5].EnumValues.Should().Equal("a", "b");
        JourneyInputMapper.Fields(null).Should().BeEmpty();
    }
}
