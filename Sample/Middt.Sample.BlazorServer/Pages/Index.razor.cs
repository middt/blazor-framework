using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Captcha;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Template.Api.Model.Database;
using Middt.Template.Common.Service;
using System.Threading.Tasks;

namespace Middt.Template.BlazorServer.Pages
{
    public partial class Index : BasePageComponent
    {
        public bool isValid { get; set; }

        public BaseCaptcha baseCaptcha { get; set; }

        [Inject]
        public Table1SecureService table1SecureService { get; set; }


        public void CaptchaChange(string value)
        {
            if (baseCaptcha != null)
                isValid = baseCaptcha.IsValid(value);
            else
                isValid = false;
        }

        public async Task WordTemplate()
        {
            ExecuteMethod(() =>
            {
                Table1Secure model = new Table1Secure();

                BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
                baseModel.RequestModel = model;
                BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuWordTemplate(baseModel);

                jsRuntime.SaveAsFileAsync("Sample-Word.docx", resp.Data, "application/ms-word");
            });
        }

        public async Task WordToPdfTemplate()
        {
            ExecuteMethod(() =>
            {
                Table1Secure model = new Table1Secure();

                BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
                baseModel.RequestModel = model;
                BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuWordToPdfTemplate(baseModel);
                jsRuntime.SaveAsFileAsync("Sample-WordToPDF.pdf", resp.Data, "application/pdf"); ;
            });
        }

        public async Task ExcelTemplate()
        {
            ExecuteMethod(() =>
            {
                Table1Secure model = new Table1Secure();

                BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
                baseModel.RequestModel = model;
                BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuExcelTemplate(baseModel);

                jsRuntime.SaveAsFileAsync("Sample-Excel.xlsx", resp.Data, "application/vdn.ms-excel");
            });
        }

        public async Task ExcelToPdfTemplate()
        {
            ExecuteMethod(() =>
            {
                Table1Secure model = new Table1Secure();

                BaseSearchRequestModel<Table1Secure> baseModel = new BaseSearchRequestModel<Table1Secure>();
                baseModel.RequestModel = model;
                BaseResponseDataModel<byte[]> resp = table1SecureService.SozlesmeBedeliDokumuExcelToPdfTemplate(baseModel);
                jsRuntime.SaveAsFileAsync("Sample-ExcelToPdf.pdf", resp.Data, "application/pdf"); ;
            });
        }
    }
}
