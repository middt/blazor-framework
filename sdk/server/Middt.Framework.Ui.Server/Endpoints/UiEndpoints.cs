using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Expressions;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Schema;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Ui.Server.Runtime;
using Middt.Framework.Ui.Validation;
using Middt.Framework.Ui.Widgets;

namespace Middt.Framework.Ui.Server.Endpoints;

public static class UiEndpoints
{
    /// <summary>
    /// Maps the layout API under <paramref name="prefix"/> (every endpoint requires an authenticated user, writes require
    /// <see cref="MiddtUiOptions.AdminRole"/>):
    /// <list type="bullet">
    /// <item><c>GET /layouts[?kind=page]</c> layouts the user may open · <c>GET /layouts/{key}</c> one layout</item>
    /// <item><c>GET /layouts/{key}/html</c> static HTML (query string → <c>query</c>)</item>
    /// <item><c>GET /widgets</c> widget specs · <c>GET /schema</c> JSON Schema of layouts (editors, TypeScript types)</item>
    /// <item>admin: <c>POST /layouts/validate</c> · <c>PUT /layouts/{key}</c> (body.version = expected version, 0 for new) ·
    /// <c>DELETE /layouts/{key}[?version=]</c> · <c>GET /layouts/{key}/revisions</c></item>
    /// </list>
    /// </summary>
    public static RouteGroupBuilder MapMiddtUi(this IEndpointRouteBuilder endpoints, string prefix = "api/ui")
    {
        var adminRole = endpoints.ServiceProvider.GetRequiredService<IOptions<MiddtUiOptions>>().Value.AdminRole;
        var group = endpoints.MapGroup(prefix).WithTags("UI layouts").RequireAuthorization();
        var admin = group.MapGroup(string.Empty).RequireAuthorization(policy => policy.RequireRole(adminRole));

        group.MapGet("layouts", async (string? kind, UiLayoutStore store, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var items = await store.ListAsync(ExpressionUser.FromPrincipal(principal), kind, ct);
            return BaseResponseDataModel<List<UiLayoutSummary>>.Success([.. items], items.Count);
        }).WithName("ListUiLayouts");

        group.MapGet("layouts/{key}", async (string key, UiLayoutStore store, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var layout = await store.FindAsync(key, ct);
            if (layout is null)
            {
                return NotFound(key);
            }

            return store.CanOpen(layout, ExpressionUser.FromPrincipal(principal))
                ? Results.Ok(BaseResponseDataModel<UiLayoutDocument>.Success(layout, 1))
                : Forbidden(key);
        }).WithName("GetUiLayout");

        group.MapGet("layouts/{key}/html", async (string key, HttpContext http, UiLayoutStore store, UiHtmlRenderer renderer, CancellationToken ct) =>
        {
            var layout = await store.FindAsync(key, ct);
            if (layout is null)
            {
                return NotFound(key);
            }

            var user = ExpressionUser.FromPrincipal(http.User);
            if (!store.CanOpen(layout, user))
            {
                return Forbidden(key);
            }

            var query = new JsonObject();
            foreach (var (name, value) in http.Request.Query)
            {
                query[name] = value.ToString();
            }

            var html = renderer.Render(layout, new Dictionary<string, JsonNode?> { [UiExpressions.Query] = query }, user);
            return Results.Content(html, "text/html; charset=utf-8");
        }).WithName("RenderUiLayout");

        group.MapGet("widgets", (UiWidgetCatalog catalog) =>
        {
            var items = catalog.Specs.OrderBy(s => s.Name, StringComparer.Ordinal).Select(UiWidgetSpecModel.From).ToList();
            return BaseResponseDataModel<List<UiWidgetSpecModel>>.Success(items, items.Count);
        }).WithName("ListUiWidgets");

        group.MapGet("schema", (UiWidgetCatalog catalog) => Results.Text(SchemaJson(catalog), "application/schema+json"))
            .WithName("GetUiLayoutSchema");

        admin.MapPost("layouts/validate", (UiLayoutDocument layout, UiLayoutStore store) =>
            BaseResponseDataModel<UiValidationResult>.Success(store.Validate(layout), 1))
            .WithName("ValidateUiLayout");

        admin.MapPut("layouts/{key}", async (string key, UiLayoutDocument layout, UiLayoutStore store, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!string.Equals(key, layout.Key, StringComparison.Ordinal))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The route key and the layout key differ.");
            }

            try
            {
                var expected = layout.Version > 0 ? layout.Version : (int?)null;
                var saved = await store.SaveAsync(layout, expected, principal.Identity?.Name, ct);
                return Results.Ok(BaseResponseDataModel<UiLayoutDocument>.Success(saved, 1));
            }
            catch (UiLayoutValidationException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["layout"] = [.. ex.Errors] }, title: "The layout is invalid.");
            }
            catch (UiLayoutConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message);
            }
        }).WithName("SaveUiLayout");

        admin.MapDelete("layouts/{key}", async (string key, int? version, UiLayoutStore store, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            try
            {
                return await store.DeleteAsync(key, version, principal.Identity?.Name, ct)
                    ? Results.Ok(BaseResponseModel.Success())
                    : NotFound(key);
            }
            catch (UiLayoutConflictException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message);
            }
        }).WithName("DeleteUiLayout");

        admin.MapGet("layouts/{key}/revisions", async (string key, UiLayoutStore store, CancellationToken ct) =>
        {
            var items = await store.RevisionsAsync(key, ct);
            return BaseResponseDataModel<List<UiLayoutRevisionModel>>.Success([.. items], items.Count);
        }).WithName("ListUiLayoutRevisions");

        return group;
    }

    // The schema only depends on the widget catalog: generate it once per catalog (again if widgets were added later).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiWidgetCatalog, Tuple<int, string>> SchemaCache = new();

    private static string SchemaJson(UiWidgetCatalog catalog)
    {
        var count = catalog.Specs.Count;
        if (SchemaCache.TryGetValue(catalog, out var cached) && cached.Item1 == count)
        {
            return cached.Item2;
        }

        var json = UiLayoutSchema.GenerateJson(catalog);
        SchemaCache.AddOrUpdate(catalog, Tuple.Create(count, json));
        return json;
    }

    private static IResult NotFound(string key) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"Layout '{key}' does not exist.");

    private static IResult Forbidden(string key) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: $"You are not allowed to open layout '{key}'.");
}
