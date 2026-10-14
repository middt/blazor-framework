using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>List page rendered with a server side paged/sorted <c>RadzenDataGrid</c>; columns go in <c>ListContent</c>.</summary>
public partial class BaseRadzenListPage<TService, TModel>
{
    /// <summary>Optional expandable row template.</summary>
    [Parameter]
    public RenderFragment<TModel>? TemplateContent { get; set; }
}
