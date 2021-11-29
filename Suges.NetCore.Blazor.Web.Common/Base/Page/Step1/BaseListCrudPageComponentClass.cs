using Microsoft.AspNetCore.Components;
using Radzen;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Suges.Framework.Blazor.Web.Base
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
        public string InsertText { get; set; } = "Yeni Kayıt";

        public Action<TModel> OnAfterSave { get; set; }

        public Action OnAfterModalClose { get; set; }

        public Action OnBeforeModalOpen { get; set; }





        public TModel Model { get; set; }

        public BaseCrudModal CrudModal { get; set; }

        protected bool IsUpdate { get; set; }

        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                CrudModal.OnSave += () =>
                {
                    SaveButtonClick();
                };

                CrudModal.OnClose += (string ModalName) =>
                {
                    CloseButtonClick();
                };
            }
        }
        public void New()
        {
            Open(new TModel(), false);
        }

        public void Edit(TModel model)
        {
            Open(model, true);
        }
        private void Open(TModel model, bool isUpdate)
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
            OnBeforeModalOpen?.Invoke();

            CrudModal.Open(Model);

            StateHasChanged();
        }
        protected virtual void SaveButtonClick()
        {
            ExecuteMethod(() =>
            {
                BaseResponseDataModel<TModel> result;
                if (IsUpdate)
                {
                    result = Service.Update(Model);
                }
                else
                {
                    result = Service.Insert(Model);
                }

                if (result.Result == ResultEnum.Success)
                {
                    Notification.ShowSuccessMessage("Bilgiler başarı ile kaydedildi.", string.Empty);

                    // 
                    if (IsLoadAfterSave)
                        Search();


                    if (IsCloseModalAfterEdit)
                        CrudModal.Close();

                    OnAfterSave?.Invoke(result.Data);
                }
                else
                {
                    Log.Error(result.ErrorText);
                    Notification.ShowErrorMessage("Bilgiler Kaydedilirken bir hata oluştu.", result.MessageList.FirstOrDefault());

                }
            });
        }
        protected virtual void CloseButtonClick()
        {
            OnAfterModalClose?.Invoke();
        }
        public void Delete(TModel model)
        {
            Service.Delete(model);
        }
    }
}
