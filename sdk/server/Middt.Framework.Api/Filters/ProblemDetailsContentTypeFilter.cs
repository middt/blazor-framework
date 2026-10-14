using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Middt.Framework.Api;

/// <summary>
/// Serves <see cref="ProblemDetails"/> results (<c>Problem()</c>, <c>ValidationProblem()</c>, <see cref="ValidateModelAttribute"/>)
/// as <c>application/problem+json</c> (RFC 9457). Without it, <c>[Produces("application/json")]</c> on
/// <see cref="BaseController"/> overwrites the content type of every object result, including problem details.
/// Runs last so that it sees the content types set by <c>[Produces]</c>.
/// </summary>
internal sealed class ProblemDetailsContentTypeFilter : IResultFilter, IOrderedFilter
{
    private const string ProblemJson = "application/problem+json";

    public int Order => int.MaxValue;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails } result)
        {
            result.ContentTypes.Clear();
            result.ContentTypes.Add(ProblemJson);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
