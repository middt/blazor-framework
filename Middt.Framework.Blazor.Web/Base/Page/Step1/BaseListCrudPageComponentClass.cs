using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base
{
    public class BaseListCrudPageComponentClass<TService, TInterface, TModel> : BaseListPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        [Parameter]
        public RenderFragment EditContent { get; set; }

        [Parameter]
        public string ModalTitle { get; set; }

        [Parameter]
        public Dictionary<string, object> ModalParameters { get; set; }

        [Parameter]
        public bool IsCloseModalAfterEdit { get; set; } = true;

        [Parameter]
        public bool IsModalFullScreen { get; set; } = false;

        [Parameter]
        public string ModalWidth { get; set; }

        [Parameter]
        public string ModalHeight { get; set; }

        [Parameter]
        public bool IsLoadAfterSave { get; set; } = true;
        [Parameter]
        public bool HideInsertButton { get; set; } = false;

        [Parameter]
        public string InsertText { get; set; } = "Yeni Kayıt";

        [Parameter]
        public EventCallback<TModel>? OnAfterSave { get; set; }

        [Parameter]
        public EventCallback? OnAfterModalClose { get; set; }

        [Parameter]
        public EventCallback? OnBeforeModalOpen { get; set; }

        public TModel Model { get; set; }

        public BaseCrudModal CrudModal { get; set; }

        protected bool IsUpdate { get; set; }

        protected override async Task CustomOnAfterRenderAsync(bool firstRender)
        {
            await base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                CrudModal.OnSave = EventCallback.Factory.Create(this, SaveButtonClick);
                CrudModal.OnClose = EventCallback.Factory.Create<string>(this, CloseButtonClick);
            }
        }
        public async Task New()
        {
            await Open(new TModel(), false);
        }

        public async Task Edit(TModel model)
        {
            await Open(model, true);
        }
        private async Task Open(TModel model, bool isUpdate)
        {
            IsUpdate = isUpdate;
            Model = model.Clone() as TModel;


            // 
            if (ModalParameters != null && ModalParameters.Count > 0)
            {
                foreach (KeyValuePair<string, object> parameter in ModalParameters)
                {
                    typeof(TModel).GetProperty(parameter.Key).SetValue(Model, parameter.Value);
                }

            }
            //
            if (OnBeforeModalOpen != null)
                await OnBeforeModalOpen?.InvokeAsync();

            await CrudModal.Open(Model);

            StateHasChanged();
        }
        protected async Task SaveButtonClick()
        {
            await ExecuteMethod(async() =>
            {
                BaseResponseDataModel<TModel> result;
                if (IsUpdate)
                {
                    result = await Service.Update(Model);
                }
                else
                {
                    result = await Service.Insert(Model);
                }

                if (result.Result == ResultEnum.Success)
                {
                    if (IsUpdate)
                    {
                        Notification.ShowSuccessMessage("Bilgiler başarı ile güncellendi.", string.Empty);
                    }
                    else
                    {
                        Notification.ShowSuccessMessage("Bilgiler başarı ile kaydedildi.", string.Empty);
                    }
                    // 
                    if (IsLoadAfterSave)
                        await Search();


                    if (IsCloseModalAfterEdit)
                        await InvokeAsync(() => CrudModal.Close());

                    if (OnAfterSave != null)
                        await InvokeAsync(() => OnAfterSave?.InvokeAsync(result.Data));
                }
                else
                {
                    Log.Error(result.ErrorText);
                    Notification.ShowErrorMessage("Bilgiler Kaydedilirken bir hata oluştu.", result.MessageList.FirstOrDefault());

                }
            });
        }
        protected virtual async Task CloseButtonClick()
        {
            if (OnAfterModalClose != null)
                await OnAfterModalClose?.InvokeAsync();
        }
        public async Task Delete(TModel model)
        {
            await Service.Delete(model);
        }
    }
}
