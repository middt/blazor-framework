using System;

namespace Suges.Template.Api.Model
{
    public partial class YenilemeToken
    {
        public long YenilemeTokenId { get; set; }
        public long? KullaniciId { get; set; }
        public string Token { get; set; }
        public string YenilemeToken1 { get; set; }
        public DateTime? EklenmeTarihi { get; set; }
    }
}
