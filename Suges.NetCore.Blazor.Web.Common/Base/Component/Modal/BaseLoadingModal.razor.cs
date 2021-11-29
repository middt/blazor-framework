using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Base.Component.Modal
{
    public partial class BaseLoadingModal : BaseModalCode

    {
        public BaseModal baseModal { get; set; }

        public void Open()
        {
            baseModal.Open();
        }

        public void Close()
        {
            baseModal.Close();
        }
    }
}
