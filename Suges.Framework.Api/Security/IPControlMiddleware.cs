namespace Suges.Framework.Api.Security
{
    //public class IPControlMiddleware
    //{
    //    readonly RequestDelegate _next;
    //    IBaseConfiguration baseConfiguration;
    //    public IPControlMiddleware(RequestDelegate next, IBaseConfiguration _baseConfiguration)
    //    {
    //        baseConfiguration = _baseConfiguration;
    //        _next = next;
    //    }
    //    public async Task Invoke(HttpContext context)
    //    {
    //        if (context.Request.Path.HasValue && !context.Request.Path.Value.Contains("/swagger/"))
    //        {
    //            //Client'ın IP adresini alıyoruz.
    //            string remoteIp = context.Connection.RemoteIpAddress.ToString();
    //            //Whitelist'te ki tüm IP'leri çekiyoruz.

    //            CorsSettings corsSettings = baseConfiguration.Get<CorsSettings>();
    //            CorsModel corsModel = corsSettings.CorsModelList.First(x => x.Name == "default");

    //            if (corsModel != null)
    //            {
    //                //Client IP, whitelist'te var mı kontrol ediyoruz.
    //                if (!corsModel.Url.Where(x => x.Contains(remoteIp)).Any())
    //                {
    //                    //Eğer yoksa 403 hatası veriyoruz.
    //                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
    //                    await context.Response.WriteAsync("Bu IP'nin erişim yetkisi yoktur.");
    //                    return;
    //                }
    //            }   
    //        }

    //        await _next.Invoke(context);
    //    }
    //}
}
