namespace Middt.Framework.Common.Export;

internal static class ExportTemplate
{
    /// <summary>Resolves a template path relative to the content root, accepting both '/' and '\' separators and rejecting path traversal.</summary>
    public static string Resolve(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safeFileName = Path.GetFileName(fileName);
        if (safeFileName != fileName || safeFileName is "." or "..")
        {
            throw new ArgumentException($"Invalid template file name '{fileName}'.", nameof(fileName));
        }

        var normalizedFolder = folder.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        var folderFullPath = Path.GetFullPath(Path.Combine(root, normalizedFolder));
        if (!folderFullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(folderFullPath, root, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Template folder '{folder}' must stay within the application root.", nameof(folder));
        }

        return Path.Combine(folderFullPath, safeFileName);
    }
}
