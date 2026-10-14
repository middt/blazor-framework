using Middt.Framework.Ui.Rendering;

namespace Middt.Framework.Tests.Ui;

/// <summary>
/// <see cref="UiUrl"/>: browsers strip tabs/newlines (and trim surrounding whitespace/control characters) while parsing a URL,
/// so <c>"/\t/evil.example"</c> navigates to the protocol relative <c>//evil.example</c>.
/// </summary>
public sealed class UiUrlTests
{
    public static TheoryData<string> BrowserNormalisedOffSiteUrls =>
    [
        "/\t/evil.example",
        "/\n/evil.example",
        "/\r\n/evil.example",
        "/\t\\evil.example",
        "\t//evil.example",
        " //evil.example",
        "/\u000B/evil.example",
        "/\u0000/evil.example",
        "/\u00A0/evil.example",
        "/\u2028/evil.example",
        "/ui/orders\t",
        "/ui/orders ",
        "https://example.com/\tx",
    ];

    [Theory]
    [MemberData(nameof(BrowserNormalisedOffSiteUrls))]
    public void Control_characters_and_whitespace_are_rejected(string url)
    {
        UiUrl.IsLocal(url).Should().BeFalse();
        UiUrl.IsSafe(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    public void Protocol_relative_urls_are_rejected(string url)
    {
        UiUrl.IsLocal(url).Should().BeFalse();
        UiUrl.IsSafe(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ui/orders?id=1&q=a%20b")]
    [InlineData("/ürün/5")]
    [InlineData("/a#frag")]
    [InlineData("/ /not-off-site")] // browsers encode inner spaces (%20), they do not strip them
    [InlineData("/orders?q=a b")]
    public void Local_paths_are_accepted(string url)
    {
        UiUrl.IsLocal(url).Should().BeTrue();
        UiUrl.IsSafe(url).Should().BeTrue();
    }
}
