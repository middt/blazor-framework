using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.BlazorServer.Pages.Bike.PopUp;
using Middt.Sample.Common.Service;
using Radzen.Blazor;
using System.Collections.Generic;

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

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                LoadStore();
                LoadStaff();

                popCustomerSelect.OnSelect += OnSelect;
            }
        }
        private void LoadStore()
        {
            BaseResponseDataModel<List<Store>> response = StoreService.GetAll();

            if (response.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                ListStore = response.Data;
            }
            else
            {
                Notification.ShowErrorMessage("Error", response.ErrorText);
            }
        }
        private void LoadStaff()
        {
            BaseResponseDataModel<List<Staff>> response = StaffService.GetAll();

            if (response.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                ListStaff = response.Data;
            }
            else
            {
                Notification.ShowErrorMessage("Error", response.ErrorText);
            }
        }


        protected void OpenCustomerModel()
        {
            popCustomerSelect.Open();
        }
        protected void OnSelect(Customer customer)
        {
            SearchRequestModel.RequestModel.CustomerId = customer.CustomerId;
            txtCustomerName.Value = customer.Name;

            StateHasChanged();
        }
        void OnChange(string value, string name)
        {
           // Added radzen blazor bug
        }






        //public async Task WordTemplate()
        //{
        //    ExecuteMethod(() =>
        //    {
        //        Table1Secure model = new Table1Secure();

        //        BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
        //        baseModel.RequestModel = model;
        //        BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuWordTemplate(baseModel);

        //        jsRuntime.SaveAsFileAsync("Sample-Word.docx", resp.Data, "application/ms-word");
        //    });
        //}

        //public async Task WordToPdfTemplate()
        //{
        //    ExecuteMethod(() =>
        //    {
        //        Table1Secure model = new Table1Secure();

        //        BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
        //        baseModel.RequestModel = model;
        //        BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuWordToPdfTemplate(baseModel);
        //        jsRuntime.SaveAsFileAsync("Sample-WordToPDF.pdf", resp.Data, "application/pdf"); ;
        //    });
        //}

        //public async Task ExcelTemplate()
        //{
        //    ExecuteMethod(() =>
        //    {
        //        Table1Secure model = new Table1Secure();

        //        BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
        //        baseModel.RequestModel = model;
        //        BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuExcelTemplate(baseModel);

        //        jsRuntime.SaveAsFileAsync("Sample-Excel.xlsx", resp.Data, "application/vdn.ms-excel");
        //    });
        //}

    }
}
