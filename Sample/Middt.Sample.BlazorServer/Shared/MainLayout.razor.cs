using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using Radzen.Blazor;
using Middt.Framework.Common.Security;
using Middt.Template.BlazorServer.Model;
using Middt.Template.BlazorServer.Service;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Middt.Template.BlazorServer.Shared
{
    public partial class MainLayout : LayoutComponentBase
    {

        [Inject]
        ThemeState ThemeState { get; set; }
        [Inject]
        MenuService MenuService { get; set; }
        [Inject]
        NavigationManager UriHelper { get; set; }
        [Inject]
        IJSRuntime JSRuntime { get; set; }
        [Inject]
        IHttpContextAccessor httpContextAccessor { get; set; }
        [Inject]
        protected IBaseSessionState baseSessionState { get; set; }

        [Inject]
        protected BaseTokenHelper baseTokenHelper { get; set; }

        RadzenSidebar sidebar0;
        RadzenBody body0;

        bool sidebarExpanded = true;
        bool bodyExpanded = false;

        dynamic themes = new[]
        {
    new { Text = "Default", Value = "default"},
    new { Text = "Dark", Value="dark" },
    new { Text = "Software", Value = "software"},
    new { Text = "Humanistic", Value = "humanistic" }
    };

        IEnumerable<MenuItem> examples;

        string Theme
        {
            get
            {
                return $"{ThemeState.CurrentTheme}.css";
            }
        }

        protected override void OnInitialized()
        {
            if (httpContextAccessor != null && httpContextAccessor.HttpContext != null &&
                 httpContextAccessor.HttpContext.Request != null && httpContextAccessor.HttpContext.Request.Headers.ContainsKey("User-Agent"))
            {
                var userAgent = httpContextAccessor.HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
                if (!string.IsNullOrEmpty(userAgent))
                {
                    if (userAgent.Contains("iPhone") || userAgent.Contains("Android") || userAgent.Contains("Googlebot"))
                    {
                        sidebarExpanded = false;
                        bodyExpanded = true;
                    }
                }
            }

            examples = MenuService.MenuItems;

            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
            {
                var example = MenuService.FindCurrent(UriHelper.ToAbsoluteUri(UriHelper.Uri));

                await JSRuntime.InvokeVoidAsync("setTitle", MenuService.TitleFor(example));
            }
        }

        void FilterPanelMenu(ChangeEventArgs args)
        {
            var term = args.Value.ToString();

            examples = MenuService.Filter(term);
        }

        void ChangeTheme(object value)
        {
            ThemeState.CurrentTheme = value.ToString();
            UriHelper.NavigateTo(UriHelper.ToAbsoluteUri(UriHelper.Uri).ToString());
        }

        void OnUserButtonClick(RadzenSplitButtonItem item, string buttonName)
        {
            if (item != null)
            {
                if (item.Value == "2")
                {
                    baseSessionState.ClearToken();
                }
            }
        }
    }
}
