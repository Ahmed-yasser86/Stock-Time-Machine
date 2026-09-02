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
            Exceptions.InvalidHistoricalDateException =>
                (HttpStatusCode.BadRequest, exception.Message),
            Exceptions.UnsupportedCompanyException =>
                (HttpStatusCode.NotFound, exception.Message),
            Exceptions.HistoricalDataNotFoundException =>
                (HttpStatusCode.NotFound, exception.Message),
            Exceptions.ExternalProviderException =>
                (HttpStatusCode.ServiceUnavailable, "An external data provider is temporarily unavailable. Please try again later."),
            Exceptions.RateLimitExceededException =>
                (HttpStatusCode.TooManyRequests, "Rate limit exceeded. Please try again later."),
            TimeoutException =>
                (HttpStatusCode.GatewayTimeout, "The request timed out. Please try again later."),
            _ =>
                (HttpStatusCode.InternalServerError, "An unexpected error occurred. Please try again later.")
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
