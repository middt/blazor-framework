using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Middt.Framework.Blazor.Web.Base.Component.Captcha;

/// <summary>
/// Simple server rendered captcha. The image is an inline SVG (no System.Drawing, works on Linux containers)
/// and the word is generated with a cryptographic RNG. Validate with <see cref="IsValid"/>.
/// </summary>
public class BaseCaptcha : ComponentBase
{
    // Ambiguous characters (0/O, 1/l/I) are excluded on purpose.
    private const string Alphabet = "ABCDEFGHJKLMNPRSTUVYZabcdefghjkmnprstuvyz23456789";

    private string captchaWord = string.Empty;
    private string svg = string.Empty;

    [Parameter]
    public EventCallback<string> CaptchaWordChanged { get; set; }

    [Parameter]
    public int Width { get; set; } = 170;

    [Parameter]
    public int Height { get; set; } = 40;

    [Parameter]
    public int CharNumber { get; set; } = 4;

    [Parameter]
    public bool CaseSensitive { get; set; } = true;

    public bool IsValid(string? text) =>
        !string.IsNullOrEmpty(text) &&
        string.Equals(text, captchaWord, CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public async Task RefreshAsync()
    {
        CreateCaptcha();
        StateHasChanged();
        await CaptchaWordChanged.InvokeAsync(captchaWord);
    }

    protected override void OnInitialized() => CreateCaptcha();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "middt-captcha");
        builder.AddMarkupContent(2, svg);
        builder.OpenElement(3, "button");
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", "btn btn-refresh");
        builder.AddAttribute(6, "aria-label", "Yeni kod");
        builder.AddAttribute(7, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, RefreshAsync));
        builder.AddContent(8, "↻");
        builder.CloseElement();
        builder.CloseElement();
    }

    private void CreateCaptcha()
    {
        captchaWord = RandomNumberGenerator.GetString(Alphabet, CharNumber);
        svg = RenderSvg(captchaWord);
    }

    private string RenderSvg(string word)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" role=\"img\" aria-label=\"captcha\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fffdfb\"/>");

        for (var i = 0; i < 4; i++)
        {
            sb.Append(inv, $"<line x1=\"{RandomNumberGenerator.GetInt32(Width)}\" y1=\"{RandomNumberGenerator.GetInt32(Height)}\" x2=\"{RandomNumberGenerator.GetInt32(Width)}\" y2=\"{RandomNumberGenerator.GetInt32(Height)}\" stroke=\"#{RandomNumberGenerator.GetInt32(0x1000000):x6}\" stroke-width=\"1\"/>");
        }

        var step = (double)(Width - 10) / Math.Max(word.Length, 1);
        var fontSize = Height * 0.6;
        for (var i = 0; i < word.Length; i++)
        {
            var x = 5 + (i * step) + (step / 4);
            var y = (Height / 2.0) + (fontSize / 3);
            var angle = RandomNumberGenerator.GetInt32(-20, 21);
            sb.Append(inv, $"<text x=\"{x:0.#}\" y=\"{y:0.#}\" font-family=\"Courier, monospace\" font-weight=\"bold\" font-size=\"{fontSize:0.#}\" transform=\"rotate({angle} {x:0.#} {y:0.#})\">{WebUtility.HtmlEncode(word[i].ToString())}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
