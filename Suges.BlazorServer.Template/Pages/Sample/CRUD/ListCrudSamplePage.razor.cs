using Microsoft.AspNetCore.Authorization;
using Suges.Framework.Blazor.Web.Base.Page;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Model;
using Suges.Template.Api.Model.Database;
using Suges.Template.Common.Service;
using System.Collections.Generic;

namespace Suges.Template.BlazorServer.Pages.Sample.CRUD
{
    [Authorize]
    public partial class ListCrudSamplePage : BaseListCrudPageCode<Table1SecureService, ITable1SecureService, Table1Secure>
    {
        public override void Search()
        {
            baseListCrudPage.SearchRequestModel = new BaseSearchRequestModel<Table1Secure>();
            baseListCrudPage.SearchRequestModel.RequestItemSize = 3;
            baseListCrudPage.SearchRequestModel.RequestModel.Table1Id = 0;
            base.Search();
        }
        public int MainListID { get; set; }

        public int SubListID { get; set; }
        public List<TextValueItem> MainList { get; set; }
        public List<TextValueItem> SubList { get; set; }

        public bool disableProjeAltTipi = false;


        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                MainList = new List<TextValueItem>();
                for (int i = 1; i < 5; i++)
                {
                    MainList.Add(new TextValueItem() { Value = i, Text = "Text " + i.ToString() });
                }
            }
        }

        public override void OnAfterModalClose()
        {
            base.OnAfterModalClose();
            SubList = null;
        }
        public void ProjeTipChange()
        {
            SubList = new List<TextValueItem>();
            ExecuteMethod(() =>
            {
                for (int i = 1; i < 5; i++)
                {
                    SubList.Add(new TextValueItem() { Value = i, Text = "Text " + i.ToString() });
                }
            });
        }
    }


}
