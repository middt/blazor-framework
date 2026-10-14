namespace Middt.Sample.Api.Model;

public class YenilemeToken
{
    public long YenilemeTokenId { get; set; }
    public long? KullaniciId { get; set; }
    public string? Token { get; set; }
    public string? YenilemeToken1 { get; set; }
    public DateTimeOffset? EklenmeTarihi { get; set; }
}
