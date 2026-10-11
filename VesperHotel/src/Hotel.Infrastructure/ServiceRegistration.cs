using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hotel.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddHotel(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connection = config.GetConnectionString("HotelDatabase") ?? "Data Source=.local/vesper.db;Default Timeout=30";
        if (provider != "Sqlite" && provider != "MySql") throw new InvalidOperationException("Database:Provider must be Sqlite or MySql.");
        services.AddDbContext<HotelDbContext>(options =>
        {
            if (provider == "MySql") options.UseMySQL(connection, mysql => mysql.MigrationsAssembly("Hotel.Migrations.MySql"));
            else options.UseSqlite(connection);
        });
        services.AddIdentityCore<HotelUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
            options.Password.RequireUppercase = true; options.Password.RequireLowercase = true;
            options.Password.RequireDigit = true; options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<HotelDbContext>().AddSignInManager().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        var keysPath = Path.GetFullPath(config["DataProtection:Directory"] ?? ".local/keys");
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection().SetApplicationName("VesperHotel").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        var key = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Set Jwt:Key to a randomly generated secret of at least 32 bytes. Use the local start script.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = config["Jwt:Issuer"] ?? "VesperHotel", ValidAudience = config["Jwt:Audience"] ?? "VesperHotel.Web",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew = TimeSpan.FromSeconds(15)
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var stamp = context.Principal?.FindFirst("stamp")?.Value;
                    var db = context.HttpContext.RequestServices.GetRequiredService<HotelDbContext>();
                    if (id == null || !await db.Users.AnyAsync(x => x.Id == id && x.SecurityStamp == stamp)) context.Fail("Session no longer valid.");
                }
            };
        });
        services.AddAuthorization();
        services.AddScoped<BookingService>(); services.AddScoped<CatalogService>(); services.AddScoped<AdminService>(); services.AddScoped<AuthService>();
        services.AddMemoryCache();
        services.AddHttpClient<WeatherService>(client =>
        {
            client.BaseAddress = new Uri(config["Weather:BaseUrl"] ?? "https://api.open-meteo.com");
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddHttpContextAccessor();
        return services;
    }
}
