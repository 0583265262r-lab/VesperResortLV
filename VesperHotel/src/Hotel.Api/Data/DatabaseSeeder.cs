using Hotel.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hotel.Api.Data;

public static class DatabaseSeeder
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (db.Database.IsSqlite())
        {
            var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString());
            if (!string.IsNullOrWhiteSpace(connection.DataSource) && connection.DataSource != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(connection.DataSource))!);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        else
        {
            await db.Database.MigrateAsync();
        }
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HotelUser>>();
        // Seeding runs in API startup. Workers wait for API health before starting.
        foreach (var role in new[] { "Admin", "Guest" }) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        var email = config["Seed:AdminEmail"] ?? "admin@vesper.local";
        var admin = await users.FindByEmailAsync(email);
        if (admin == null)
        {
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Seed:AdminPassword before the first startup. No default admin password is shipped.");
            admin = new HotelUser { UserName = email, Email = email, FirstName = "Vesper", LastName = "Administrator", EmailConfirmed = true };
            var created = await users.CreateAsync(admin, password);
            if (!created.Succeeded) throw new InvalidOperationException("Seed admin could not be created: " + string.Join(" ", created.Errors.Select(x => x.Description)));
            await users.AddToRoleAsync(admin, "Admin");
        }
        if (!await db.Resources.AnyAsync(x => x.Id == "system"))
            db.Resources.Add(new CatalogResource { Id = "system", Slug = "system", Name = "Internal transaction lock", IsActive = false });
        if (!await db.Resources.AnyAsync(x => x.Id != "system"))
        {
            AddRoom(db, "The Vesper Suite", "Above the extraordinary.", "An expansive private retreat with a king bed, separate lounge, and floor-to-ceiling views of the Las Vegas skyline.", 69500, 2, 5, "/images/suite.jpg", new[] { "/images/suite.jpg", "/images/rooms/vesper-lounge.jpg", "/images/rooms/vesper-bathroom.jpg" }, new[] { "King bed", "Skyline views", "Separate lounge", "Marble bathroom", "Espresso bar" });
            AddRoom(db, "Panorama Residence", "A different perspective.", "A generous two-bedroom residence for quiet gatherings, with a dining area and uninterrupted views beyond the Strip.", 129500, 4, 3, "/images/rooms/panorama-living.jpg", new[] { "/images/rooms/panorama-living.jpg", "/images/rooms/panorama-bedroom.jpg", "/images/rooms/panorama-bathroom.jpg" }, new[] { "Two bedrooms", "Private dining", "Panoramic views", "Deep soaking tub" });
            AddRoom(db, "The Penthouse", "Your private Las Vegas.", "Our most expansive residence pairs warm walnut interiors with a private terrace and an exceptional view of the city.", 245000, 6, 2, "/images/rooms/penthouse-living.jpg", new[] { "/images/rooms/penthouse-living.jpg", "/images/rooms/penthouse-terrace.jpg", "/images/rooms/penthouse-bedroom.jpg" }, new[] { "Three bedrooms", "Private terrace", "Dedicated host", "Living and dining rooms" });
            AddActivity(db, "Poolside Cabana", "A slower kind of spectacle.", "A private cabana by the pool, attentive service, and a sunlit afternoon reserved for you.", 9500, 6, 12, "/images/pool.jpg", 20, new[] { "Private cabana", "Pool access", "Refreshments" });
            AddActivity(db, "Desert at Dusk", "Beyond the city lights.", "A hosted desert outlook experience timed for the changing light, with a small group and a relaxed pace.", 18500, 8, 16, "/images/hero.jpg", 23, new[] { "Guided experience", "Small groups", "Sunset views" });
            AddActivity(db, "The Tasting Table", "An evening to remember.", "A seasonal tasting experience in an intimate setting, built around careful ingredients and thoughtful hospitality.", 22500, 8, 14, "/images/suite.jpg", 2, new[] { "Seasonal tasting", "Hosted dining", "Intimate setting" });
            AddTransport(db, "Executive Sedan", "Arrive effortlessly.", "A private chauffeured sedan for airport arrivals, city appointments, and evenings out.", 12500, 3, 3);
            AddTransport(db, "Private SUV", "Room for your plans.", "A spacious chauffeured luxury SUV for your party and luggage, available by the hour.", 18500, 6, 3);
        }
        await db.SaveChangesAsync();
        await AddDemoRoomGalleriesAsync(db);
    }
    private static CatalogResource Resource(string name, ResourceKind kind, string tagline, string description, long cents, int guests, string image, string[] features) => new()
    { Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-'), Kind = kind, Tagline = tagline, Description = description, BasePriceCents = cents, MaxGuests = guests, ImageUrl = image, GalleryImagesJson = JsonDefaults.Serialize(new[] { image }), PriceUnit = kind == ResourceKind.Room ? "night" : kind == ResourceKind.Activity ? "person" : "hour", FeaturesJson = JsonDefaults.Serialize(features) };
    private static void AddRoom(HotelDbContext db, string name, string tagline, string description, long price, int guests, int units, string image, string[] gallery, string[] features)
    {
        var resource = Resource(name, ResourceKind.Room, tagline, description, price, guests, image, features);
        resource.GalleryImagesJson = JsonDefaults.Serialize(gallery);
        for (var i = 1; i <= units; i++) resource.Units.Add(new ResourceUnit { ResourceId = resource.Id, Name = name + " " + (100 + i) });
        db.Resources.Add(resource);
    }
    private static async Task AddDemoRoomGalleriesAsync(HotelDbContext db)
    {
        var missingGalleries = await db.Resources.Where(x => x.GalleryImagesJson == null || x.GalleryImagesJson == "").ToListAsync();
        foreach (var resource in missingGalleries) resource.GalleryImagesJson = "[]";
        var galleries = new Dictionary<string, (string Cover, string[] Photos)>
        {
            ["the-vesper-suite"] = ("/images/suite.jpg", new[] { "/images/suite.jpg", "/images/rooms/vesper-lounge.jpg", "/images/rooms/vesper-bathroom.jpg" }),
            ["panorama-residence"] = ("/images/rooms/panorama-living.jpg", new[] { "/images/rooms/panorama-living.jpg", "/images/rooms/panorama-bedroom.jpg", "/images/rooms/panorama-bathroom.jpg" }),
            ["the-penthouse"] = ("/images/rooms/penthouse-living.jpg", new[] { "/images/rooms/penthouse-living.jpg", "/images/rooms/penthouse-terrace.jpg", "/images/rooms/penthouse-bedroom.jpg" })
        };
        var rooms = await db.Resources.Where(x => x.Kind == ResourceKind.Room && galleries.Keys.Contains(x.Slug)).ToListAsync();
        foreach (var room in rooms)
        {
            if (room.GalleryImagesJson != "[]" && !string.IsNullOrWhiteSpace(room.GalleryImagesJson)) continue;
            var gallery = galleries[room.Slug];
            room.ImageUrl = gallery.Cover;
            room.GalleryImagesJson = JsonDefaults.Serialize(gallery.Photos);
        }
        await db.SaveChangesAsync();
    }
    private static void AddActivity(HotelDbContext db, string name, string tagline, string description, long price, int guests, int seats, string image, int hour, string[] features)
    {
        var resource = Resource(name, ResourceKind.Activity, tagline, description, price, guests, image, features);
        for (var day = 1; day <= 45; day++) resource.Sessions.Add(new ActivitySession { ResourceId = resource.Id, StartUtc = DateTime.UtcNow.Date.AddDays(day).AddHours(hour), EndUtc = DateTime.UtcNow.Date.AddDays(day).AddHours(hour + 2), Capacity = seats, PriceCents = price });
        db.Resources.Add(resource);
    }
    private static void AddTransport(HotelDbContext db, string name, string tagline, string description, long price, int guests, int units)
    {
        var resource = Resource(name, ResourceKind.Transportation, tagline, description, price, guests, "/images/hero.jpg", new[] { "Private chauffeur", "Meet and greet", "Bottled water", "Hourly service" });
        for (var i = 1; i <= units; i++) resource.Units.Add(new ResourceUnit { ResourceId = resource.Id, Name = name + " " + i });
        db.Resources.Add(resource);
    }
}
