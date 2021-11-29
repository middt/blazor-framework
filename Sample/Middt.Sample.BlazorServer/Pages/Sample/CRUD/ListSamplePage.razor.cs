using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Template.Api.Model.Database;
using Middt.Template.Common.Service;
using System.Collections.Generic;

namespace Middt.Template.BlazorServer.Pages.Sample.CRUD
{
    [Authorize]
    public partial class ListSamplePage : BaseListPageCode<Table1SecureService, ITable1SecureService, Table1Secure>
    {
        [Inject]
        public Table1SecureService table1SecureService { get; set; }

        private bool ExcelDisable = true;

        public override void Search()
        {
            SearchRequestModel.RequestItemSize = 20;

            base.Search();
            ExcelDisable = false;
        }
        private void ExcelGizle()
        {
            ExcelDisable = true;
        }
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                BaseResponseDataModel<List<Table1Secure>> rr = table1SecureService.GetAll();
            }
        }
    }
}
