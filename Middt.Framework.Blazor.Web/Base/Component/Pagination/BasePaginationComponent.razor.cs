using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.Pagination
{
    public partial class BasePaginationComponent : BaseComponent
    {
        public EventCallback? OnPageChange { get; set; }

        [Parameter]
        public int PageSize { get; set; } = 20;

        public long Count { get; set; }

        public int CurrentPage { get; set; } = 1;

        public int TotalPage { get; set; }

        public async Task GotoPage(int page)
        {
            if (page < 1)
            {
                CurrentPage = 1;
            }
            else
            {
                CurrentPage = page;
            }

            if (OnPageChange != null)
                await OnPageChange?.InvokeAsync();
        }
        public async Task ChangeRecordSize()
        {
            CurrentPage = 1;
            if (OnPageChange != null)
                await OnPageChange?.InvokeAsync();
        }


        public async Task Next()
        {
            CurrentPage++;
            await GotoPage(CurrentPage);
        }

        public async Task Previous()
        {
            CurrentPage--;
            await GotoPage(CurrentPage);
        }

        List<int> PageList;

        List<int> PageSizeList = new List<int>() { 5, 10, 20, 40, 50, 100, 1000 };

        public async Task CalculateTotalPage()
        {
            TotalPage = Convert.ToInt32(Math.Ceiling((decimal)Count / PageSize));

            PageList = new List<int>();
            for (int i = 1; i <= TotalPage; i++)
            {
                PageList.Add(i);
            }

            await InvokeAsync(() => StateHasChanged());
        }
    }
}
