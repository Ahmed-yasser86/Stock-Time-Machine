using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace StockTimeMachine.Web.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, detail) = exception switch
        {
            InvalidHistoricalDateException =>
                (HttpStatusCode.BadRequest, exception.Message),
            UnsupportedCompanyException =>
                (HttpStatusCode.NotFound, exception.Message),
            HistoricalDataNotFoundException =>
                (HttpStatusCode.NotFound, exception.Message),
            ExternalProviderException =>
                (HttpStatusCode.ServiceUnavailable, "We were unable to retrieve market data at this time. Please try again."),
            RateLimitExceededException =>
                (HttpStatusCode.TooManyRequests, "We've reached our data provider limit. Please wait a moment and try again."),
            TimeoutException =>
                (HttpStatusCode.GatewayTimeout, "The investigation took too long. Please try again."),
            TaskCanceledException =>
                (HttpStatusCode.GatewayTimeout, "The investigation took too long. Please try again."),
            _ =>
                (HttpStatusCode.InternalServerError, "Something went wrong. Please try again.")
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = GetTitle(statusCode),
            Detail = detail,
            Type = $"https://httpstatuses.com/{(int)statusCode}",
        };
        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    private static string GetTitle(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => "Bad Request",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.ServiceUnavailable => "Service Unavailable",
        HttpStatusCode.TooManyRequests => "Too Many Requests",
        HttpStatusCode.GatewayTimeout => "Gateway Timeout",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        _ => "Error"
    };
}
