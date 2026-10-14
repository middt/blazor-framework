using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;

namespace Middt.Sample.BlazorServer.Pages.Bike.PopUp;

public partial class PopCustomerSelect
{
    private BaseModal baseModal = default!;
    private BaseRadzenListPage<CustomerService, Customer> customerListPage = default!;

    [Parameter]
    public EventCallback<Customer> OnSelect { get; set; }

    public override Task Open() => baseModal.Open();

    public override Task Close() => baseModal.Close();

    private async Task Select(Customer model)
    {
        await OnSelect.InvokeAsync(model);
        await Close();
    }

    private Task HandleClosed(string name) => OnClose.InvokeAsync(name);
}
