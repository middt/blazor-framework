using Microsoft.AspNetCore.Components;

namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>CRUD page rendered with a server side paged/sorted <c>RadzenDataGrid</c>.</summary>
public partial class BaseRadzenListCrudPage<TService, TModel>
{
    /// <summary>Optional expandable row template.</summary>
    [Parameter]
    public RenderFragment<TModel>? TemplateContent { get; set; }
}
