using Microsoft.AspNetCore.Http;

namespace AK.BuildingBlocks.Middleware;

// Correlation ID middleware: attaches a unique request ID to every log entry for a request,
// making it possible to trace a single user request across all microservices in Kibana.
//
// How it works:
//   - If the incoming request already has X-Correlation-Id (set by the API Gateway or client),
//     reuse that same ID so the chain of logs across services is traceable end-to-end.
//   - If not, generate a new GUID for this request.
//   - The ID is echoed back in the response header so clients can reference it in support tickets.
//   - Serilog.Context.LogContext.PushProperty adds CorrelationId to every log event while
//     the request is in scope, so every log line includes it automatically.
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId))
            correlationId = Guid.NewGuid().ToString();

        // Echo the ID back in the response so callers can reference it.
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // PushProperty is scoped to the `using` block — once the request completes,
        // the property is removed from the logging context automatically.
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId.ToString()))
            await next(context);
    }
}
