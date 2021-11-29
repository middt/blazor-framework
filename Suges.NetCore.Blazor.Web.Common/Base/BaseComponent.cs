using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Notification;
using Suges.Framework.Common.Security;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace Suges.Framework.Blazor.Web.Base
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


        //protected Timer timer { get; set; }

        //public int ExpireIn { get; set; }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                //ExecuteMethod(() =>
                //{
                LoadCustomOnAfterRenderAsync(firstRender);
                //});
            }
            else
            {
                CustomOnAfterRenderAsync(firstRender);
            }

            //ExpireIn = 30000;
        }

        private async Task LoadCustomOnAfterRenderAsync(bool firstRender)
        {
            //timer = new Timer(1000);
            //timer.Elapsed += Timer_Elapsed;
            //timer.Start();

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

        //private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        //{
        //    ExpireIn--;
        //}
        //~BaseComponent()
        //{
        //    if (timer != null)
        //    {
        //        timer.Stop();
        //    }
        //}

        protected virtual void CustomOnAfterRenderAsync(bool firstRender)
        {

        }
    }
}
