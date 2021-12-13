using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Middt.Framework.Common.Model.Data;
using System.Linq;

namespace Middt.Framework.Api
{
    public class ValidateModelAttribute : ActionFilterAttribute
    {
        public bool enableValidation { get; set; } = true;
        public ValidateModelAttribute()
        {

        }
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);


            if (enableValidation)
            {
                if (!context.ModelState.IsValid)
                {
                    BaseResponseModel responseModel = new BaseResponseModel();
                    responseModel.Result = Model.Model.Enumerations.ResultEnum.Error;


                    responseModel.MessageList = context.ModelState.Keys
                            .SelectMany(key => context.ModelState[key].Errors.Select(x => x.ErrorMessage))
                            .ToList();

                    context.Result = new OkObjectResult(responseModel);

                }
            }
            else
            {
                if (!context.ModelState.IsValid)
                {
                    context.ModelState.Clear();
                }
            }
        }
    }
}
