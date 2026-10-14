using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Middt.Framework.Api;

/// <summary>
/// Opt-in model validation. Automatic <c>[ApiController]</c> validation is disabled by the framework so that
/// endpoints such as <c>GetItems</c>/<c>Delete</c> can receive partially filled models; actions decorated with
/// <c>[ValidateModel]</c> return a 400 ValidationProblemDetails when the model is invalid.
/// Use <c>[ValidateModel(enableValidation = false)]</c> to explicitly ignore validation errors.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ValidateModelAttribute : ActionFilterAttribute
{
#pragma warning disable IDE1006 // kept for source compatibility with earlier framework versions
    public bool enableValidation { get; set; } = true;
#pragma warning restore IDE1006

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        if (!enableValidation)
        {
            context.ModelState.Clear();
            return;
        }

        context.Result = context.Controller is ControllerBase controller
            ? controller.ValidationProblem(context.ModelState)
            : new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
    }
}
