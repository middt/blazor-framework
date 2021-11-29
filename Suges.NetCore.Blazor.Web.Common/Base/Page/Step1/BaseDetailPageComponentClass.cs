using Microsoft.AspNetCore.Components;
using Radzen;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Blazor.Web.Base.Page;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base
{
    public abstract partial class BaseDetailPageComponentClass<TService, TInterface, TModel> : BasePageComponent
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
                where TService : BaseListRefit<TInterface, TModel>
    {
        [Parameter]
        public RenderFragment DetailContent { get; set; }

        [Parameter]
        public string QueryStringID { get; set; }

        public TModel Model { get; set; }

        [Parameter]
        public int? id { get; set; }

        [Inject]
        public TService Service { get; set; }


        [Parameter]
        public string GetMethod { get; set; } = "GetById";

        [Parameter]
        public bool IsFirstLoad { get; set; } = true;

        public virtual async Task Search()
        {
            ExecuteMethod(() =>
            {
                Type serviceType = Service.GetType();
                //  MethodInfo getMethod = serviceType.GetMethod(GetMethod);
                MethodInfo getMethod = serviceType.GetMethods()
      //narrow the search before doing 'Single()'
      .Single(mi => mi.Name == GetMethod
                 && mi.GetParameters().Length == 1);

                BaseResponseDataModel<TModel> result = null;
                if (id.HasValue)
                    result = getMethod.Invoke(Service, new object[] { id }) as BaseResponseDataModel<TModel>;
                else
                    result = getMethod.Invoke(Service, null) as BaseResponseDataModel<TModel>;

                if (result.Result == Framework.Model.Model.Enumerations.ResultEnum.Success)
                {
                    Model = result.Data;
                }
                else
                {
                    Log.Error(result.ErrorText);
                    Notification.ShowErrorMessage("Bilgiler kaydedilirken bir hata oluştu.", result.MessageList.FirstOrDefault());
                }
            });
        }
    }
}