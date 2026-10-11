using Hotel.Api.Data;
using System.Text.Json;
using Hotel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Services;

public class CatalogService
{
    private readonly HotelDbContext db;
    private readonly BookingService bookings;
    public CatalogService(HotelDbContext db, BookingService bookings) { this.db = db; this.bookings = bookings; }
    public async Task<List<CatalogItem>> ListAsync(ResourceKind? kind, bool includeInactive = false)
    {
        var query = db.Resources.AsNoTracking().Include(x => x.Units).Include(x => x.Sessions).Where(x => x.Id != "system");
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (kind != null) query = query.Where(x => x.Kind == kind);
        var resources = await query.OrderBy(x => x.Kind).ThenBy(x => x.BasePriceCents).ToListAsync();
        var counts = await bookings.ActiveBookings(DateTime.UtcNow).Where(x => x.SessionId != null)
            .GroupBy(x => x.SessionId).Select(g => new { Id = g.Key!, Seats = g.Sum(x => x.Guests) }).ToDictionaryAsync(x => x.Id, x => x.Seats);
        return resources.Select(x => Map(x, counts)).ToList();
    }
    public async Task<CatalogItem> GetAsync(string id, bool admin = false)
    {
        var resource = await db.Resources.AsNoTracking().Include(x => x.Units).Include(x => x.Sessions)
            .SingleOrDefaultAsync(x => x.Id == id && x.Id != "system" && (admin || x.IsActive))
            ?? throw new HotelException(404, "resource_not_found", "Resource not found.");
        var counts = await bookings.ActiveBookings(DateTime.UtcNow).Where(x => x.ResourceId == id && x.SessionId != null)
            .GroupBy(x => x.SessionId).Select(g => new { Id = g.Key!, Seats = g.Sum(x => x.Guests) }).ToDictionaryAsync(x => x.Id, x => x.Seats);
        return Map(resource, counts);
    }
    private static CatalogItem Map(CatalogResource r, Dictionary<string, int> counts) => new()
    {
        Id = r.Id, Kind = r.Kind, Name = r.Name, Slug = r.Slug, Description = r.Description, Tagline = r.Tagline,
        ImageUrl = r.ImageUrl, GalleryImages = ReadStringList(r.GalleryImagesJson),
        BasePrice = r.BasePriceCents / 100m, PriceUnit = r.PriceUnit, MaxGuests = r.MaxGuests,
        IsActive = r.IsActive, OfferVersion = r.OfferVersion, Features = ReadStringList(r.FeaturesJson),
        AvailableUnits = r.Units.Count(x => x.IsActive), Sessions = r.Sessions.Where(x => x.EndUtc > DateTime.UtcNow).OrderBy(x => x.StartUtc).Select(s => Session(s, counts.GetValueOrDefault(s.Id))).ToList()
    };
    private static List<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }
    public static SessionResponse Session(ActivitySession s, int reserved = 0) => new() { Id = s.Id,
        StartUtc = DateTime.SpecifyKind(s.StartUtc, DateTimeKind.Utc), EndUtc = DateTime.SpecifyKind(s.EndUtc, DateTimeKind.Utc),
        Capacity = s.Capacity, ReservedSeats = reserved, Price = s.PriceCents / 100m, Version = s.Version };
}
