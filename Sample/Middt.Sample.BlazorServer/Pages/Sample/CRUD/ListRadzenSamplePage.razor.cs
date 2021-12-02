using Microsoft.AspNetCore.Authorization;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using System;

namespace Middt.Sample.BlazorServer.Pages.Sample.CRUD
{
    [Authorize]
    public partial class ListRadzenSamplePage : BaseListPageCode<Table1SecureService, ITable1SecureService, Table1Secure>
    {
        private bool ExcelDisable = true;
        //public override void Search()
        //{
        //    SearchRequestModel.RequestItemSize = 20;
        //    base.Search();
        //}
        public override void Search()
        {
            //ExecuteMethod(() =>
            //{
            //SearchRequestModel.RequestModel.Ad = "B";
            //SearchRequestModel.RequestModel.Table1Id = 1000000;//1035;

            //SearchRequestModel.RequestModel.StartDate = new System.DateTime(2021, 1, 1);
            SearchRequestModel.RequestItemSize = 2000;

            base.Search();
            ExcelDisable = false;
            //});
        }
        private void ExcelGizle()
        {
            ExcelDisable = true;
        }
        public override void OnAfterSearch()
        {
            base.OnAfterSearch();

            Console.WriteLine(SearchResultModel.Count);
        }

    }
}
