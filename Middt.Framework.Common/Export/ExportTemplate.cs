namespace Middt.Framework.Common.Export;

internal static class ExportTemplate
{
    /// <summary>Resolves a template path relative to the content root, accepting both '/' and '\' separators.</summary>
    public static string Resolve(string folder, string fileName)
    {
        var normalizedFolder = folder.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Environment.CurrentDirectory, normalizedFolder, fileName);
    }
}
