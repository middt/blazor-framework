using Microsoft.AspNetCore.SignalR.Client;
using Suges.Framework.Api.Configuration.Model;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using System;
using System.Threading.Tasks;

namespace Suges.Framework.Common.SignalR
{
    public abstract class BaseSignalRClient<TModel> : IBaseSignalRClient<TModel>
    {
        public Action<TModel> OnReceive { get; set; }

        IBaseConfiguration baseConfiguration;
        IBaseLog baseLog;
        IBaseSessionState baseSessionState;

        public abstract string hubName { get; }

        public BaseSignalRClient(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
        {

            baseConfiguration = _baseConfiguration;
            baseLog = _baseLog;
            baseSessionState = _baseSessionState;


            ApiSettings apiSettings = baseConfiguration.Get<ApiSettings>();
            string hubUrl = apiSettings.URL + hubName;

            HubConnection connection = new HubConnectionBuilder()
                .WithUrl(new Uri(hubUrl), options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(baseSessionState.Token().Result);
                })
                .WithAutomaticReconnect()
                .Build();

            connection.On<TModel>(nameof(this.Receive), Receive);

            connection.StartAsync();
        }

        public Task Receive(TModel model)
        {
            OnReceive?.Invoke(model);
            return Task.CompletedTask;
        }
    }
}
