using Microsoft.AspNetCore.Components;
using Radzen.Blazor;
using Suges.Template.BlazorServer.Model;

namespace Suges.Template.BlazorServer.Shared
{
    public partial class LayoutWithoutAuth : LayoutComponentBase
    {

        [Inject]
        ThemeState ThemeState { get; set; }


        dynamic themes = new[]
        {
    new { Text = "Default", Value = "default"},
    new { Text = "Dark", Value="dark" },
    new { Text = "Software", Value = "software"},
    new { Text = "Humanistic", Value = "humanistic" }
    };


        string Theme
        {
            get
            {
                return $"{ThemeState.CurrentTheme}.css";
            }
        }
        RadzenSidebar sidebar0;
        RadzenBody body0;

        bool sidebarExpanded = true;
        bool bodyExpanded = false;
        protected override void OnInitialized()
        {

            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        }
    }
}
