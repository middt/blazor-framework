using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Middt.Framework.Expressions;
using Middt.Framework.Expressions.Portable;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Server.Rendering;

/// <summary>Writes the HTML of one (custom) widget for <see cref="UiHtmlRenderer"/>.</summary>
/// <param name="node">The widget node.</param>
/// <param name="context">Variables and formatting.</param>
/// <param name="html">Output; use <see cref="UiHtmlBuilder.Text"/> for every value (it HTML encodes).</param>
/// <param name="children">Renders child nodes (optionally with another context).</param>
public delegate void UiHtmlWidget(UiNode node, UiRenderContext context, UiHtmlBuilder html, Action<IEnumerable<UiNode>?, UiRenderContext> children);

/// <summary>HTML output with encoding helpers.</summary>
public sealed class UiHtmlBuilder
{
    private readonly StringBuilder sb = new();
    private int ids;

    /// <summary>Appends trusted markup (tags written by the renderer, never data).</summary>
    public UiHtmlBuilder Raw(string markup)
    {
        sb.Append(markup);
        return this;
    }

    /// <summary>Appends HTML encoded text.</summary>
    public UiHtmlBuilder Text(string? text)
    {
        sb.Append(HtmlEncoder.Default.Encode(text ?? string.Empty));
        return this;
    }

    /// <summary>HTML encoded attribute value (quotes are encoded too).</summary>
    public static string Attribute(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);

    /// <summary>A unique element id within this document.</summary>
    public string NewId(string prefix) => $"{prefix}{(++ids).ToString(CultureInfo.InvariantCulture)}";

    public override string ToString() => sb.ToString();
}

/// <summary>
/// Static HTML renderer of layouts (e-mails, PDFs, non-interactive clients, workflow <c>…/view</c>) in plain C# – no
/// Razor/Blazor dependency, so the backend stays frontend independent. It writes the same markup and CSS classes
/// (<c>middt-ui-*</c>) as the Blazor display widgets; interactive widgets render a placeholder. Custom widgets:
/// <see cref="MiddtUiOptions.AddWidget"/> / <see cref="MiddtUiOptions.AddHtmlWidget"/>.
/// </summary>
public sealed class UiHtmlRenderer(UiWidgetCatalog catalog, IOptions<MiddtUiOptions> options)
{
    private const int MaxListItems = 1000;

    public string Render(UiLayoutDocument layout, IReadOnlyDictionary<string, JsonNode?> variables, ExpressionUser user, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var context = new UiRenderContext(catalog, user, variables, interactive: false) { Culture = culture ?? CultureInfo.CurrentCulture };
        var html = new UiHtmlBuilder();
        html.Raw($"<div class=\"middt-ui\" data-layout=\"{UiHtmlBuilder.Attribute(layout.Key)}\">");
        Nodes(layout.Body, context, html);
        html.Raw("</div>");
        return html.ToString();
    }

    private void Nodes(IEnumerable<UiNode>? nodes, UiRenderContext context, UiHtmlBuilder html)
    {
        foreach (var node in nodes ?? [])
        {
            Node(node, context, html);
        }
    }

    private void Node(UiNode node, UiRenderContext context, UiHtmlBuilder html)
    {
        if (!context.IsVisible(node))
        {
            return;
        }

        var spec = catalog.Find(node.Widget);
        if (spec is null)
        {
            html.Raw("<div class=\"middt-ui-error\" role=\"alert\">").Text($"Bilinmeyen bileşen: {node.Widget}").Raw("</div>");
            return;
        }

        if (options.Value.HtmlWidgets.TryGetValue(node.Widget, out var custom))
        {
            custom(node, context, html, (children, childContext) => Nodes(children, childContext, html));
            return;
        }

        if (spec.Interactive)
        {
            var title = node.StringProp("title");
            html.Raw("<div class=\"middt-ui-placeholder\">").Text(title is null ? spec.Description : context.Text(title)).Raw("</div>");
            return;
        }

        switch (node.Widget)
        {
            case "heading":
                var level = Math.Clamp(node.IntProp("level", 2), 1, 6).ToString(CultureInfo.InvariantCulture);
                html.Raw($"<h{level} class=\"middt-ui-heading\">").Text(Text(node, context, "text")).Raw($"</h{level}>");
                break;
            case "text":
                html.Raw($"<p class=\"middt-ui-text middt-ui-text-{Attr(node.StringProp("style") ?? "normal")}\">").Text(Text(node, context, "text")).Raw("</p>");
                break;
            case "alert":
                var severity = node.StringProp("severity") ?? "info";
                html.Raw($"<div class=\"middt-ui-alert middt-ui-alert-{Attr(severity)}\" role=\"{(severity is "danger" or "warning" ? "alert" : "status")}\">")
                    .Text(Text(node, context, "text")).Raw("</div>");
                break;
            case "badge":
                html.Raw($"<span class=\"middt-ui-badge middt-ui-badge-{Attr(node.StringProp("color") ?? "secondary")}\">").Text(Text(node, context, "text")).Raw("</span>");
                break;
            case "divider":
                html.Raw("<hr class=\"middt-ui-divider\" />");
                break;
            case "fields":
                Fields(node, context, html);
                break;
            case "section":
                Section(node, context, html);
                break;
            case "columns":
                html.Raw("<div class=\"middt-ui-columns\">");
                var widths = node.ArrayProp("widths");
                for (var i = 0; i < (node.Children?.Count ?? 0); i++)
                {
                    html.Raw($"<div class=\"middt-ui-column\" style=\"flex: {Width(widths, i)} 1 0\">");
                    Node(node.Children![i], context, html);
                    html.Raw("</div>");
                }

                html.Raw("</div>");
                break;
            case "list":
                List(node, context, html);
                break;
            case "link":
                Link(node, context, html);
                break;
            default:
                html.Raw("<div class=\"middt-ui-error\" role=\"alert\">").Text($"'{node.Widget}' bileşeni için HTML çıktısı tanımlı değil.").Raw("</div>");
                break;
        }
    }

