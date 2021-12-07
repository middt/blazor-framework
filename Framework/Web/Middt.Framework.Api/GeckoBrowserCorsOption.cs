//using Microsoft.AspNetCore.Builder;
//using Microsoft.AspNetCore.Hosting;
//using Microsoft.AspNetCore.Http;
//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Threading.Tasks;

//namespace Middt.Framework.Api
//{
//    public class GeckoBrowserCorsOption
//    {
//        private readonly RequestDelegate _next;
//        private readonly IHostingEnvironment _environment;

//        public GeckoBrowserCorsOption(RequestDelegate next, IHostingEnvironment environment)
//        {
//            _next = next;
//            _environment = environment;
//        }

//        public async Task Invoke(HttpContext context)
//        {
//            await this.BeginInvoke(context);
//            await this._next.Invoke(context);
//        }

//        public async Task BeginInvoke(HttpContext context)
//        {
//            if (context.Request.Method == "OPTIONS")
//            {
//                context.Response.Headers.Add("Access-Control-Allow-Origin", new[] { (string)context.Request.Headers["Origin"] });
//                context.Response.Headers.Add("Access-Control-Allow-Headers", new[] { "Origin, X-Requested-With, Content-Type, Accept, Authorization" });
//                context.Response.Headers.Add("Access-Control-Allow-Methods", new[] { "GET, POST, PUT, DELETE, OPTIONS" });
//                context.Response.Headers.Add("Access-Control-Allow-Credentials", new[] { "true" });
//                context.Response.StatusCode = 200;
//                await context.Response.WriteAsync("OK");
//            }
//        }
//    }
//    public static class GeckoBrowserCorsOptionExtensions
//    {
//        public static IApplicationBuilder GeckoBrowserCorsOption(this IApplicationBuilder builder)
//        {
//            return builder.UseMiddleware<GeckoBrowserCorsOption>();
//        }
//    }
//}
