using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Middt.Framework.Blazor.Web.Model;
using Middt.Framework.Model.Model.Enumerations;
using Syncfusion.Blazor.Inputs;

namespace Middt.Framework.Blazor.Web.Base.Component.FileUpload;

/// <summary>Collects uploaded files (in memory) into <see cref="FileUploadModelList"/> with count/extension/size checks.</summary>
public partial class BaseFileUploadComponent : BaseComponent
{
    [Parameter]
    public string AcceptedFileTypes { get; set; } = ".csv,.xlsx";

    [Parameter]
    public int MaxFileSize { get; set; } = 5 * 1024 * 1024;

    [Parameter]
    public int MaxFileCount { get; set; } = 1;

    [Parameter]
    public List<FileUploadModel> FileUploadModelList { get; set; } = [];

    [Parameter]
    public EventCallback<List<FileUploadModel>> FileUploadModelListChanged { get; set; }

    public List<FileUploadModel> ActiveFileUploadModelList =>
        FileUploadModelList.Where(x => x.IsDeleted == YesNoEnum.No).ToList();

    public async Task HandleSelection(UploadChangeEventArgs args)
    {
        var errors = new List<string>();

        foreach (var upload in args.Files)
        {
            var name = upload.FileInfo.Name;
            var error =
                ActiveFileUploadModelList.Count >= MaxFileCount ? "Dosya sayısı için izin verilen sayıyı aştınız." :
                !IsAllowedExtension(name) ? $"{name} dosyasının uzantısına izin verilmiyor." :
                FileUploadModelList.Any(x => x.Name == name && x.IsDeleted == YesNoEnum.No) ? $"{name} dosyası sisteme daha önce eklenmiş." :
                upload.FileInfo.Size > MaxFileSize ? $"{name} dosyası için izin verilen en büyük boyut: {MaxFileSize / (1024 * 1024)} MB." :
                null;

            if (error is not null)
            {
                errors.Add(error);
                continue;
            }

            await using var source = upload.File.OpenReadStream(MaxFileSize, ComponentCancellation);
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ComponentCancellation);

            FileUploadModelList.Add(new FileUploadModel { Name = name, IsDeleted = YesNoEnum.No, File = buffer.ToArray() });
        }

        if (errors.Count == 0)
        {
            Notification.ShowSuccessMessage("Dosya başarı ile yüklendi.", null);
        }
        else
        {
            Logger.LogWarning("File upload rejected: {Errors}", string.Join(" ", errors));
            Notification.ShowErrorMessage("Dosya yüklenirken bir hata oluştu.", errors[0]);
        }

        await FileUploadModelListChanged.InvokeAsync(FileUploadModelList);
    }

    public async Task Delete(FileUploadModel fileUploadModel)
    {
        if (fileUploadModel.FileUploadModelID < 1)
        {
            FileUploadModelList.Remove(fileUploadModel);
        }
        else
        {
            fileUploadModel.IsDeleted = YesNoEnum.Yes;
        }

        await FileUploadModelListChanged.InvokeAsync(FileUploadModelList);
    }

    private bool IsAllowedExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(AcceptedFileTypes))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return AcceptedFileTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(allowed => string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase));
    }
}
