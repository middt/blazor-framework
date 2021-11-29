using Middt.Framework.Common.Database.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

#nullable disable

namespace Middt.Template.Api.Model.Database
{
    public partial class Table1
    {
        [QueryInt(IntSearchType.GreaterThanOrEqual)]
        public int Table1Id { get; set; }

        [QueryString(StringSearchType.StartsWith)]
        [Required(ErrorMessage = "Lütfen geçerli bir ad giriniz.")]
        public string Ad { get; set; }
        public int? BoolDeger { get; set; }

        public DateTime? Tarih { get; set; }
    }
}
