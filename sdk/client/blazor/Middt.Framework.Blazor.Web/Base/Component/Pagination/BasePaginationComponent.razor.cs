using Microsoft.AspNetCore.Components;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Component.Pagination;

/// <summary>Pager for list pages that do not use a Radzen data grid.</summary>
public partial class BasePaginationComponent : BaseComponent
{
    private static readonly int[] PageSizeOptions = [5, 10, 20, 40, 50, 100, 1000];

    [Parameter]
    public int PageSize { get; set; } = 20;

    [Parameter]
    public EventCallback OnPageChange { get; set; }

    public int Count { get; set; }

    public int CurrentPage { get; set; } = 1;

    public int TotalPage => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)Count / PageSize);

    public Task GotoPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, Math.Max(TotalPage, 1));
        return OnPageChange.InvokeAsync();
    }

    /// <summary>Re-renders the pager after <see cref="Count"/> changed.</summary>
    public Task CalculateTotalPage() => InvokeAsync(StateHasChanged);

    private Task HandlePageChanged(PagerEventArgs args)
    {
        PageSize = args.Top;
        return GotoPage(args.PageIndex + 1);
    }
}
