using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;

namespace Vora.Api.Middleware;

public class VoraGlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<VoraGlobalExceptionHandler> _logger;

    public VoraGlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IWebHostEnvironment environment,
        ILogger<VoraGlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad request"),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            OperationCanceledException => (499, "Client closed request"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed: {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        var details = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = ResolveDetail(exception, status),
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path
        };

        if (_environment.IsDevelopment() && status >= 500)
        {
            details.Extensions["exception"] = exception.GetType().Name;
            details.Extensions["stackTrace"] = exception.ToString();
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = details
        });
    }

    private string ResolveDetail(Exception exception, int status)
    {
        if (status >= 500 && !_environment.IsDevelopment())
        {
            return "An unexpected error occurred. Check server logs for details.";
        }
        return exception.Message;
    }
}
