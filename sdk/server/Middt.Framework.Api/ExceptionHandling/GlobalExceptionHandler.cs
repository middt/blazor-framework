using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Middt.Framework.Api.ExceptionHandling;

/// <summary>
/// Converts unhandled exceptions into RFC 9457 ProblemDetails responses. Exception details are only
/// exposed in Development; everywhere else clients get a generic message plus a trace id for correlation.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IWebHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "İstek iptal edildi."),
            _ when exception.GetType().Name == "DbUpdateConcurrencyException" => (StatusCodes.Status409Conflict, "Kayıt başka bir işlem tarafından değiştirildi."),
            _ when exception.GetType().Name == "DbUpdateException" => (StatusCodes.Status409Conflict, "Kayıt veritabanına yazılamadı."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Kayıt bulunamadı."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Bu işlem için yetkiniz yok."),
            // Unreadable body, wrong JSON shape, too large: ASP.NET Core already knows the status (400, 413, …).
            BadHttpRequestException badRequest => (badRequest.StatusCode, "İstek geçersiz."),
            ArgumentException or FormatException => (StatusCodes.Status400BadRequest, "İstek geçersiz."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu."),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            LogHandled(logger, exception, status, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = environment.IsDevelopment() ? exception.Message : null,
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request {Method} {Path} failed with {Status}")]
    private static partial void LogHandled(ILogger logger, Exception exception, int status, string method, string path);
}
