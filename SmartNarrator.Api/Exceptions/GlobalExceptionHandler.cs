using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Api.Options;

namespace SmartNarrator.Api.Exceptions;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment,
    IOptions<ApiOptions> apiOptions) : IExceptionHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method,
            httpContext.Request.Path.Value);

        var expose = environment.IsDevelopment() || apiOptions.Value.ExposeExceptionDetails;
        var dto = ApiClientErrorDto.From(exception, expose, "Unexpected server error");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(dto, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
