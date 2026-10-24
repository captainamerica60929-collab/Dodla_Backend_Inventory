using System.Diagnostics;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var (statusCode, title, detail) = exception switch
        {
            DomainException domainEx =>  (
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Entity",
                domainEx.Message),

            UnauthorizedException unauthorizedEx => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                unauthorizedEx.Message),

            NotFoundException notFoundEx => (
                StatusCodes.Status404NotFound,
                "Not Found",
                notFoundEx.Message),

            KeyNotFoundException keyNotFoundEx => (
                StatusCodes.Status404NotFound,
                "Not Found",
                keyNotFoundEx.Message),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "A concurrency conflict occurred. The resource was modified by another operation."),

            ConcurrencyException customConcurrencyEx => (
                StatusCodes.Status409Conflict,
                "Conflict",
                customConcurrencyEx.Message),

            ArgumentException argEx => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                argEx.Message),

            BadHttpRequestException badHttpEx => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                badHttpEx.Message),

            System.Data.Common.DbException dbEx => (
                StatusCodes.Status500InternalServerError,
                "Database Error",
                _environment.IsDevelopment()
                    ? dbEx.ToString()
                    : "A database error occurred while processing the request. Check parameters and logs."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                _environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred while processing your request.")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled server error {TraceId}: {Message}", traceId, exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled domain/client exception {StatusCode} {TraceId}: {Message}", statusCode, traceId, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
