using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hotel.Api;
using Hotel.Api.Data;
using Hotel.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://localhost:5080");
}

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("HotelDatabase")
    ?? "Data Source=.local/vesper.db;Default Timeout=30";

builder.Services.AddDbContext<HotelDbContext>(options =>
{
    if (databaseProvider == "MySql")
    {
        options.UseMySQL(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

builder.Services.AddIdentityCore<HotelUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<HotelDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

var keysPath = Path.GetFullPath(
    builder.Configuration["DataProtection:Directory"] ?? ".local/keys"
);

Directory.CreateDirectory(keysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("VesperHotel")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Set Jwt:Key to a random secret of at least 32 bytes."
    );
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "VesperHotel",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "VesperHotel.Web",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),
            ClockSkew = TimeSpan.FromSeconds(15)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<WeatherService>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Weather:BaseUrl"] ?? "https://api.open-meteo.com"
    );
    client.Timeout = TimeSpan.FromSeconds(8);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        );
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
            );

        return new BadRequestObjectResult(new
        {
            status = 400,
            code = "validation_failed",
            message = "Check the submitted fields.",
            errors
        });
    };
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration["Frontend:Url"] ?? "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        );
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (builder.Configuration.GetValue("Workers:RunInApi", true))
{
    builder.Services.AddHostedService<LocalHotelWorker>();
}

var app = builder.Build();

app.UseMiddleware<ApiErrors>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/health", async (HotelDbContext db) =>
{
    var connected = await db.Database.CanConnectAsync();

    if (!connected)
    {
        return Results.Json(
            new { status = "unhealthy", database = "unavailable" },
            statusCode: 503
        );
    }

    return Results.Ok(new
    {
        status = "healthy",
        database = databaseProvider
    });
});

app.MapControllers();

await DatabaseSeeder.InitializeAsync(app.Services);
await app.RunAsync();

public partial class Program { }
