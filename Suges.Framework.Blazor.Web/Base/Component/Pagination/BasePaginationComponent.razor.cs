using Microsoft.AspNetCore.Components;

namespace Suges.Framework.Blazor.Web.Base.Component.Pagination
{
    public partial class BasePaginationComponent : BaseComponent
    {
        public Action OnPageChange { get; set; }

        [Parameter]
        public int PageSize { get; set; } = 20;

        public long Count { get; set; }

        [Parameter]
        public int CurrentPage { get; set; } = 1;

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
            OnPageChange?.Invoke();
        }
        public void ChangeRecordSize()
        {
            CurrentPage = 1;
            OnPageChange?.Invoke();
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

        List<int> PageList;

        List<int> PageSizeList = new List<int>() { 5, 10, 20, 40, 50, 100 };

        public void CalculateTotalPage()
        {
            TotalPage = Convert.ToInt32(Math.Ceiling((decimal)Count / PageSize));

            PageList = new List<int>();
            for (int i = 1; i <= TotalPage; i++)
            {
                PageList.Add(i);
            }


            InvokeAsync(() => StateHasChanged()).Wait();
        }
    }
}
