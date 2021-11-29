using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Common.Export;
using Middt.Framework.Common.Model.Data;
using Middt.Template.Api.Model.Database;
using Middt.Template.Api.Repository;
using Middt.Template.Common.Service;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace Middt.Template.Api.Controllers
{
    [ApiController]
    //[Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class Table1SecureController : BaseCrudControllerWithAuth<Table1, Table1SecureRepository>
    {

        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]

        public BaseResponseDataModel<List<ExpandoObject>> GetDataTable([FromBody] BaseSearchRequestModel<Table1> model)
        {
            BaseResponseDataModel<List<ExpandoObject>> response = new BaseResponseDataModel<List<ExpandoObject>>();
            response.Data = new List<ExpandoObject>();

            response.Result = Framework.Model.Model.Enumerations.ResultEnum.Success;

            BaseResponseDataModel<List<Table1>> responseDataModel = repository.GetItems(model);
            if (responseDataModel.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
            {
                if (responseDataModel.Data != null && responseDataModel.Data.Count > 0)
                {
                    ExpandoObject expandoObject;

                    foreach (Table1 table1 in responseDataModel.Data)
                    {
                        expandoObject = new ExpandoObject();

                        expandoObject.Add("Ad", table1.Ad);
                        expandoObject.Add("Tarih", table1.Tarih);

                        response.Data.Add(expandoObject);
                    }
                }
            }

            return response;
        }

        [Authorize]
        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]

        public BaseResponseDataModel<List<Table1>> GetRandevuByFirmaID([FromBody] BaseSearchRequestModel<Table1> model)
        {
            return repository.GetItems(model);
        }




        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuExcelTemplate([FromBody] BaseSearchRequestModel<Table1> model)
        {
            IList<Table1> docs = repository.GetAll().ToList();

            BaseExportModel baseExportModel = new BaseExportModel();
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje", Value = docs });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Baslik", Value = "Başlık" });

            BaseResponseDataModel<byte[]> resp = new BaseResponseDataModel<byte[]>();
            try
            {
                Table1ExcelExport table1ExcelExport = new Table1ExcelExport();
                resp = table1ExcelExport.Convert(baseExportModel);
            }
            catch (Exception ex)
            {
                resp.MessageList.Add("Bir hata oldu bla bla");
                resp.MessageList.Add(ex.ToString());
            }






            return resp;
        }

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuExcelToPdfTemplate([FromBody] BaseSearchRequestModel<Table1> model)
        {
            IList<Table1> docs = repository.GetAll().ToList();

            BaseExportModel baseExportModel = new BaseExportModel();
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje", Value = docs });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Baslik", Value = "Başlık" });

            BaseResponseDataModel<byte[]> resp = new BaseResponseDataModel<byte[]>();
            try
            {
                Table1ExcelToPdfExport table1ExcelExport = new Table1ExcelToPdfExport();
                resp = table1ExcelExport.Convert(baseExportModel);
            }
            catch (Exception ex)
            {
                resp.MessageList.Add("Bir hata oldu bla bla");
                resp.MessageList.Add(ex.ToString());
            }

            return resp;
        }

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuWordTemplate([FromBody] BaseSearchRequestModel<Table1> model)
        {
            IList<Table1> docs = repository.GetAll().ToList();

            BaseExportModel baseExportModel = new BaseExportModel();
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje", Value = docs });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "TesisatFirmaAdi", Value = "Mehmet TOSUN" });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje1", Value = docs });

            BaseResponseDataModel<byte[]> resp = new BaseResponseDataModel<byte[]>();
            try
            {

            }
            catch (Exception ex)
            {
                resp.MessageList.Add("Bir hata oldu bla bla");
                resp.MessageList.Add(ex.ToString());
            }

            Table1WordExport table1ExcelExport = new Table1WordExport();
            resp = table1ExcelExport.Convert(baseExportModel);

            return resp;
        }

        [HttpPost("[action]")]
        [ValidateModelAttribute(enableValidation = false)]
        public BaseResponseDataModel<byte[]> SozlesmeBedeliDokumuWordToPdfTemplate([FromBody] BaseSearchRequestModel<Table1> model)
        {
            IList<Table1> docs = repository.GetAll().ToList();

            BaseExportModel baseExportModel = new BaseExportModel();
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje", Value = docs });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "TesisatFirmaAdi", Value = "Mehmet TOSUN" });
            baseExportModel.ParamList.Add(new ExportParameterModel() { Key = "Proje1", Value = docs });

            BaseResponseDataModel<byte[]> resp = new BaseResponseDataModel<byte[]>();
            try
            {

            }
            catch (Exception ex)
            {
                resp.MessageList.Add("Bir hata oldu bla bla");
                resp.MessageList.Add(ex.ToString());
            }

            Table1WordToPdfExport table1ExcelExport = new Table1WordToPdfExport();
            resp = table1ExcelExport.Convert(baseExportModel);

            return resp;
        }

    }
}
