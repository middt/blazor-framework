using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Service;
using System;
using System.Threading.Tasks;

namespace Middt.Sample.BlazorServer.Pages.Bike.PopUp
{
    public partial class PopCustomerSelect : BaseModalCode
    {
        public BaseRadzenListPage<CustomerService, ICustomerService, Customer> baseListPage { get; set; }

        public BaseModal baseModal { get; set; }

        [Parameter]
        public EventCallback<Customer>? OnSelect { get; set; }
        public async Task Select(Customer model)
        {
            if (OnSelect != null)
                await OnSelect?.InvokeAsync(model);
            await Close();
        }
        public override async Task Open()
        {            
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
