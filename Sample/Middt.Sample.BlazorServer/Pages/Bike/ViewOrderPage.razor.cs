using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using System.Collections.Generic;

namespace Middt.Sample.BlazorServer.Pages.Bike
{
    public partial class ViewOrderPage : BaseListPageCode<ViewOrderService, IViewOrderService, ViewOrder>
    {
        public List<Store> ListStore { get; set; }

        [Inject]
        public StoreService StoreService { get; set; }
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if(firstRender)
            {
                LoadStore();
            }
        }
        private void LoadStore()
        {
            BaseResponseDataModel<List<Store>> response =  StoreService.GetAll();

            if (response.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                ListStore = response.Data;
            }
            else
            {
                Notification.ShowErrorMessage("Error",response.ErrorText);
            }
        }
    }
}
