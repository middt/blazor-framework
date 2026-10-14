using System.Reflection;

namespace Middt.Framework.Tests.Architecture;

/// <summary>
/// Guards the layering described in docs/architecture.md: contracts carry no UI/ASP.NET technology and the backend never
/// depends on a frontend, so another frontend (e.g. Angular) can be added without touching the backend.
/// </summary>
public sealed class LayeringTests
{
    private static readonly string[] FrontendAssemblies =
    [
        "Middt.Framework.Client",
        "Middt.Framework.Bff",
        "Middt.Framework.Blazor.Web",
        "Middt.Framework.Workflow.Razor",
        "Radzen.Blazor",
        "Syncfusion.Blazor",
    ];

    public static TheoryData<string> Contracts() =>
        ["Middt.Framework.Model", "Middt.Framework.Expressions", "Middt.Framework.Ui.Abstractions"];

    public static TheoryData<string> Backend() =>
        [
            "Middt.Framework.Common",
            "Middt.Framework.Data",
            "Middt.Framework.Api",
            "Middt.Framework.Workflow",
            "Middt.Framework.Ui.Server",
            "Middt.Framework.Plugin.Redis",
            "Middt.Framework.Plugin.Rebus",
            "Middt.Framework.Plugin.SyncfusionExport",
        ];

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Contracts_do_not_depend_on_aspnetcore_or_frontends(string assembly) =>
        References(assembly).Should().NotContain(r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || IsFrontend(r));

    [Theory]
    [MemberData(nameof(Backend))]
    public void Backend_does_not_depend_on_frontends(string assembly) =>
        References(assembly).Should().NotContain(r => IsFrontend(r));

    [Fact]
    public void Sample_common_does_not_depend_on_client_or_frontends() =>
        References("Middt.Sample.Common").Should().NotContain(r => IsFrontend(r));

    [Fact]
    public void Sample_api_backend_does_not_depend_on_client_or_blazor_web() =>
        References("Middt.Sample.Api").Should().NotContain(r => IsClientOrBlazorWeb(r));

    private static bool IsClientOrBlazorWeb(string r) =>
        r is "Middt.Framework.Client" or "Middt.Framework.Bff" or "Middt.Framework.Blazor.Web" or "Radzen.Blazor";

    [Fact]
    public void Client_sdk_does_not_depend_on_backend_or_ui_technology() =>
        References("Middt.Framework.Client").Should().NotContain(r => IsBackendOrUiTechnology(r));

    [Theory]
    [InlineData("Middt.Framework.Bff")]
    [InlineData("Middt.Framework.Blazor.Web")]
    [InlineData("Middt.Framework.Workflow.Razor")]
    public void Frontends_do_not_depend_on_backend_engines(string assembly) =>
        References(assembly).Should().NotContain(r => IsBackendEngine(r));

    // Expression trees (NotContain) cannot contain 'is' patterns, hence the helper methods.
    private static bool IsBackendOrUiTechnology(string r) =>
        r is "Middt.Framework.Common" or "Middt.Framework.Data" or "Middt.Framework.Api" or "Middt.Framework.Workflow" or "Middt.Framework.Ui.Server"
        || r.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) || IsFrontend(r);

    private static bool IsBackendEngine(string r) =>
        r is "Middt.Framework.Data" or "Middt.Framework.Api" or "Middt.Framework.Workflow" or "Middt.Framework.Ui.Server" || r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);

    private static bool IsFrontend(string reference) =>
        FrontendAssemblies.Any(f => reference == f || reference.StartsWith(f + ".", StringComparison.Ordinal));

    private static List<string> References(string assembly) =>
        [.. Assembly.Load(assembly).GetReferencedAssemblies().Select(a => a.Name!)];
}
