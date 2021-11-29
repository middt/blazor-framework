using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Suges.Framework.Blazor.Web.Base.Page;
using Suges.Framework.Common.Model.Data;
using Suges.Template.Api.Model.Database;
using Suges.Template.Common.Service;
using System.Collections.Generic;

namespace Suges.Template.BlazorServer.Pages.Sample.CRUD
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
