using Microsoft.AspNetCore.Components;
using Radzen;
using Suges.Framework.Common.Model.Data;
using Suges.Framework.Common.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Page
{
    public partial class BaseDetailPage<TService, TInterface, TModel> : BaseDetailPageComponentClass<TService, TInterface, TModel>
        where TModel : BaseRequestModel, new()
        where TInterface : IBaseListRefit<TModel>
                where TService : BaseListRefit<TInterface, TModel>
    {
    }
}