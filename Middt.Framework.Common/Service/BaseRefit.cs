using Polly;
using RestEase;
using Middt.Framework.Api.Configuration.Model;
using Middt.Framework.Common.Configuration;
using Middt.Framework.Common.Dependency;
using Middt.Framework.Common.Log;
using Middt.Framework.Common.Security;
using Middt.Framework.Common.Security.Refit;
using Middt.Framework.Model.Authentication;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Net.Http.Headers;

namespace Middt.Framework.Common.Service
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
                               string token = baseSessionState.Token().Result;
                               if (auth != null && !string.IsNullOrEmpty(token))
                               {
                                   request.Headers.Authorization = new AuthenticationHeaderValue(auth.Scheme, token);
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


            return policy.Execute <TReturnModel>(action);
        }
    }
}
