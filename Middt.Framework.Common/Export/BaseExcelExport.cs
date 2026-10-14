using System.Data;
using Middt.Framework.Common.Model.Data;
using Syncfusion.XlsIO;

namespace Middt.Framework.Common.Export;

/// <summary>Fills an Excel template (Syncfusion XlsIO template markers) with <see cref="BaseExportModel"/> values.</summary>
public abstract class BaseExcelExport
{
    public abstract string Folder { get; }

    public abstract string TemplateFileName { get; }

    public virtual BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel) => ConvertToExcel(baseExportModel);

    protected BaseResponseDataModel<byte[]> ConvertToExcel(BaseExportModel baseExportModel)
    {
        using var excelEngine = new ExcelEngine();
        var application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;

        using var fileStream = File.OpenRead(ExportTemplate.Resolve(Folder, TemplateFileName));
        var workbook = application.Workbooks.Open(fileStream);

        CustomConvert(baseExportModel, workbook, workbook.CreateTemplateMarkersProcessor());

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return BaseResponseDataModel<byte[]>.Success(stream.ToArray());
    }

    public virtual void CustomConvert(BaseExportModel baseExportModel, IWorkbook workbook, ITemplateMarkersProcessor marker)
    {
        foreach (var parameter in baseExportModel?.ParamList ?? [])
        {
            if (parameter.Value is DataSet dataSet)
            {
                var merged = new DataTable();
                foreach (DataTable table in dataSet.Tables)
                {
                    merged.Merge(table);
                }

                marker.AddVariable(parameter.Key, merged);
            }
            else
            {
                marker.AddVariable(parameter.Key, parameter.Value);
            }
        }

        marker.ApplyMarkers();
    }
}
