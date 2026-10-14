using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Middt.Framework.Blazor.Web.Ui.Components;
using Middt.Framework.Blazor.Web.Ui.Rendering;
using Middt.Framework.Tests.Ui;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Tests.Blazor;

/// <summary>Widgets are not re-rendered when a parent re-renders with the same node and render context.</summary>
public sealed class UiWidgetRenderingTests
{
    public sealed class CountingWidget : UiWidgetBase
    {
        private static int renders;

        public static int Renders => Volatile.Read(ref renders);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Interlocked.Increment(ref renders);
            builder.AddContent(0, Text("text"));
        }
    }

    /// <summary>Parent that re-renders on demand (like a workflow screen re-rendering around an embedded layout).</summary>
    public sealed class Parent : ComponentBase
    {
        [Parameter]
        public UiNode Node { get; set; } = default!;

        [Parameter]
        public UiRenderContext Context { get; set; } = default!;

        [Parameter]
        public Action<Parent>? Created { get; set; }

        protected override void OnInitialized() => Created?.Invoke(this);

        public void Rerender(UiRenderContext? context = null)
        {
            Context = context ?? Context;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<UiNodeView>(0);
            builder.AddComponentParameter(1, nameof(UiNodeView.Node), Node);
            builder.AddComponentParameter(2, nameof(UiNodeView.Context), Context);
            builder.CloseComponent();
        }
    }

    [Fact]
    public async Task Same_node_and_context_skip_the_widget_render_a_new_context_does_not()
    {
        var widgets = UiBlazorWidgets.CreateDefault().Add<CountingWidget>(new UiWidgetSpec("counting", "Counting") { Props = [new("text", UiPropType.Template)] });
        await using var services = new ServiceCollection().AddLogging().AddSingleton(widgets).BuildServiceProvider();
        var layout = UiRenderInput.Layout("""[ { "widget": "counting", "text": "n={{ data.n }}" } ]""");
        UiRenderContext Context(int n) => new(widgets.Catalog, UiRenderInput.User, UiRenderInput.Variables($$"""{ "n": {{n}} }"""), interactive: true) { Culture = CultureInfo.InvariantCulture };

        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        Parent? parent = null;
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<Parent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Parent.Node)] = layout.Body[0],
                [nameof(Parent.Context)] = Context(1),
                [nameof(Parent.Created)] = (Action<Parent>)(p => parent = p),
            }));
            return output;
        });

        var before = CountingWidget.Renders;
        await renderer.Dispatcher.InvokeAsync(() => parent!.Rerender());
        await renderer.Dispatcher.InvokeAsync(() => parent!.Rerender());
        CountingWidget.Renders.Should().Be(before, "node and context are the same instances");

        await renderer.Dispatcher.InvokeAsync(() => parent!.Rerender(Context(2)));
        CountingWidget.Renders.Should().Be(before + 1);
        (await renderer.Dispatcher.InvokeAsync(html.ToHtmlString)).Should().Be("n=2");
    }
}
