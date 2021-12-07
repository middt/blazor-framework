using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

public static class JSRuntimeExtension
{
    public static async Task SaveAsFileAsync(this IJSRuntime js, string filename, byte[] data, string type = "application/octet-stream")
    {
        await js.InvokeAsync<object>("JSInteropExt.saveAsFile", filename, type, Convert.ToBase64String(data));

    }
}

