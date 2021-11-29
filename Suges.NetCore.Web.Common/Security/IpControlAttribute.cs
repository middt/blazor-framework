using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Suges.Framework.Api.Configuration.Model;
using Suges.Framework.Common.Configuration;
using Suges.Framework.Model.Model.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Api.Security
{
    //public class IpControlAttribute : ActionFilterAttribute
    //{
    //    public string CorsName { get; set; } 
    //    public IpControlAttribute()
    //    {
    //    }
    //    public override void OnActionExecuting(ActionExecutingContext context)
    //    {
    //        IBaseConfiguration baseConfiguration = (IBaseConfiguration)context.HttpContext.RequestServices.GetService(typeof(IBaseConfiguration));

    //        //Client'ın IP adresini alıyoruz.
    //        string remoteIp = context.HttpContext.Connection.RemoteIpAddress.ToString();
    //        //Whitelist'te ki tüm IP'leri çekiyoruz.

    //        CorsSettings corsSettings = baseConfiguration.Get<CorsSettings>();
    //        CorsModel corsModel = corsSettings.CorsModelList.First(x => x.Name == CorsName);

    //        if (corsModel != null)
    //        {
    //            //Client IP, whitelist'te var mı kontrol ediyoruz.
    //            if (!corsModel.Url.Where(x => x.Contains(remoteIp)).Any())
    //            {
    //                //Eğer yoksa 403 hatası veriyoruz.
    //                context.Result = new StatusCodeResult((int)HttpStatusCode.Forbidden);
    //                return;
    //            }
    //        }
    //        base.OnActionExecuting(context);
    //    }



    //}
}
