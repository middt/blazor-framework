using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace Middt.Identity.Journeys;

/// <summary>UI-safe Turkish messages of the journeys.</summary>
internal static partial class JourneyTexts
{
    public const string InvalidCredentials = "Kullanıcı adı veya şifre hatalı.";
    public const string LockedOut = "Çok fazla hatalı deneme yapıldı. Hesabınız geçici olarak kilitlendi, lütfen daha sonra tekrar deneyin.";
    public const string EmailNotConfirmed = "E-posta adresiniz henüz doğrulanmadı.";
    public const string InvalidTotp = "Doğrulama kodu hatalı.";
    public const string InvalidEmailCode = "Doğrulama kodu hatalı veya süresi dolmuş.";
    public const string TooManyCodes = "Çok fazla kod istendi. Lütfen daha sonra tekrar deneyin.";
    public const string RegistrationDisabled = "Yeni kayıt şu anda kapalı.";
    public const string UserNotFound = "Kullanıcı bulunamadı.";
    public const string NotAvailable = "Bu işlem şu anda kullanılamaz.";
    public const string Busy = "İşlem şu anda yapılamıyor, lütfen tekrar deneyin.";
    public const string Unexpected = "Beklenmeyen bir hata oluştu. Lütfen işleme baştan başlayın.";
    public const string Generic = "İşlem tamamlanamadı.";
    public const string EmailCodeSubject = "Doğrulama kodunuz";

    public static string EmailCodeText(string code, TimeSpan lifetime) =>
        string.Create(CultureInfo.InvariantCulture, $"Doğrulama kodunuz: {code}. Kod {(int)lifetime.TotalMinutes} dakika geçerlidir. Bu işlemi siz başlatmadıysanız bu mesajı dikkate almayın.");

    /// <summary>ASP.NET Core Identity errors (English) as UI texts.</summary>
    public static string Describe(IEnumerable<IdentityError> errors, IdentityOptions options) =>
        string.Join(" ", errors.Select(e => e.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateUserName) => "Bu kullanıcı adı zaten kullanılıyor.",
            nameof(IdentityErrorDescriber.DuplicateEmail) => "Bu e-posta adresi zaten kayıtlı.",
            nameof(IdentityErrorDescriber.InvalidUserName) => "Kullanıcı adı geçersiz karakterler içeriyor.",
            nameof(IdentityErrorDescriber.InvalidEmail) => "E-posta adresi geçersiz.",
            nameof(IdentityErrorDescriber.PasswordTooShort) => string.Create(CultureInfo.InvariantCulture, $"Şifre en az {options.Password.RequiredLength} karakter olmalıdır."),
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "Şifre en az bir rakam içermelidir.",
            nameof(IdentityErrorDescriber.PasswordRequiresLower) => "Şifre en az bir küçük harf içermelidir.",
            nameof(IdentityErrorDescriber.PasswordRequiresUpper) => "Şifre en az bir büyük harf içermelidir.",
            nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => "Şifre en az bir özel karakter içermelidir.",
            nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => "Şifre daha fazla farklı karakter içermelidir.",
            nameof(IdentityErrorDescriber.InvalidToken) => "Bağlantının süresi dolmuş. Lütfen işleme baştan başlayın.",
            _ => Generic,
        }).Distinct());

    /// <summary>
    /// Workflow input validation errors (<c>$.password: is required.</c>) as UI texts, using the schema property titles:
    /// <c>Şifre: Bu alan zorunludur.</c>
    /// </summary>
    public static string Validation(string error, System.Text.Json.Nodes.JsonObject? schema)
    {
        var match = ValidationPattern().Match(error);
        if (!match.Success)
        {
            return Generic;
        }

        var property = match.Groups["property"].Value;
        var title = schema?["properties"]?[property]?["title"]?.GetValue<string>() ?? property;
        var text = match.Groups["message"].Value;
        var message = text switch
        {
            "is required." => "Bu alan zorunludur.",
            "is not allowed." => "Bu alana izin verilmiyor.",
            "must be an e-mail address." => "Geçerli bir e-posta adresi giriniz.",
            "has an invalid format." => "Geçersiz biçim.",
            _ when text.StartsWith("must be at least ", StringComparison.Ordinal) =>
                $"En az {Number(text)} karakter olmalıdır.",
            _ when text.StartsWith("must be at most ", StringComparison.Ordinal) =>
                $"En fazla {Number(text)} karakter olmalıdır.",
            _ => "Geçersiz değer.",
        };
        return $"{title}: {message}";
    }

    private static string Number(string text) => NumberPattern().Match(text).Value;

    [GeneratedRegex(@"^\$\.(?<property>[^:.\[]+): (?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidationPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
