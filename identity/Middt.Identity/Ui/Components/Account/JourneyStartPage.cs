using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Middt.Identity.Ui.Journeys;

namespace Middt.Identity.Ui.Components.Account;

/// <summary>
/// Base of the pages that start an anonymous journey (static SSR): starts <see cref="Journey"/> with the local
/// <c>returnUrl</c>, binds it to the browser (<see cref="AuthJourneyCookie"/>) and redirects to the journey page.
/// </summary>
public abstract class JourneyStartPage : ComponentBase
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    [Inject]
    private IAuthJourneyService Journeys { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    /// <summary>Key of the journey to start (<see cref="AuthJourneys"/>).</summary>
    protected abstract string Journey { get; }

    /// <summary>The page is disabled (e.g. registration turned off): renders a message instead of starting.</summary>
    protected virtual bool Disabled => false;

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext is null || Disabled)
        {
            return;
        }

        var handle = await Journeys.StartAsync(Journey, JourneyLinks.CreateStart(ReturnUrl), HttpContext.RequestAborted);
        AuthJourneyCookie.Write(HttpContext, handle);
        HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        Navigation.NavigateTo(MiddtIdentityDefaults.JourneyPath(handle.Id));
    }
}