    private static void Fields(UiNode node, UiRenderContext context, UiHtmlBuilder html)
    {
        html.Raw("<dl class=\"middt-ui-fields\">");
        foreach (var item in (node.ArrayProp("items") ?? []).OfType<JsonObject>())
        {
            if (!context.IsVisible(UiFieldJson.GetString(item, "visibleWhen")))
            {
                continue;
            }

            html.Raw("<div class=\"middt-ui-field\"><dt>").Text(UiFieldJson.GetString(item, "label"))
                .Raw("</dt><dd>").Text(context.Text(UiFieldJson.GetString(item, "value"), UiFieldJson.GetString(item, "format"))).Raw("</dd></div>");
        }

        html.Raw("</dl>");
    }

    private void Section(UiNode node, UiRenderContext context, UiHtmlBuilder html)
    {
        if (node.StringProp("title") is null)
        {
            html.Raw("<section class=\"middt-ui-section\">");
        }
        else
        {
            var titleId = $"{(node.Id is null ? html.NewId("ui-section") : $"ui-{node.Id}")}-title";
            html.Raw($"<section class=\"middt-ui-section\" aria-labelledby=\"{titleId}\">")
                .Raw($"<h3 class=\"middt-ui-section-title\" id=\"{titleId}\">").Text(Text(node, context, "title")).Raw("</h3>");
        }

        Nodes(node.Children, context, html);
        html.Raw("</section>");
    }

    private void List(UiNode node, UiRenderContext context, UiHtmlBuilder html)
    {
        JsonArray? items;
        try
        {
            items = node.StringProp("source") is { } source
                ? UiExpressions.Evaluator.EvaluateToNode(source, context.ToExpressionContext()) as JsonArray
                : null;
        }
        catch (PortableExpressionException)
        {
            items = null;
        }

        if (items is not { Count: > 0 })
        {
            html.Raw("<p class=\"middt-ui-text middt-ui-text-muted\">").Text(node.StringProp("empty") ?? "Kayıt yok.").Raw("</p>");
            return;
        }

        html.Raw("<ul class=\"middt-ui-list\">");
        foreach (var item in items.Take(MaxListItems))
        {
            html.Raw("<li>");
            Nodes(node.Children, context.With(UiExpressions.Row, item?.DeepClone()), html);
            html.Raw("</li>");
        }

        html.Raw("</ul>");
    }

    private static void Link(UiNode node, UiRenderContext context, UiHtmlBuilder html)
    {
        var href = Text(node, context, "href");
        if (UiUrl.IsSafe(href))
        {
            var css = node.StringProp("style") == "button" ? "middt-ui-button" : "middt-ui-link";
            html.Raw($"<a class=\"{css}\" href=\"{UiHtmlBuilder.Attribute(href)}\">").Text(Text(node, context, "text")).Raw("</a>");
        }
        else
        {
            html.Raw("<span class=\"middt-ui-link-disabled\">").Text(Text(node, context, "text")).Raw("</span>");
        }
    }

    private static string Text(UiNode node, UiRenderContext context, string prop) => context.Text(node.StringProp(prop));

    // Enum values were validated against the spec; encoded anyway.
    private static string Attr(string value) => UiHtmlBuilder.Attribute(value);

    private static string Width(JsonArray? widths, int index) =>
        widths is not null && index < widths.Count && widths[index] is JsonValue v
            && decimal.TryParse(v.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var width) && width > 0
            ? width.ToString(CultureInfo.InvariantCulture)
            : "1";
}
