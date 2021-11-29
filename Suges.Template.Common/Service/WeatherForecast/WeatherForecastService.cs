using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Service;

namespace Suges.Template.Common.Service
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