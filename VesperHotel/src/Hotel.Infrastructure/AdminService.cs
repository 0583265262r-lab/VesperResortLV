using System.Text.RegularExpressions;
using Hotel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Infrastructure;

public class AdminService
{
    private readonly HotelDbContext db;
    private readonly CatalogService catalog;
    private readonly BookingService bookings;
    public AdminService(HotelDbContext db, CatalogService catalog, BookingService bookings) { this.db = db; this.catalog = catalog; this.bookings = bookings; }
    public async Task<CatalogItem> SaveResourceAsync(string? id, ResourceRequest request, string email, string correlationId)
    {
        if (!Enum.IsDefined(request.Kind)) throw new HotelException(400, "invalid_kind", "Choose a valid resource kind.");
        if (request.Features.Count > 30 || request.Features.Any(x => x.Length > 120)) throw new HotelException(400, "invalid_features", "Use at most 30 short features.");
        if (!(request.ImageUrl.StartsWith("/images/") || (Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out var imageUri) && imageUri.Scheme == "https")))
            throw new HotelException(400, "invalid_image", "Image must be a local /images/ path or an HTTPS URL.");
        if (request.GalleryImages.Count > 12 || request.GalleryImages.Any(x => x.Length > 500 || !(x.StartsWith("/images/") || (Uri.TryCreate(x, UriKind.Absolute, out var galleryUri) && galleryUri.Scheme == "https"))))
            throw new HotelException(400, "invalid_gallery", "Use at most 12 local /images/ paths or HTTPS image URLs.");
        await using var write = await DatabaseWrite.BeginAsync(db, id);
        var resource = id == null ? new CatalogResource() : await db.Resources.SingleOrDefaultAsync(x => x.Id == id && x.Id != "system")
            ?? throw new HotelException(404, "resource_not_found", "Resource not found.");
        var old = id == null ? null : JsonDefaults.Serialize(resource);
        if (id != null)
        {
            if (request.ExpectedVersion != resource.OfferVersion) throw new HotelException(409, "version_conflict", "This resource was edited elsewhere. Reload it.");
            if (request.Kind != resource.Kind) throw new HotelException(400, "immutable_kind", "A resource kind cannot be changed.");
            resource.OfferVersion++;
        }
        resource.Kind = request.Kind; resource.Name = request.Name.Trim(); resource.Description = request.Description.Trim();
        resource.Tagline = request.Tagline.Trim(); resource.BasePriceCents = BookingRules.ToCents(request.BasePrice);
        resource.PriceUnit = request.PriceUnit; resource.MaxGuests = request.MaxGuests; resource.ImageUrl = request.ImageUrl;
        resource.IsActive = request.IsActive; resource.FeaturesJson = JsonDefaults.Serialize(request.Features);
        resource.GalleryImagesJson = JsonDefaults.Serialize(request.GalleryImages.Distinct().ToList());
        if (id == null)
        {
            resource.Slug = Regex.Replace(resource.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') + "-" + resource.Id[..8];
            db.Resources.Add(resource);
        }
        Audit(id == null ? "Create" : "Update", "Resource", resource.Id, old, JsonDefaults.Serialize(resource), email, correlationId);
        await db.SaveChangesAsync(); await write.CommitAsync();
        return await catalog.GetAsync(resource.Id, true);
    }
    public async Task<ResourceUnit> SaveUnitAsync(string? id, string? resourceId, UnitRequest request, string email, string correlationId)
    {
        if (id != null) resourceId = await db.Units.Where(x => x.Id == id).Select(x => x.ResourceId).SingleOrDefaultAsync();
        if (resourceId == null) throw new HotelException(404, "unit_not_found", "Unit not found.");
        await using var write = await DatabaseWrite.BeginAsync(db, resourceId);
        var resource = await db.Resources.SingleOrDefaultAsync(x => x.Id == resourceId && x.Id != "system") ?? throw new HotelException(404, "resource_not_found", "Resource not found.");
        if (resource.Kind == ResourceKind.Activity) throw new HotelException(400, "sessions_required", "Activities use sessions instead of units.");
        var unit = id == null ? new ResourceUnit { ResourceId = resourceId } : await db.Units.SingleAsync(x => x.Id == id);
        var old = id == null ? null : JsonDefaults.Serialize(unit);
        if (!request.IsActive && await bookings.ActiveBookings(DateTime.UtcNow).AnyAsync(x => x.UnitId == unit.Id && x.InventoryEndUtc > DateTime.UtcNow))
            throw new HotelException(409, "unit_reserved", "This unit has active reservations. Cancel or complete them before deactivation.");
        unit.Name = request.Name.Trim(); unit.IsActive = request.IsActive;
        if (id == null) db.Units.Add(unit);
        Audit(id == null ? "Create" : "Update", "Unit", unit.Id, old, JsonDefaults.Serialize(unit), email, correlationId);
        // Adding inventory does not alter an existing price offer.
        await db.SaveChangesAsync(); await write.CommitAsync(); return unit;
    }
    public async Task<SessionResponse> SaveSessionAsync(string? id, string? resourceId, SessionRequest request, string email, string correlationId)
    {
        if (request.StartUtc <= DateTime.UtcNow || request.EndUtc <= request.StartUtc || request.EndUtc - request.StartUtc > TimeSpan.FromDays(7))
            throw new HotelException(400, "invalid_session", "A session must have a future start and an end within seven days.");
        if (id != null) resourceId = await db.Sessions.Where(x => x.Id == id).Select(x => x.ResourceId).SingleOrDefaultAsync();
        if (resourceId == null) throw new HotelException(404, "session_not_found", "Session not found.");
        await using var write = await DatabaseWrite.BeginAsync(db, resourceId);
        var resource = await db.Resources.SingleOrDefaultAsync(x => x.Id == resourceId) ?? throw new HotelException(404, "resource_not_found", "Resource not found.");
        if (resource.Kind != ResourceKind.Activity) throw new HotelException(400, "wrong_kind", "Only activities have sessions.");
        var session = id == null ? new ActivitySession { ResourceId = resourceId } : await db.Sessions.SingleAsync(x => x.Id == id);
        var old = id == null ? null : JsonDefaults.Serialize(session);
        var reserved = await bookings.ActiveBookings(DateTime.UtcNow).Where(x => x.SessionId == session.Id).SumAsync(x => (int?)x.Guests) ?? 0;
        if (id != null)
        {
            if (request.ExpectedVersion != session.Version) throw new HotelException(409, "version_conflict", "This session changed. Reload it.");
            if (reserved > request.Capacity || (reserved > 0 && (request.StartUtc != session.StartUtc || request.EndUtc != session.EndUtc)))
                throw new HotelException(409, "session_reserved", "Reserved sessions cannot be moved or reduced below their reserved seats.");
            session.Version++;
        }
        session.StartUtc = request.StartUtc.ToUniversalTime(); session.EndUtc = request.EndUtc.ToUniversalTime();
        session.Capacity = request.Capacity; session.PriceCents = BookingRules.ToCents(request.Price);
        resource.OfferVersion++;
        if (id == null) db.Sessions.Add(session);
        Audit(id == null ? "Create" : "Update", "Session", session.Id, old, JsonDefaults.Serialize(session), email, correlationId);
        await db.SaveChangesAsync(); await write.CommitAsync(); return CatalogService.Session(session, reserved);
    }

