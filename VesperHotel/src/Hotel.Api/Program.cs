using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hotel.Api;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://localhost:5080");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHotel(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
    new BadRequestObjectResult(new { status = 400, code = "validation_failed", message = "Check the submitted fields.", traceId = context.HttpContext.TraceIdentifier,
        errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()) }));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration["Frontend:Url"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
if (builder.Configuration.GetValue("Workers:RunInApi", true)) builder.Services.AddHostedService<LocalHotelWorker>();
var app = builder.Build();
app.UseMiddleware<ApiErrors>();
app.UseStatusCodePages(async context =>
{
    var code = context.HttpContext.Response.StatusCode;
    await context.HttpContext.Response.WriteAsJsonAsync(new { status = code, code = "http_" + code, message = code == 401 ? "Please sign in." : code == 403 ? "Access denied." : "The request could not be completed.", traceId = context.HttpContext.TraceIdentifier });
});
app.UseCors(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.UseSwagger(); app.UseSwaggerUI();
app.MapGet("/api/health", async (HotelDbContext db, IConfiguration config) =>
{
    var connected = await db.Database.CanConnectAsync();
    return connected ? Results.Ok(new { status = "healthy", database = config["Database:Provider"] ?? "Sqlite", messagingMode = config["Messaging:Mode"] ?? "Local" }) : Results.Json(new { status = "unhealthy", database = "unavailable", messagingMode = config["Messaging:Mode"] ?? "Local" }, statusCode: 503);
});
app.MapControllers();
await DatabaseSeeder.InitializeAsync(app.Services);
await app.RunAsync();
public partial class Program { }
