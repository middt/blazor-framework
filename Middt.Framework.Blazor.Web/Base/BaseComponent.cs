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
    public abstract class BaseComponent : ComponentBase
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

        public virtual async Task ExecuteMethod(Action action)
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
                await CloseModal();
                await InvokeAsync(() => StateHasChanged());
            }
        }



        protected virtual async Task OpenModal()
        {
            if (LoadingModal != null && !LoadingModal.IsModalOpen)
            {
                await InvokeAsync(() => LoadingModal.Open());
            }
        }
        protected virtual async Task CloseModal()
        {
            if (LoadingModal != null)
            {
                await InvokeAsync(() => LoadingModal.Close());
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

                await LoadCustomOnAfterRenderAsync(firstRender);

            }
            else
            {
                await CustomOnAfterRenderAsync(firstRender);
            }
        }

        private async Task LoadCustomOnAfterRenderAsync(bool firstRender)
        {
            await Task.Run(() =>
            {
                baseTokenHelper.GetClaimsPrincipal();
            }).ContinueWith(async prev =>
            {
                await CustomOnAfterRenderAsync(firstRender);
            })
            .ContinueWith(async prev =>
            {
                await InvokeAsync(() => StateHasChanged());
            });
        }

        protected virtual async Task CustomOnAfterRenderAsync(bool firstRender) { }
    }
}
