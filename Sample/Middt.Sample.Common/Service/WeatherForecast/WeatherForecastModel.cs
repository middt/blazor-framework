using Middt.Framework.Common.Model.Data;
using System;

namespace Middt.Sample.Common.Service
{
    public class WeatherForecastModel : BaseRequestModel
    {
        public DateTime Date { get; set; }

        public int TemperatureC { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public string Summary { get; set; }




        // [Required]
        //[StringLength(10, ErrorMessage = "Name is too long.")]
        public int? Name { get; set; }
    }
}
