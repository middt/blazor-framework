using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Notification;
using Middt.Framework.Common.Security;
using System;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base
{
    public class BaseComponent : ComponentBase
    {
        [Inject]
        protected NavigationManager NavigationManager { get; set; }

        [Inject]
        protected INotification Notification { get; set; }

        [Inject]
        protected IBaseLog Log { get; set; }

        [Inject]
        protected IJSRuntime jsRuntime { get; set; }

        public BaseLoadingModal LoadingModal { get; set; }

        public virtual async void ExecuteMethod(Action action)
        {
            try
            {
                OpenModal();
                await Task.Run(action);
            }
            catch (Exception ex)
            {
                Notification.ShowErrorMessage("Bilgiler kaydedilirken bir hata oluştu.", ex.ToString());
                Log.Error(ex.ToString());
            }
            finally
            {
                CloseModal();

                InvokeAsync(() => StateHasChanged()).Wait();
            }
        }

        protected void OpenModal()
        {
            if (LoadingModal != null && !LoadingModal.IsModalOpen)
            {
                LoadingModal.Open();
            }
        }
        protected void CloseModal()
        {
            if (LoadingModal != null)
            {
                LoadingModal.Close();
            }
        }
        public bool IsAuthenticated { get; set; } = false;

        [Inject]
        public BaseTokenHelper baseTokenHelper { get; set; }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                Notification.Clear();

                LoadCustomOnAfterRenderAsync(firstRender);

            }
            else
            {
                CustomOnAfterRenderAsync(firstRender);
            }
        }

        private async Task LoadCustomOnAfterRenderAsync(bool firstRender)
        {
            await Task.Run(() =>
            {
                baseTokenHelper.GetClaimsPrincipal();
            }).ContinueWith(async prev =>
            {
                CustomOnAfterRenderAsync(firstRender);
            }).ContinueWith(async prev =>
            {
                InvokeAsync(() => StateHasChanged()).Wait();
            });
        }

        protected virtual void CustomOnAfterRenderAsync(bool firstRender)
        {

        }
    }
}
