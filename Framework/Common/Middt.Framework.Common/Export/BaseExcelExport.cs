using Middt.Framework.Common.Model.Data;
using Middt.Framework.Model.Model.Enumerations;
using Syncfusion.XlsIO;
using System;
using System.Data;
using System.IO;

namespace Middt.Framework.Common.Export
{
    public abstract class BaseExcelExport //: IBaseExport
    {
        public abstract string Folder { get; }
        public abstract string TemplateFileName { get; }


        public virtual BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel)
        {
            return ConvertToExcel(baseExportModel);
        }
        protected BaseResponseDataModel<byte[]> ConvertToExcel(BaseExportModel baseExportModel)
        {
            BaseResponseDataModel<byte[]> response = new BaseResponseDataModel<byte[]>();

            try
            {
                response.Result = ResultEnum.Success;

                using (ExcelEngine excelEngine = new ExcelEngine())
                {
                    IApplication application = excelEngine.Excel;
                    application.DefaultVersion = ExcelVersion.Excel2016;
                    string path = Path.Combine(Environment.CurrentDirectory, Folder, TemplateFileName);
                    FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
                    IWorkbook workbook = application.Workbooks.Open(fileStream);

                    ITemplateMarkersProcessor marker = workbook.CreateTemplateMarkersProcessor();
                    CustomConvert(baseExportModel, workbook, marker);

                    using (MemoryStream stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        response.Data = stream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                response.Result = Framework.Model.Model.Enumerations.ResultEnum.Error;
                response.MessageList.Add(ex.ToString());
            }
            return response;
        }

        public virtual void CustomConvert(BaseExportModel baseExportModel, IWorkbook workbook, ITemplateMarkersProcessor marker)
        {
            if (baseExportModel != null && baseExportModel.ParamList != null && baseExportModel.ParamList.Count > 0)
            {
                foreach (ExportParameterModel keyValue in baseExportModel.ParamList)
                {
                    if (keyValue.Value is DataSet)
                    {
                        DataTable dt = new DataTable();
                        foreach (DataTable dataTable in (keyValue.Value as DataSet).Tables)
                        {
                            dt.Merge(dataTable);
                        }

                        marker.AddVariable(keyValue.Key, dt);
                    }
                    else
                    {
                        marker.AddVariable(keyValue.Key, keyValue.Value);
                    }
                }
            }
            marker.ApplyMarkers();

        }
    }
}
