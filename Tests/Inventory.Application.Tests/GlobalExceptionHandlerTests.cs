using System.IO;
using System.Text.Json;
using Inventory.Domain.Exceptions;
using Inventory.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Application.Tests;

public class GlobalExceptionHandlerTests
{
    private class DummyHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Inventory";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private class DummyLogger : ILogger<GlobalExceptionHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Theory]
    [InlineData(typeof(DomainException), 422, "Unprocessable Entity")]
    [InlineData(typeof(NotFoundException), 404, "Not Found")]
    [InlineData(typeof(ConcurrencyException), 409, "Conflict")]
    public async Task TryHandleAsync_ShouldMapExceptionsToExpectedStatusCodes(
        Type exceptionType,
        int expectedStatusCode,
        string expectedTitle)
    {
        // Arrange
        var environment = new DummyHostEnvironment { EnvironmentName = Environments.Development };
        var handler = new GlobalExceptionHandler(new DummyLogger(), environment);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        Exception exception = exceptionType switch
        {
            Type t when t == typeof(DomainException) => new DomainException("Domain violation"),
            Type t when t == typeof(NotFoundException) => new NotFoundException("Item", 123),
            Type t when t == typeof(ConcurrencyException) => new ConcurrencyException("Rowversion mismatch"),
            _ => new InvalidOperationException()
        };

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(problemDetails);
        Assert.Equal(expectedStatusCode, problemDetails.Status);
        Assert.Equal(expectedTitle, problemDetails.Title);
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_ShouldMapTo409Conflict()
    {
        // Arrange
        var environment = new DummyHostEnvironment { EnvironmentName = Environments.Development };
        var handler = new GlobalExceptionHandler(new DummyLogger(), environment);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var exception = new DbUpdateConcurrencyException("Concurrency conflict");

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_InProduction_ShouldReturnGenericMessage()
    {
        // Arrange
        var environment = new DummyHostEnvironment { EnvironmentName = Environments.Production };
        var handler = new GlobalExceptionHandler(new DummyLogger(), environment);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var exception = new Exception("Database credentials or raw sql failure");

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(problemDetails);
        Assert.NotNull(problemDetails.Detail);
        Assert.Equal("An unexpected error occurred while processing your request.", problemDetails.Detail);
        Assert.DoesNotContain("credentials", problemDetails.Detail);
    }
}
