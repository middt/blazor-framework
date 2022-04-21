using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.Modal
{
    public partial class BaseLoadingModal : BaseModalCode
    {
        public BaseModal baseModal { get; set; }

        public override async Task Open()
        {
            await baseModal.Open();
        }

        public override async Task Close()
        {
            await baseModal.Close();
        }


    }
}
