using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.Pagination
{
    public partial class BasePaginationComponent : BaseComponent
    {
        public Action<int> OnPageChange { get; set; }

        public int PageSize { get; set; }
        public long Count { get; set; }

        [Parameter]
        public int CurrentPage { get; set; }
        [Parameter]
        public int TotalPage { get; set; }

        public void GotoPage(int page)
        {
            if (page < 1)
            {
                CurrentPage = 1;
            }
            else
            {
                CurrentPage = page;

            }
            OnPageChange?.Invoke(CurrentPage);
        }

        public void Next()
        {
            CurrentPage++;
            GotoPage(CurrentPage);
        }

        public void Previous()
        {
            CurrentPage--;
            GotoPage(CurrentPage);
        }


        public void CalculateTotalPage()
        {
            TotalPage = Convert.ToInt32(Math.Ceiling((decimal)Count / PageSize));

            InvokeAsync(() => StateHasChanged()).Wait();
        }
    }
}
