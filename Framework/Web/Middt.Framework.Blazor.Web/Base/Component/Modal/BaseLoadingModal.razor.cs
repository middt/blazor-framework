namespace Middt.Framework.Blazor.Web.Base.Component.Modal
{
    public partial class BaseLoadingModal: Middt.Framework.Blazor.Web.Base.Component.Modal.BaseModalCode
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
