namespace Middt.Framework.Tests.Ui;

/// <summary>Both frontends render the same layout markup, so they must ship the same <c>middt-ui.css</c>.</summary>
public sealed class SharedStylesTests
{
    [Fact]
    public void Blazor_and_ui_core_ship_identical_layout_styles()
    {
        var root = RepositoryRoot();
        var uiCore = File.ReadAllText(Path.Combine(root, "sdk", "client", "ui-core", "styles", "middt-ui.css"));
        var blazor = File.ReadAllText(Path.Combine(root, "sdk", "client", "Middt.Framework.Blazor", "wwwroot", "middt-ui.css"));

        blazor.Should().Be(uiCore, "copy sdk/client/ui-core/styles/middt-ui.css to the Blazor wwwroot (or the other way round)");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
