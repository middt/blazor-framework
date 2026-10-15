using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Radzen;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Blazor.Web.Base.Component.Workflow;
using Middt.Framework.Blazor.Web.Ui;
using Middt.Framework.Client.Http;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Tests.Client;

namespace Middt.Framework.Tests.Blazor;

/// <summary><see cref="JsonSchemaForm"/> rendered with <see cref="HtmlRenderer"/>: <c>x-lookup</c> selects.</summary>
public sealed class JsonSchemaFormTests
{
    private const string Schema = """
        {
          "type": "object",
          "required": ["customerId"],
          "properties": {
            "customerId": { "type": "integer", "title": "Müşteri", "x-lookup": { "entity": "Customer", "valueField": "customerId", "textField": "email" } },
            "storeCode": { "type": "string", "title": "Mağaza", "x-lookup": { "entity": "Store", "valueField": "code", "textField": "name" } },
            "note": { "type": "string", "title": "Not" }
          }
        }
        """;

    /// <summary>Data client answering <c>getall</c> from a dictionary (missing entity = API error).</summary>
    private sealed class FakeDataClient(Dictionary<string, List<JsonObject>> items) : UiDataClient(
        new HttpClient(), new FakeSessionState("token"), new FakeTokenService(_ => new TokenResponseModel()),
        Options.Create(new ApiSettings { URL = "http://localhost", Version = "1" }), NullLogger<UiDataClient>.Instance)
    {
        public List<string> Calls { get; } = [];

        public override Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(string entity, CancellationToken cancellationToken = default)
        {
            Calls.Add(entity);
            return Task.FromResult(items.TryGetValue(entity, out var list)
                ? BaseResponseDataModel<List<JsonObject>>.Success([.. list.Select(i => (JsonObject)i.DeepClone())])
                : BaseResponseDataModel<List<JsonObject>>.Failure("boom"));
        }
    }

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    /// <summary>Hosts the form and hands out the instance (for <see cref="JsonSchemaForm.GetValue"/> / <see cref="JsonSchemaForm.Validate"/>).</summary>
    public sealed class Host : ComponentBase
    {
        [Parameter]
        public JsonObject? Schema { get; set; }

        [Parameter]
        public JsonObject? Value { get; set; }

        [Parameter]
        public Action<JsonSchemaForm>? Captured { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<JsonSchemaForm>(0);
            builder.AddComponentParameter(1, nameof(JsonSchemaForm.Schema), Schema);
            builder.AddComponentParameter(2, nameof(JsonSchemaForm.Value), Value);
            builder.AddComponentParameter(3, nameof(JsonSchemaForm.IdPrefix), "t");
            builder.AddComponentReferenceCapture(4, form => Captured?.Invoke((JsonSchemaForm)form));
            builder.CloseComponent();
        }
    }

    private static Dictionary<string, List<JsonObject>> Items() => new(StringComparer.Ordinal)
    {
        ["Customer"] = [new() { ["customerId"] = 1, ["email"] = "ada@test.com" }, new() { ["customerId"] = 2, ["email"] = "bob@test.com" }],
        ["Store"] = [new() { ["code"] = "IST", ["name"] = "İstanbul" }],
    };

