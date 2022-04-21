using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Model;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Notification;
using Middt.Framework.Model.Model.Enumerations;
using Syncfusion.Blazor.Inputs;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.FileUpload
{
    public partial class BaseFileUploadComponent : BaseComponent
    {
        [Parameter]
        public string acceptedFileTypes { get; set; }


        [Parameter]
        public int MaxFileSize { get; set; }

        [Parameter]
        public int MaxFileCount { get; set; }


        [Parameter]
        public List<FileUploadModel> fileUploadModelList { get; set; }

        public BaseFileUploadComponent()
        {
            if (string.IsNullOrEmpty(acceptedFileTypes))
            {
                acceptedFileTypes = ".csv,.xlsx";
            }
            MaxFileCount = 1;
            MaxFileSize = 5 * 1024 * 1024; // 5MB
        }

        public List<FileUploadModel> ActiveFileUploadModelList
        {
            get
            {
                if (fileUploadModelList != null)
                {
                    return fileUploadModelList.Where(x => x.IsDeleted == YesNoEnum.No).ToList();
                }
                else
                {
                    return new List<FileUploadModel>();
                }
            }
        }

        public async Task HandleSelection(UploadChangeEventArgs args)
        {

            BaseResponseModel result = new BaseResponseModel();
            result.Result = ResultEnum.Success;


            BaseResponseModel checkResult;
            foreach (UploadFiles fileListEntry in args.Files)
            {

                checkResult = CheckFileCount();
                if (checkResult.Result != ResultEnum.Success)
                {
                    result.Result = ResultEnum.Error;
                    result.MessageList.AddRange(checkResult.MessageList);

                    break;
                }


                checkResult = CheckFileExtension(fileListEntry);
                if (checkResult.Result != ResultEnum.Success)
                {
                    result.Result = ResultEnum.Error;
                    result.MessageList.AddRange(checkResult.MessageList);

                    continue;
                }

                checkResult = CheckFileExist(fileListEntry);
                if (checkResult.Result != ResultEnum.Success)
                {
                    result.Result = ResultEnum.Error;
                    result.MessageList.AddRange(checkResult.MessageList);

                    continue;
                }

                checkResult = CheckFileSize(fileListEntry);
                if (checkResult.Result != ResultEnum.Success)
                {
                    result.Result = ResultEnum.Error;
                    result.MessageList.AddRange(checkResult.MessageList);

                    continue;
                }


                FileUploadModel fileUploadModel = new FileUploadModel();
                fileUploadModel.IsDeleted = YesNoEnum.No;
                fileUploadModel.Name = fileListEntry.FileInfo.Name;

                fileUploadModel.File = fileListEntry.Stream.ToArray();
                fileUploadModelList.Add(fileUploadModel);
            }

            if (result.Result == ResultEnum.Success)
            {
                Notification.ShowSuccessMessage("Dosya başarı ile yüklendi.", string.Empty);
            }
            else
            {
                Log.Error(result.ErrorText);
                Notification.ShowErrorMessage("Dosya yüklenirken bir hata oluştu.", result.MessageList.FirstOrDefault());
            }
        }

        public void Delete(FileUploadModel fileUploadModel)
        {
            if (fileUploadModel.FileUploadModelID < 1)
            {
                fileUploadModelList.Remove(fileUploadModel);
            }
            else
            {
                fileUploadModel.IsDeleted = YesNoEnum.Yes;
            }
            StateHasChanged();
        }

        private BaseResponseModel CheckFileExist(UploadFiles fileListEntry)
        {

            BaseResponseModel baseResponseModel = new BaseResponseModel();
            if (fileUploadModelList.Any(x => x.Name == fileListEntry.FileInfo.Name))
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"{fileListEntry.FileInfo.Name} dosyası sisteme daha önce eklenmiş");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileSize(UploadFiles fileListEntry)
        {
            BaseResponseModel baseResponseModel = new BaseResponseModel();
            if (fileListEntry.FileInfo.Size > MaxFileSize)
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"{fileListEntry.FileInfo.Name} dosyası için izin verilen max boyut: {MaxFileSize / 1024 * 1024} MB");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileExtension(UploadFiles fileListEntry)
        {
            BaseResponseModel baseResponseModel = new BaseResponseModel();
            bool isExist = false;




            //foreach (string extension in acceptedFileTypes)
            //{
            //    if (fileListEntry.FileInfo.Name.EndsWith(extension))
            //    {
            //        isExist = true;
            //        break;
            //    }
            //}
            //if (!isExist)
            //{
            //    baseResponseModel.Result = ResultEnum.Error;
            //    baseResponseModel.MessageList.Add($"{fileListEntry.FileInfo.Name} dosyası için izin verilen uzantıda değil.");
            //}
            //else
            //{
            //    baseResponseModel.Result = ResultEnum.Success;

            //}
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileCount()
        {
            BaseResponseModel baseResponseModel = new BaseResponseModel();
            if (fileUploadModelList.Count >= MaxFileCount)
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"Dosya sayısı için izin verilen sayıyı aştınız.");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
    }
}
