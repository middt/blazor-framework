using Swashbuckle.AspNetCore.Filters;

namespace Suges.Framework.Model.Model.Authentication
{
    //class LoginRequestModelSwaggerExample
    //{
    //}
    public class LoginRequestModelSwaggerExample : IExamplesProvider<SwaggerLoginRequestModel>
    {
        public SwaggerLoginRequestModel GetExamples()
        {
            return new SwaggerLoginRequestModel
            {
                username = "kullaniciadiniz",
                password = "sifreniz"

            };
        }
    }


}
