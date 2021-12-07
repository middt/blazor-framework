using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using System;

namespace Middt.Sample.BlazorServer.Pages.Bike.PopUp
{
    public partial class PopCustomerSelect : BaseModalCode
    {
        public BaseRadzenListPage<CustomerService, ICustomerService, Customer> baseListPage { get; set; }

        public BaseModal baseModal { get; set; }

        [Parameter]
        public Action<Customer> OnSelect { get; set; }
        public void Select(Customer model)
        {
            OnSelect?.Invoke(model);
            Close();
        }
        public void Open()
        {            
            baseModal.Open();
        }

        public void Close()
        {
            baseModal.Close();
            OnClose?.Invoke(string.Empty);
        }
    }
}
