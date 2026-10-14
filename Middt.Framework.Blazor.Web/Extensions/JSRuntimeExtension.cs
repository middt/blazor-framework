using Microsoft.JSInterop;

namespace Middt.Framework.Blazor.Web.Extensions;

public static class JSRuntimeExtension
{
    /// <summary>Downloads <paramref name="data"/> in the browser by streaming it (no base64 round trip).</summary>
    public static async Task SaveAsFileAsync(this IJSRuntime js, string fileName, byte[] data, string contentType = "application/octet-stream")
    {
        using var stream = new MemoryStream(data);
        using var streamRef = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("middt.downloadFile", fileName, streamRef, contentType);
    }
}
