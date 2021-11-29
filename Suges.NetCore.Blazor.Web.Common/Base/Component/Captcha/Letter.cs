using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.Captcha
{
    public class Letter
    {
        public int Angle { get; set; }
        public string Value { get; set; }
        public Font Font { get; set; }
        public Color ForeColor { get; set; }
        public string Family { get; set; }

    }
}
