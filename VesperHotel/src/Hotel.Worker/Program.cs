using Hotel.Api.Data;
using Hotel.Api.Services;
using Hotel.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var provider = builder.Configuration["Database:Provider"] ?? "MySql";
var connectionString = builder.Configuration.GetConnectionString("HotelDatabase")
    ?? throw new InvalidOperationException("HotelDatabase connection string is missing.");

builder.Services.AddDbContext<HotelDbContext>(options =>
{
    if (provider == "MySql")
    {
        options.UseMySQL(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

builder.Services.AddScoped<BookingService>();
builder.Services.AddHostedService<HotelWorker>();

var host = builder.Build();
await host.RunAsync();
