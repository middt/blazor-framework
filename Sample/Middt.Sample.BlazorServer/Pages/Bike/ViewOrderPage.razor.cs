using Microsoft.AspNetCore.Components;
using Middt.Sample.BlazorServer.Pages.Bike.PopUp;

namespace Middt.Sample.BlazorServer.Pages.Bike;

public partial class ViewOrderPage
{
    private List<Store>? stores;
    private List<Staff>? staff;
    private string? customerName;
    private PopCustomerSelect customerSelect = default!;

    [Inject]
    private StoreService StoreService { get; set; } = default!;

    [Inject]
    private StaffService StaffService { get; set; } = default!;

    protected override async Task CustomOnAfterRenderAsync(bool firstRender)
    {
        await base.CustomOnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            var storeTask = StoreService.GetAllAsync(ComponentCancellation);
            var staffTask = StaffService.GetAllAsync(ComponentCancellation);
            await Task.WhenAll(storeTask, staffTask);

            stores = Unwrap(await storeTask);
            staff = Unwrap(await staffTask);
            StateHasChanged();
        }
    }

    private List<T>? Unwrap<T>(Middt.Framework.Common.Model.Data.BaseResponseDataModel<List<T>> response)
    {
        if (!response.IsSuccess)
        {
            Notification.ShowErrorMessage("Hata", response.ErrorText);
        }

        return response.Data;
    }

    private Task OpenCustomerModal() => customerSelect.Open();

    private void OnCustomerSelected(Customer customer)
    {
        SearchRequestModel.RequestModel.CustomerId = customer.CustomerId;
        customerName = customer.Name;
        StateHasChanged();
    }
}
