using Microsoft.AspNetCore.Components;
using Radzen;
using Suges.Framework.Blazor.Web.Base.Component.Modal;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using Suges.Framework.Model.Model.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public partial class BaseDetailCrudPage<TService, TInterface, TModel> : BaseDetailCrudPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseCrudRefit<TModel>
        where TService : BaseCrudRefit<TInterface, TModel>
    {
        protected override void CustomOnAfterRenderAsync(bool firstRender)
        {
            base.CustomOnAfterRenderAsync(firstRender);


            if (firstRender)
            {
                if (Model != null)
                {
                    IsUpdate = true;
                }
                else
                {
                    IsUpdate = false;
                    New();
                }
            }
        }
    }
}