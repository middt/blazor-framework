namespace Middt.Framework.Common.Export;

public class BaseExportModel
{
    public List<ExportParameterModel> ParamList { get; set; } = [];
}

public class ExportParameterModel
{
    public string Key { get; set; } = string.Empty;

    public object? Value { get; set; }
}
