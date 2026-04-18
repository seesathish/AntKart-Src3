using AK.UserIdentity.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace AK.UserIdentity.Tests.Middleware;

public sealed class ExceptionHandlerMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlerMiddleware>> _loggerMock = new();

    private DefaultHttpContext CreateHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_WhenNoException_PassesThrough()
    {
        var middleware = new ExceptionHandlerMiddleware(_ => Task.CompletedTask, _loggerMock.Object);
        var ctx = CreateHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_Returns401()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new UnauthorizedAccessException("Not allowed"),
            _loggerMock.Object);
        var ctx = CreateHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WhenInvalidOperationException_Returns409()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new InvalidOperationException("Conflict"),
            _loggerMock.Object);
        var ctx = CreateHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task InvokeAsync_WhenKeyNotFoundException_Returns404()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new KeyNotFoundException("Not found"),
            _loggerMock.Object);
        var ctx = CreateHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_Returns500()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new Exception("Unexpected"),
            _loggerMock.Object);
        var ctx = CreateHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }
}
