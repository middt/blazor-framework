using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Service;

namespace Middt.Sample.Common.Service
{
    public class WeatherForecastService : BaseListRefit<IWeatherForecastService, WeatherForecastModel>
    {
        public override string controllerName => "WeatherForecast";
        public WeatherForecastService(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
            : base(_baseConfiguration, _baseLog, _baseSessionState)
        {

        }
    }
}