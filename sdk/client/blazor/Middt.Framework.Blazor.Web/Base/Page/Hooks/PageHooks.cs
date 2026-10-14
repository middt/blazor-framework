namespace Middt.Framework.Blazor.Web.Base.Page;

/// <summary>Lifecycle callbacks a code-behind page (Step2 classes) receives from the framework page it hosts.</summary>
public interface IPageHooks
{
    Task OnBeforeSearch();

    Task OnAfterSearch();
}

/// <summary>Additional callbacks for CRUD pages.</summary>
public interface ICrudPageHooks : IPageHooks
{
    Task OnBeforeModalOpen();

    Task OnAfterModalClose();
}