    private sealed class Rendered(HtmlRenderer renderer, ServiceProvider services, HtmlRootComponent root, JsonSchemaForm form) : IAsyncDisposable
    {
        public JsonSchemaForm Form => form;

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));

        public Task<T> InvokeAsync<T>(Func<T> action) => renderer.Dispatcher.InvokeAsync(action);

        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private static async Task<Rendered> RenderAsync(Dictionary<string, List<JsonObject>> items, string? value = null, bool registerCache = true, string schema = Schema)
    {
        var collection = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NullJsRuntime>().AddSingleton(TimeProvider.System);
        collection.AddRadzenComponents();
        collection.AddSingleton<UiDataClient>(new FakeDataClient(items));
        if (registerCache)
        {
            collection.AddScoped<UiLookupCache>();
        }

        var services = collection.BuildServiceProvider();
        var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        JsonSchemaForm? form = null;
        var root = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Host.Schema)] = JsonNode.Parse(schema)!.AsObject(),
                [nameof(Host.Value)] = value is null ? null : JsonNode.Parse(value)!.AsObject(),
                [nameof(Host.Captured)] = (Action<JsonSchemaForm>)(f => form = f),
            }));
            await output.QuiescenceTask;
            return output;
        });
        return new Rendered(renderer, services, root, form!);
    }

    [Fact]
    public async Task Lookup_properties_render_selects_with_the_entity_items()
    {
        await using var rendered = await RenderAsync(Items());
        var html = await rendered.HtmlAsync();

        html.Should().Contain("data-lookup=\"Customer\"").And.Contain("data-lookup=\"Store\"");
        html.Should().Contain("ada@test.com").And.Contain("bob@test.com").And.Contain("İstanbul");
        html.Should().Contain("name=\"t-customerId\"").And.Contain("aria-label=\"Müşteri\"");
        html.Should().NotContain(JsonSchemaForm.LookupLoadError);

        // a plain property has no data-lookup
        html.Split("middt-jsf-field").Count(part => part.Contains("data-lookup", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task Prefilled_values_are_kept_and_written_with_the_property_type()
    {
        await using var rendered = await RenderAsync(Items(), """{ "customerId": 2, "storeCode": "IST", "note": "x" }""");

        var value = await rendered.InvokeAsync(rendered.Form.GetValue);
        value["customerId"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        value["customerId"]!.GetValue<long>().Should().Be(2);
        value["storeCode"]!.GetValue<string>().Should().Be("IST");
        (await rendered.InvokeAsync(rendered.Form.Validate)).Should().BeTrue();
        (await rendered.HtmlAsync()).Should().Contain("bob@test.com");
    }

    [Fact]
    public async Task Prefilled_values_missing_from_the_items_stay_selectable()
    {
        await using var rendered = await RenderAsync(Items(), """{ "customerId": 99 }""");

        (await rendered.HtmlAsync()).Should().Contain("99");
        (await rendered.InvokeAsync(rendered.Form.GetValue))["customerId"]!.ToJsonString().Should().Be("99");
    }

    [Fact]
    public async Task Url_prefill_selects_the_lookup_and_keeps_the_type()
    {
        // ?start=1&key=c-2&customerId=2&note=hi → the start dialog form shows the selection; the user still submits
        var prefill = WorkflowUi.StartPrefill(JsonNode.Parse(Schema)!.AsObject(),
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["start"] = "1", ["key"] = "c-2", ["customerId"] = "2", ["note"] = "hi", ["x"] = "y" });
        await using var rendered = await RenderAsync(Items(), prefill!.ToJsonString());

        (await rendered.HtmlAsync()).Should().Contain("bob@test.com");
        (await rendered.InvokeAsync(rendered.Form.GetValue)).ToJsonString().Should().Be("""{"customerId":2,"note":"hi"}""");
    }

    [Fact]
    public async Task Reset_with_a_value_prefills_and_without_clears()
    {
        await using var rendered = await RenderAsync(Items());

        (await rendered.InvokeAsync(() =>
        {
            rendered.Form.Reset(new JsonObject { ["customerId"] = 1, ["storeCode"] = "IST" });
            return rendered.Form.GetValue();
        })).ToJsonString().Should().Be("""{"customerId":1,"storeCode":"IST"}""");

        (await rendered.InvokeAsync(() =>
        {
            rendered.Form.Reset();
            return rendered.Form.GetValue();
        })).Should().BeEmpty();
    }

    [Fact]
    public async Task Required_lookups_are_validated()
    {
        await using var rendered = await RenderAsync(Items());

        (await rendered.InvokeAsync(rendered.Form.Validate)).Should().BeFalse();
        var html = await rendered.HtmlAsync();
        html.Should().Contain("Bu alan zorunludur.");
        (await rendered.InvokeAsync(rendered.Form.GetValue)).Should().BeEmpty();
    }

    [Fact]
    public async Task Load_errors_are_shown_next_to_the_field()
    {
        await using var rendered = await RenderAsync(new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal) { ["Store"] = [] });
        var html = await rendered.HtmlAsync();

        html.Should().Contain("<div class=\"middt-jsf-error middt-jsf-lookup-error\" role=\"alert\">Liste yüklenemedi.</div>");
        html.Split("middt-jsf-lookup-error").Should().HaveCount(2, "only the Customer lookup failed");
    }

    [Fact]
    public async Task Without_the_lookup_cache_the_field_shows_the_load_error()
    {
        await using var rendered = await RenderAsync(Items(), registerCache: false);
        (await rendered.HtmlAsync()).Should().Contain(JsonSchemaForm.LookupLoadError);
    }

    [Fact]
    public async Task Incomplete_lookups_render_by_type()
    {
        await using var rendered = await RenderAsync(Items(), schema: """
            { "type": "object", "properties": { "n": { "type": "integer", "title": "N", "x-lookup": { "entity": "Customer" } } } }
            """);
        var html = await rendered.HtmlAsync();
        html.Should().NotContain("data-lookup");
        html.Should().NotContain("ada@test.com");
    }

    [Theory]
    [InlineData("42", "integer", "42")]
    [InlineData("4.5", "number", "4.5")]
    [InlineData("42", "number", "42")]
    [InlineData("42", "string", "\"42\"")]
    [InlineData("abc", "integer", "\"abc\"")]
    [InlineData("IST", null, "\"IST\"")]
    public void Lookup_values_follow_the_schema_type(string key, string? type, string json) =>
        WorkflowUi.LookupValue(key, type)!.ToJsonString().Should().Be(json);

    [Fact]
    public void Empty_lookup_values_are_omitted() => WorkflowUi.LookupValue(string.Empty, "integer").Should().BeNull();
}
