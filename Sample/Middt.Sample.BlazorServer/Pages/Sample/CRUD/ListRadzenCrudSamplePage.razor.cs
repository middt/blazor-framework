using Microsoft.AspNetCore.Authorization;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using Radzen.Blazor;
using System.Collections.Generic;
using System.Dynamic;

namespace Middt.Sample.BlazorServer.Pages.Sample.CRUD
{
    public class RadzenColumn
    {
        public string Property { get; set; }

        public string Title { get; set; }
    }
    [Authorize]
    public partial class ListRadzenCrudSamplePage : BaseListCrudPageCode<Table1SecureService, ITable1SecureService, Table1Secure>
    {
        public RadzenGrid<ExpandoResponse> radzenGridTest { get; set; }
        public List<RadzenColumn> radzenColumnList { get; set; }
        public override void OnAfterModalClose()
        {
            base.OnAfterModalClose();
            SubList = null;
        }

        public override void Search()
        {
            baseListCrudPage.SearchRequestModel = new BaseSearchRequestModel<Table1Secure>();
            baseListCrudPage.SearchRequestModel.RequestItemSize = 3;
            baseListCrudPage.SearchRequestModel.RequestModel.Table1Id = 0;
            base.Search();
        }
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                radzenColumnList = new List<RadzenColumn>();


                //DataTable dt = new DataTable();
                //dt.Columns.Add(nameof(Model.Ad));
                //dt.Columns.Add(nameof(Model.Ad));
                //dt.Columns.Add(nameof(Model.Ad));
                //dt.Columns.Add(nameof(Model.Ad));
                //dt.Columns.Add(nameof(Model.Ad));

            }
        }
        public override void OnAfterSearch()
        {
            base.OnAfterSearch();
            InvokeAsync(() =>
            {

                BaseResponseDataModel<List<ExpandoResponse>> baseResponseDataModel = Service.GetDataTable(baseListCrudPage.SearchRequestModel);


                radzenColumnList.Clear();

                if (baseResponseDataModel.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
                {
                    if (baseResponseDataModel.Data != null && baseResponseDataModel.Data.Count > 0)
                    {
                        //IDictionary<string, object> propertyValues = baseResponseDataModel.Data[0].properties;

                        //foreach (var property in propertyValues.Keys)
                        //{
                        //    radzenColumnList.Add(new RadzenColumn() { Property = property, Title = property });
                        //}
                    }
                }

                //StateHasChanged();
                radzenGridTest.Data = baseResponseDataModel.Data;
                StateHasChanged();
            });
        }
        public override void OnBeforeModalOpen()
        {
            base.OnBeforeModalOpen();
            if (MainList == null)
            {
                //ExecuteMethod(() =>
                //{
                MainList = new List<TextValueItem>();
                for (int i = 1; i < 5; i++)
                {
                    MainList.Add(new TextValueItem() { Value = i, Text = "Text " + i.ToString() });
                }
                //});
            }
        }


        public int MainListID { get; set; }
        public int SubListID { get; set; }

        public List<TextValueItem> MainList { get; set; }
        public List<TextValueItem> SubList { get; set; }


        public bool disableProjeAltTipi2
        {
            get
            {
                if (SubList != null && SubList.Count > 0)
                {
                    return true;
                }

                return false;
            }
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
