using Microsoft.AspNetCore.Components;
using Middt.Framework.Common.Model.Data;
using System;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal
{
    public partial class BaseCrudModal: Middt.Framework.Blazor.Web.Base.Component.Modal.BaseModalCode
    {
        public BaseModal baseModal { get; set; }
        [Parameter]
        public Action OnSave { get; set; }
        public void Save()
        {
            OnSave?.Invoke();
        }

        public BaseRequestModel Model { get; set; }

        public void Open(BaseRequestModel model)
        {
            Model = model;

            baseModal.Open();
        }

        public void Close()
        {
            baseModal.Close();
            OnClose?.Invoke(string.Empty);
        }
    }
}