    public async Task<object> DashboardAsync(DateTime? from, DateTime? to)
    {
        var start = from?.ToUniversalTime() ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = to?.ToUniversalTime() ?? DateTime.UtcNow.Date.AddDays(31);
        if (end <= start || end - start > TimeSpan.FromDays(366)) throw new HotelException(400, "invalid_range", "Choose a range of up to 366 days.");
        var query = db.Bookings.AsNoTracking().Where(x => x.StartUtc >= start && x.StartUtc < end);
        var counts = await query.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var value = await query.Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed).SumAsync(x => (long?)x.TotalCents) ?? 0;
        var daily = await query.GroupBy(x => x.StartUtc.Date).Select(g => new { Date = g.Key, Bookings = g.Count(), Value = g.Sum(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed ? x.TotalCents : 0) }).OrderBy(x => x.Date).ToListAsync();
        var byKind = await query.GroupBy(x => x.Kind).Select(g => new { Kind = g.Key, Count = g.Count(), Value = g.Sum(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed ? x.TotalCents : 0) }).ToListAsync();
        var now = DateTime.UtcNow;
        // Current utilization is an instantaneous SQL count, separate from date-range financial totals.
        var active = bookings.ActiveBookings(now);
        var roomTotal = await db.Units.CountAsync(u => u.IsActive && db.Resources.Any(r => r.Id == u.ResourceId && r.IsActive && r.Kind == ResourceKind.Room));
        var carTotal = await db.Units.CountAsync(u => u.IsActive && db.Resources.Any(r => r.Id == u.ResourceId && r.IsActive && r.Kind == ResourceKind.Transportation));
        var roomUsed = await active.CountAsync(x => x.Kind == ResourceKind.Room && x.StartUtc <= now && x.InventoryEndUtc > now);
        var carUsed = await active.CountAsync(x => x.Kind == ResourceKind.Transportation && x.StartUtc <= now && x.InventoryEndUtc > now);
        var capacity = await db.Sessions.Where(x => x.StartUtc >= start && x.StartUtc < end).SumAsync(x => (long?)x.Capacity) ?? 0;
        var seats = await active.Where(x => x.Kind == ResourceKind.Activity && x.StartUtc >= start && x.StartUtc < end).SumAsync(x => (long?)x.Guests) ?? 0;
        var upcoming = await active.Where(x => x.StartUtc > now).OrderBy(x => x.StartUtc).Take(8).ToListAsync();
        int Count(BookingStatus status) => counts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
        return new { TotalBookings = counts.Sum(x => x.Count), ConfirmedBookings = Count(BookingStatus.Confirmed), CancelledBookings = Count(BookingStatus.Cancelled), CompletedBookings = Count(BookingStatus.Completed), PendingBookings = Count(BookingStatus.Pending), BookedValue = value / 100m,
            RoomOccupancy = roomTotal == 0 ? 0 : 100m * roomUsed / roomTotal, ActivityUtilization = capacity == 0 ? 0 : 100m * seats / capacity,
            TransportationUtilization = carTotal == 0 ? 0 : 100m * carUsed / carTotal,
            Daily = daily.Select(x => new { Date = x.Date.ToString("yyyy-MM-dd"), x.Bookings, Value = x.Value / 100m }),
            ByKind = byKind.Select(x => new { x.Kind, x.Count, Value = x.Value / 100m }), Upcoming = upcoming.Select(BookingResponse.From) };
    }
    private void Audit(string action, string entityType, string entityId, string? oldValue, string? newValue, string email, string correlationId) =>
        db.Audit.Add(new AuditEntry { Action = action, EntityType = entityType, EntityId = entityId, OldValue = oldValue, NewValue = newValue, AdminEmail = email, CorrelationId = correlationId });
}
