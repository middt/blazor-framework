using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.BlazorServer.Pages.Bike.PopUp;
using Middt.Sample.Common.Service;
using Radzen.Blazor;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Middt.Sample.BlazorServer.Pages.Bike
{
    public partial class ViewOrderPage : BaseListPageCode<ViewOrderService, IViewOrderService, ViewOrder>
    {
        public List<Store> ListStore { get; set; }

        [Inject]
        public StoreService StoreService { get; set; }


        public List<Staff> ListStaff { get; set; }

        [Inject]
        public StaffService StaffService { get; set; }

        public PopCustomerSelect popCustomerSelect { get; set; }

        public RadzenTextBox txtCustomerName { get; set; }

        public RadzenNumeric<int?> txtCustomerID { get; set; }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                await LoadStore();
                await LoadStaff();

                popCustomerSelect.OnSelect = EventCallback.Factory.Create<Customer>(this, OnSelect);
            }
        }
        private async Task LoadStore()
        {
            BaseResponseDataModel<List<Store>> response = await StoreService.GetAll();

            if (response.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                ListStore = response.Data;
            }
            else
            {
                Notification.ShowErrorMessage("Error", response.ErrorText);
            }
        }
        private async Task LoadStaff()
        {
            BaseResponseDataModel<List<Staff>> response = await StaffService.GetAll();

            if (response.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                ListStaff = response.Data;
            }
            else
            {
                Notification.ShowErrorMessage("Error", response.ErrorText);
            }
        }


        protected async Task OpenCustomerModel()
        {
           await popCustomerSelect.Open();
        }
        protected async Task OnSelect(Customer customer)
        {
            SearchRequestModel.RequestModel.CustomerId = customer.CustomerId;
            txtCustomerName.Value = customer.Name;

            StateHasChanged();
        }
        protected async Task OnChange(string value, string name)
        {
           // Added radzen blazor bug
        }

    }
}
