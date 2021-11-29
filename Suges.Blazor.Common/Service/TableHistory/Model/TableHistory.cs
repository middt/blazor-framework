using System;
using System.ComponentModel.DataAnnotations.Schema;

#nullable disable

namespace Suges.Template.Api.Model.Database
{
    public partial class TableHistory
    {
        public int Id { get; set; }
        public string Ad { get; set; }
        public double? Fiyat { get; set; }
        public short? Onay { get; set; }
        public DateTime? EklenmeTarihi { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime ValidFrom { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime ValidTill { get; set; }
    }
}
