using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;
using System;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal
{
    public partial class BaseCrudModal : BaseModalCode
    {
        public BaseModal baseModal { get; set; }
        public EventCallback? OnSave { get; set; }

        public async Task Save()
        {
            if (OnSave != null)
                await OnSave?.InvokeAsync();
        }

        public BaseRequestModel Model { get; set; }

        public async Task Open(BaseRequestModel model)
        {
            Model = model;

            await baseModal.Open();
        }

        public override async Task Close()
        {
            await baseModal.Close();
            if (OnClose != null)
                await OnClose?.InvokeAsync(string.Empty);
        }
    }
}
