using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Model.Enumerations;
using System.Linq;

namespace Middt.Framework.Blazor.Web.Base
{
    public abstract partial class BaseDetailCrudPageComponentClass<TService, TInterface, TModel> : BaseDetailPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
         where TService : BaseCrudRefit<TInterface, TModel>
    {
        public BaseDetailCrudPageComponentClass() : base()
        {

        }

        protected bool IsUpdate { get; set; }

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

            StateHasChanged();
        }
        public virtual void SaveButtonClick()
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
                    if (!string.IsNullOrEmpty(SaveAfterURL))
                    {
                        NavigationManager.NavigateTo(SaveAfterURL);
                    }
                    else
                    {
                        Notification.ShowSuccessMessage("Bilgiler başarı ile kaydedildi.", string.Empty);
                    }
                }
                else
                {
                    Log.Error(result.ErrorText);
                    Notification.ShowErrorMessage("Bilgiler Kaydedilirken bir hata oluştu.", result.MessageList.FirstOrDefault());
                }
            });
        }


        [Parameter]
        public string SaveAfterURL { get; set; }
    }
}