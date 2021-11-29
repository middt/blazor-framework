using BlazorInputFile;
using Microsoft.AspNetCore.Components;
using Suges.Framework.Blazor.Web.Model;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Notification;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.FileUpload
{
    public partial class BaseFileUploadComponent : BaseComponent
    {
        [Parameter]
        public List<string> acceptedFileTypes { get; set; }


        [Parameter]
        public int MaxFileSize { get; set; }

        [Parameter]
        public int MaxFileCount { get; set; }


        [Parameter]
        public List<FileUploadModel> fileUploadModelList { get; set; }



        [Inject]
        protected INotification Notification { get; set; }

        [Inject]
        protected IBaseLog Log { get; set; }

        public BaseFileUploadComponent()
        {
            if (acceptedFileTypes == null)
            {
                acceptedFileTypes = new List<string>() { ".csv", ".xlsx" };
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




        public async Task HandleSelection(IFileListEntry[] files)
        {

            BaseResponseModel result = new BaseResponseModel();
            result.Result = ResultEnum.Success;


            BaseResponseModel checkResult;
            foreach (IFileListEntry fileListEntry in files)
            {

                checkResult = CheckFileCount(fileListEntry);
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
                fileUploadModel.Name = fileListEntry.Name;


                var ms = new MemoryStream();

                await fileListEntry.Data.CopyToAsync(ms);

                fileUploadModel.File = ms.ToArray();
                // TODO : Byte Array veya base64 hangisi
                string base64 = Convert.ToBase64String(ms.ToArray());



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

        private BaseResponseModel CheckFileExist(IFileListEntry fileListEntry)
        {

            BaseResponseModel baseResponseModel = new BaseResponseModel();
            if (fileUploadModelList.Any(x => x.Name == fileListEntry.Name))
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"{fileListEntry.Name} dosyası sisteme daha önce eklenmiş");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileSize(IFileListEntry fileListEntry)
        {
            BaseResponseModel baseResponseModel = new BaseResponseModel();
            if (fileListEntry.Size > MaxFileSize)
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"{fileListEntry.Name} dosyası için izin verilen max boyut: {MaxFileSize / 1024 * 1024} MB");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileExtension(IFileListEntry fileListEntry)
        {
            BaseResponseModel baseResponseModel = new BaseResponseModel();
            bool isExist = false;
            foreach (string extension in acceptedFileTypes)
            {
                if (fileListEntry.Name.EndsWith(extension))
                {
                    isExist = true;
                    break;
                }
            }
            if (!isExist)
            {
                baseResponseModel.Result = ResultEnum.Error;
                baseResponseModel.MessageList.Add($"{fileListEntry.Name} dosyası için izin verilen uzantıda değil.");
            }
            else
            {
                baseResponseModel.Result = ResultEnum.Success;

            }
            return baseResponseModel;
        }
        private BaseResponseModel CheckFileCount(IFileListEntry fileListEntry)
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

