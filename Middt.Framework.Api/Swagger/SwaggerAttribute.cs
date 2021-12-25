using System;

namespace Middt.Framework.Api.Swagger
{
    public class SwaggerTagAttribute : Attribute
    {
        public bool ShownInRelease { get; }

        public SwaggerTagAttribute(bool shownInRelease)
        {
            ShownInRelease = shownInRelease;
        }
    }
}
