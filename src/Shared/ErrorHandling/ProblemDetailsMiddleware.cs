using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kendo.Shared.ErrorHandling;

/// <summary>
/// Middleware that catches unhandled exceptions and returns RFC 7807 Problem Details
/// responses. Also replaces 4xx response bodies with Problem Details JSON.
/// Health check endpoints (/health/live, /health/ready) are skipped.
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip health check endpoints — they return text/plain as designed
        if (context.Request.Path.StartsWithSegments(HealthLivePath, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(HealthReadyPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        try
        {
            await _next(context);

            // If the response already has a 4xx/5xx status code with no body, set Problem Details
            if (context.Response.StatusCode >= 400 && context.Response.StatusCode < 600 &&
                !context.Response.HasStarted)
            {
                var problemDetails = CreateProblemDetails(context, context.Response.StatusCode);
                await WriteProblemDetailsAsync(context, problemDetails);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an application error, don't log as error
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499; // Nginx-style "Client Closed Request"
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing request {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                var (statusCode, title) = MapExceptionToStatusCode(ex);
                var problemDetails = CreateProblemDetails(context, statusCode, title, ex.Message);
                await WriteProblemDetailsAsync(context, problemDetails);
            }
        }
    }

    private static KendoProblemDetails CreateProblemDetails(
        HttpContext context, int statusCode, string? title = null, string? detail = null)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "unknown";

        return new KendoProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = title ?? GetDefaultTitle(statusCode),
            Status = statusCode,
            Detail = detail ?? "An error occurred while processing your request.",
            Instance = context.Request.Path,
            TraceId = traceId
        };
    }

    private static (int StatusCode, string Title) MapExceptionToStatusCode(Exception ex)
    {
        return ex switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            OperationCanceledException or TaskCanceledException
                => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "An error occurred while processing your request.")
        };
    }

    private static string GetDefaultTitle(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
            StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            StatusCodes.Status500InternalServerError => "An error occurred while processing your request.",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            _ => "Error"
        };
    }

    private static async Task WriteProblemDetailsAsync(HttpContext context, KendoProblemDetails problemDetails)
    {
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        var json = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(json, System.Text.Encoding.UTF8);
    }
}
