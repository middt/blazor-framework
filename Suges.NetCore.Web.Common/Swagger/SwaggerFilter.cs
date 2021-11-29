
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Suges.Framework.Api.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;


namespace Suges.Framework.Api
{
    public class SwaggerFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
#if !DEBUG
            foreach (ApiDescription apiDescription in context.ApiDescriptions)
            {
                SwaggerTagAttribute swaggerTagAttribute = apiDescription.CustomAttributes().OfType<SwaggerTagAttribute>().FirstOrDefault();


                if (swaggerTagAttribute == null || !swaggerTagAttribute.ShownInRelease)
                {
                    var route = "/" + apiDescription.RelativePath.TrimEnd('/');
                    swaggerDoc.Paths.Remove(route);
                }
            }
#endif
        }
    }
}
