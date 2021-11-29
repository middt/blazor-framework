using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Api.Swagger
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
