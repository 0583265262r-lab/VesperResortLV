using System.Diagnostics;
using Hotel.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api;

public class ApiErrors
{
    private readonly RequestDelegate next;
    private readonly ILogger<ApiErrors> logger;
    public ApiErrors(RequestDelegate next, ILogger<ApiErrors> logger) { this.next = next; this.logger = logger; }
    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { await next(context); }
        catch (Exception exception)
        {
            var status = 500; var code = "server_error"; var message = "The request could not be completed. Please try again.";
            if (exception is HotelException hotel) { status = hotel.Status; code = hotel.Code; message = hotel.Message; }
            else if (exception is DbUpdateConcurrencyException) { status = 409; code = "version_conflict"; message = "This record changed. Reload and try again."; }
            else if (exception is DbUpdateException || exception is SqliteException { SqliteErrorCode: 5 or 6 }) { status = 409; code = "write_conflict"; message = "A concurrent change prevented this request. Retry with the same idempotency key."; }
            if (status == 500) logger.LogError(exception, "Request failed with trace {TraceId}", context.TraceIdentifier);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { status, code, message, traceId = context.TraceIdentifier });
            }
        }
        finally { logger.LogInformation("{Method} {Path} returned {Status} in {Elapsed}ms ({TraceId})", context.Request.Method, context.Request.Path, context.Response.StatusCode, timer.ElapsedMilliseconds, context.TraceIdentifier); }
    }
}
