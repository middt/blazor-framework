using Middt.Framework.Common.Extension;
using Middt.Framework.Common.Model.Data;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace Middt.Framework.Common.Export;

/// <summary>Fills a Word template via mail merge: primitive values as fields, enumerables as merge groups.</summary>
public abstract class BaseWordExport
{
    public abstract string Folder { get; }

    public abstract string TemplateFileName { get; }

    public virtual BaseResponseDataModel<byte[]> Convert(BaseExportModel baseExportModel) => ConvertToWord(baseExportModel);

    protected BaseResponseDataModel<byte[]> ConvertToWord(BaseExportModel baseExportModel)
    {
        using var reader = File.OpenRead(ExportTemplate.Resolve(Folder, TemplateFileName));
        using var wordDocument = new WordDocument(reader, FormatType.Docx);

        var primitives = new List<ExportParameterModel>();
        foreach (var parameter in baseExportModel?.ParamList ?? [])
        {
            if (parameter.Value is null || parameter.Value.GetType().IsPrimitiveType())
            {
                primitives.Add(parameter);
            }
            else if (parameter.Value is IEnumerable<object> rows)
            {
                wordDocument.MailMerge.ExecuteGroup(new MailMergeDataTable(parameter.Key, rows));
            }
        }

        if (primitives.Count > 0)
        {
            wordDocument.MailMerge.Execute(
                primitives.Select(p => p.Key).ToArray(),
                primitives.Select(p => System.Convert.ToString(p.Value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty).ToArray());
        }

        using var stream = new MemoryStream();
        wordDocument.Save(stream, FormatType.Docx);
        return BaseResponseDataModel<byte[]>.Success(stream.ToArray());
    }
}
