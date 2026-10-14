using Middt.Framework.Common.Web;

namespace Middt.Framework.Tests.Security;

public sealed class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/a/b?x=1")]
    [InlineData("/a#frag")]
    [InlineData("~/x")]
    [InlineData("~/")]
    public void Local_urls_are_accepted(string url)
    {
        LocalUrl.IsLocal(url).Should().BeTrue();
        LocalUrl.OrDefault(url).Should().Be(url);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com/path")]
    [InlineData("evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/\t/evil")]
    [InlineData("/\r\n/evil")]
    [InlineData("~//evil")]
    [InlineData("~/\\evil")]
    [InlineData("~")]
    [InlineData("\\\\evil.com")]
    [InlineData("/ürün")]
    [InlineData("/a\u00A0b")]
    [InlineData("/a b")]
    [InlineData("/\u007F/evil")]
    [InlineData("/\u2028/evil")]
    public void Non_local_urls_are_rejected(string? url)
    {
        LocalUrl.IsLocal(url).Should().BeFalse();
        LocalUrl.OrDefault(url).Should().Be("/");
        LocalUrl.OrDefault(url, "/home").Should().Be("/home");
    }

    [Fact]
    public void Percent_encoded_non_ascii_paths_are_accepted()
    {
        LocalUrl.IsLocal("/%C3%BCr%C3%BCn?q=a%20b").Should().BeTrue();
    }
}
