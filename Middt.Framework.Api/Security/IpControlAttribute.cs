namespace Middt.Framework.Api.Security
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
