using Microsoft.AspNetCore.Components;
using Middt.Framework.Blazor.Web.Base.Page;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Common.Service;
using Middt.Framework.Model.Model.Enumerations;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base
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


        [Parameter]
        public Action OnAfterSearch { get; set; }
        [Parameter]
        public Action OnBeforeSearch { get; set; }


        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                if (!id.HasValue && !string.IsNullOrEmpty(NavigationManager.QueryString(QueryStringID)))
                {
                    id = Convert.ToInt32(NavigationManager.QueryString(QueryStringID));
                }


                if (IsFirstLoad)
                {
                    Search();
                }
            }
        }



        public virtual async Task Search()
        {
            ExecuteMethod(() =>
            {
                BeforeSearch();
                OnBeforeSearch?.Invoke();

                Type serviceType = Service.GetType();
                //  MethodInfo getMethod = serviceType.GetMethod(GetMethod);
                MethodInfo getMethod = serviceType.GetMethods()
      //narrow the search before doing 'Single()'
      .Single(mi => mi.Name == GetMethod
                 && mi.GetParameters().Length == 1);

                BaseResponseDataModel<TModel> SearchResultModel = null;
                if (id.HasValue)
                    SearchResultModel = getMethod.Invoke(Service, new object[] { id }) as BaseResponseDataModel<TModel>;
                else
                    SearchResultModel = getMethod.Invoke(Service, null) as BaseResponseDataModel<TModel>;


                if (SearchResultModel.Result != ResultEnum.Success)
                {
                    Log.Error(SearchResultModel.ErrorText);
                    Notification.ShowErrorMessage("Arama yapılırken bir hata oluştu.", SearchResultModel.MessageList.FirstOrDefault());
                }
                else
                {
                    if (SearchResultModel.Data == null)
                    {
                        Notification.ShowInfoMessage("Bilgi", "Kayıt bulunamadı.");
                    }
                    else
                    {
                        Model = SearchResultModel.Data;
                    }
                }

                AfterSearch();
                OnAfterSearch?.Invoke();
            });
        }

        protected virtual void AfterSearch()
        {

        }
        public virtual void BeforeSearch()
        {
;
        }

    }
}