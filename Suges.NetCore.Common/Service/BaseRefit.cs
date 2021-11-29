using Polly;
using RestEase;
using Suges.Framework.Api.Configuration.Model;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Common.Dependency;
using Suges.Framework.Common.Log;
using Suges.Framework.Common.Security;
using Suges.Framework.Common.Security.Refit;
using Suges.Framework.Model.Authentication;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Net.Http.Headers;

namespace Suges.Framework.Common.Service
{
    public abstract class BaseRefit<TInterface>
    {
        public TInterface api;
        protected string baseURL;

        public abstract string controllerName { get; }


        IBaseConfiguration baseConfiguration;
        IBaseLog baseLog;
        IBaseSessionState baseSessionState;

        public BaseRefit(
            IBaseConfiguration _baseConfiguration,
            IBaseLog _baseLog,
            IBaseSessionState _baseSessionState)
        {
            baseConfiguration = _baseConfiguration;
            baseLog = _baseLog;
            baseSessionState = _baseSessionState;



            ApiSettings apiSettings = baseConfiguration.Get<ApiSettings>();

            api = RestClient.For<TInterface>(apiSettings.URL, async (request, cancellationToken) =>
                           {
                               // See if the request has an authorize header
                               var auth = request.Headers.Authorization;
                               if (auth != null)
                               {
                                   request.Headers.Authorization = new AuthenticationHeaderValue(auth.Scheme, baseSessionState.Token().Result);
                               }
                           });


        }

        protected TReturnModel ExecutePolly<TReturnModel>(Func<TReturnModel> action)
        {

            var policy = Policy
               .Handle<Exception>()
               .WaitAndRetry(new[]
                  {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(5)
                  }, (exception, timeSpan, retryCount, context) =>
                  {
                      if (exception.Message.Contains("401 (Unauthorized)"))
                      {
                          ITokenService tokenService = FrameworkDependencyHelper.Instance.Get<ITokenService>();

                          TokenRefreshRequestModel request = new TokenRefreshRequestModel();
                          request.token = baseSessionState.Token().Result;
                          request.refresh_token = baseSessionState.RefreshToken().Result;


                          TokenResponseModel tokenResponseModel = tokenService.RefreshToken(request);

                          if (tokenResponseModel.Result == ResultEnum.Success)
                          {
                              baseSessionState.SetToken(tokenResponseModel);
                          }
                      }
                      else
                      {
                          baseLog.Error(exception.Message);
                      }
                  });


            return policy.Execute<TReturnModel>(action);
        }
    }
}
