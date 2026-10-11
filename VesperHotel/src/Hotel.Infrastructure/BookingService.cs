using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hotel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Infrastructure;

public class BookingService
{
    private readonly HotelDbContext db;
    public BookingService(HotelDbContext db) { this.db = db; }

    public IQueryable<Booking> ActiveBookings(DateTime now) => db.Bookings.Where(b =>
        b.Status == BookingStatus.Confirmed || (b.Status == BookingStatus.Pending && b.ExpiresAtUtc > now));

    public async Task<AvailabilityResponse> AvailabilityAsync(string resourceId, string start, string end, int guests, string? sessionId)
    {
        var resource = await db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == resourceId && x.IsActive)
            ?? throw new HotelException(404, "resource_not_found", "This resource is unavailable.");
        var period = BookingRules.Period(resource.Kind, start, end, DateTime.UtcNow);
        BookingRules.ValidateGuests(guests, resource.MaxGuests);
        var allocation = await AllocateAsync(resource, period, guests, sessionId);
        return new AvailabilityResponse { Available = allocation.Count >= (resource.Kind == ResourceKind.Activity ? guests : 1),
            AvailableCount = allocation.Count, TotalAmount = allocation.PriceCents * allocation.Quantity / 100m,
            OfferVersion = resource.OfferVersion, CancellationHours = resource.Kind == ResourceKind.Room ? 48 : 24 };
    }

    public async Task<BookingResponse> HoldAsync(string userId, HoldRequest request, string key)
    {
        ValidateKey(key);
        var hash = Hash(JsonDefaults.Serialize(request));
        await using var write = await DatabaseWrite.BeginAsync(db, request.ResourceId, userId: userId);
        var replay = await ReplayAsync(userId, "hold", key, hash);
        if (replay != null) return replay;
        var now = DateTime.UtcNow;
        var resource = await db.Resources.SingleOrDefaultAsync(x => x.Id == request.ResourceId && x.IsActive)
            ?? throw new HotelException(404, "resource_not_found", "This resource is unavailable.");
        if (resource.Kind != request.Kind) throw new HotelException(400, "wrong_kind", "The resource kind does not match.");
        if (request.ExpectedOfferVersion != resource.OfferVersion)
            throw new HotelException(409, "offer_changed", "This offer changed. Refresh availability and try again.");
        BookingRules.ValidateGuests(request.Guests, resource.MaxGuests);
        var period = BookingRules.Period(resource.Kind, request.Start, request.End, now);
        if (resource.Kind == ResourceKind.Transportation && (string.IsNullOrWhiteSpace(request.PickupLocation) || string.IsNullOrWhiteSpace(request.DropoffLocation)))
            throw new HotelException(400, "route_required", "Pickup and dropoff locations are required.");
        var allocation = await AllocateAsync(resource, period, request.Guests, request.SessionId);
        if (allocation.Count < (resource.Kind == ResourceKind.Activity ? request.Guests : 1))
            throw new HotelException(409, "unavailable", "The requested inventory is no longer available.");
        Booking booking = resource.Kind switch { ResourceKind.Room => new RoomBooking(), ResourceKind.Activity => new ActivityBooking(), _ => new TransportationBooking() };
        booking.UserId = userId; booking.ResourceId = resource.Id; booking.Kind = resource.Kind;
        booking.ResourceName = resource.Name; booking.ImageUrl = resource.ImageUrl;
        booking.UnitId = allocation.Unit?.Id; booking.AssignedUnit = allocation.Unit?.Name;
        booking.SessionId = allocation.Session?.Id; booking.StartUtc = period.StartUtc; booking.EndUtc = period.EndUtc;
        booking.InventoryEndUtc = period.InventoryEndUtc; booking.Guests = request.Guests;
        booking.TotalCents = checked(allocation.PriceCents * allocation.Quantity);
        booking.ExpiresAtUtc = now.AddMinutes(10) < period.StartUtc ? now.AddMinutes(10) : period.StartUtc; booking.CreatedAtUtc = now;
        booking.CancellationHours = resource.Kind == ResourceKind.Room ? 48 : 24;
        booking.CancellationDeadlineUtc = period.StartUtc.AddHours(-booking.CancellationHours);
        booking.PickupLocation = request.PickupLocation; booking.DropoffLocation = request.DropoffLocation; booking.Notes = request.Notes;
        booking.PriceLinesJson = JsonDefaults.Serialize(new[] { new PriceLine { Description = resource.Kind == ResourceKind.Room ? "Nightly accommodation" : resource.Kind == ResourceKind.Activity ? "Activity admission" : "Private vehicle hours", Quantity = allocation.Quantity, UnitPrice = allocation.PriceCents / 100m, Total = booking.TotalCents / 100m } });
        db.Bookings.Add(booking);
        AddEvent(booking, "BookingHeld");
        var response = BookingResponse.From(booking);
        SaveReplay(userId, "hold", key, hash, response);
        await db.SaveChangesAsync();
        await write.CommitAsync();
        return response;
    }

    public async Task<BookingResponse> TransitionAsync(string id, string userId, bool admin, string action, string reason, string? key, string adminEmail, string correlationId)
    {
        var resourceId = await db.Bookings.AsNoTracking().Where(x => x.Id == id && (x.UserId == userId || admin)).Select(x => x.ResourceId).SingleOrDefaultAsync()
            ?? throw new HotelException(404, "booking_not_found", "Booking not found.");
        await using var write = await DatabaseWrite.BeginAsync(db, resourceId, userId: userId);
        var hash = Hash(JsonDefaults.Serialize(new { id, action, reason }));
        if (!string.IsNullOrEmpty(key))
        {
            ValidateKey(key);
            var replay = await ReplayAsync(userId, action, key, hash);
            if (replay != null) return replay;
        }
        var booking = await db.Bookings.SingleAsync(x => x.Id == id);
        var now = DateTime.UtcNow;
        if (booking.Status == BookingStatus.Pending && booking.ExpiresAtUtc <= now)
        {
            booking.Status = BookingStatus.Expired; booking.Version++; AddEvent(booking, "BookingExpired");
            await db.SaveChangesAsync(); await write.CommitAsync();
            throw new HotelException(409, "hold_expired", "The hold expired. Please make a new reservation.");
        }
        var previous = JsonDefaults.Serialize(BookingResponse.From(booking));
        if (action == "confirm")
        {
            if (booking.Status == BookingStatus.Pending && booking.StartUtc <= now)
                throw new HotelException(409, "booking_started", "The service has started. Please create a new reservation.");
            if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
                throw new HotelException(409, "invalid_transition", "Only an active pending hold can be confirmed.");
            if (booking.Status == BookingStatus.Pending)
            {
                booking.Status = BookingStatus.Confirmed; booking.ExpiresAtUtc = null; booking.Version++;
                AddEvent(booking, "BookingConfirmed");
            }
        }
        else
        {
            if (booking.Status != BookingStatus.Cancelled)
            {
                if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
                    throw new HotelException(409, "invalid_transition", "This booking can no longer be cancelled.");
                if (booking.EndUtc <= now) throw new HotelException(409, "booking_finished", "A finished booking cannot be cancelled.");
                if (!admin && booking.Status == BookingStatus.Confirmed && now > booking.CancellationDeadlineUtc)
                    throw new HotelException(409, "cancellation_deadline", "The cancellation deadline has passed. Contact the hotel.");
                if (admin && string.IsNullOrWhiteSpace(reason)) throw new HotelException(400, "reason_required", "An administrative cancellation needs a reason.");
                booking.Status = BookingStatus.Cancelled; booking.ExpiresAtUtc = null; booking.CancellationReason = reason; booking.Version++;
                AddEvent(booking, "BookingCancelled");
            }
        }
        var response = BookingResponse.From(booking);
        if (admin) db.Audit.Add(new AuditEntry { Action = action, EntityType = "Booking", EntityId = id, OldValue = previous, NewValue = JsonDefaults.Serialize(response), AdminEmail = adminEmail, CorrelationId = correlationId });
        if (!string.IsNullOrEmpty(key)) SaveReplay(userId, action, key, hash, response);
        await db.SaveChangesAsync(); await write.CommitAsync();
        return response;
    }

    public async Task<BookingResponse> GetAsync(string id, string userId, bool admin)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.UserId == userId))
            ?? throw new HotelException(404, "booking_not_found", "Booking not found.");
        return BookingResponse.From(booking);
    }

    public async Task<PagedBookings> ListAsync(string? userId, BookingStatus? status, ResourceKind? kind, string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Bookings.AsNoTracking();
        if (userId != null) query = query.Where(x => x.UserId == userId);
        if (status != null) query = query.Where(x => x.Status == status);
        if (kind != null) query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.BookingNumber.Contains(search) || x.ResourceName.Contains(search));
        if (from != null) query = query.Where(x => x.StartUtc >= from);
        if (to != null) query = query.Where(x => x.StartUtc < to);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedBookings { Items = items.Select(BookingResponse.From).ToList(), TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task SweepResourceAsync(string resourceId)
    {
        await using var write = await DatabaseWrite.BeginAsync(db, resourceId);
        var now = DateTime.UtcNow;
        var bookings = await db.Bookings.Where(x => x.ResourceId == resourceId &&
            ((x.Status == BookingStatus.Pending && x.ExpiresAtUtc <= now) || (x.Status == BookingStatus.Confirmed && x.EndUtc <= now))).ToListAsync();
        foreach (var booking in bookings)
        {
            booking.Status = booking.Status == BookingStatus.Pending ? BookingStatus.Expired : BookingStatus.Completed;
            booking.ExpiresAtUtc = null; booking.Version++; AddEvent(booking, "Booking" + booking.Status);
        }
        await db.SaveChangesAsync(); await write.CommitAsync();
    }

    private async Task<Allocation> AllocateAsync(CatalogResource resource, BookingPeriod period, int guests, string? sessionId)
    {
        var result = new Allocation { PriceCents = resource.BasePriceCents, Quantity = period.Quantity };
        var active = ActiveBookings(DateTime.UtcNow).Where(x => x.ResourceId == resource.Id);
        if (resource.Kind == ResourceKind.Activity)
        {
            result.Session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.ResourceId == resource.Id)
                ?? throw new HotelException(400, "session_required", "Select an available activity session.");
            if (period.StartUtc != result.Session.StartUtc || period.EndUtc != result.Session.EndUtc)
                throw new HotelException(409, "session_changed", "The selected session time changed. Refresh the activity.");
            var reserved = await active.Where(x => x.SessionId == sessionId).SumAsync(x => (int?)x.Guests) ?? 0;
            result.Count = result.Session.Capacity - reserved; result.PriceCents = result.Session.PriceCents; result.Quantity = guests;
        }
        else
        {
            var occupied = active.Where(x => x.StartUtc < period.InventoryEndUtc && period.StartUtc < x.InventoryEndUtc).Select(x => x.UnitId);
            var units = db.Units.AsNoTracking().Where(x => x.ResourceId == resource.Id && x.IsActive && !occupied.Contains(x.Id));
            result.Count = await units.CountAsync(); result.Unit = await units.OrderBy(x => x.Name).FirstOrDefaultAsync();
        }
        return result;
    }
    private async Task<BookingResponse?> ReplayAsync(string userId, string operation, string key, string hash)
    {
        var previous = await db.IdempotencyEntries.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Operation == operation && x.Key == key);
        if (previous == null) return null;
        if (previous.RequestHash != hash) throw new HotelException(409, "idempotency_conflict", "This idempotency key was already used for another request.");
        return JsonSerializer.Deserialize<BookingResponse>(previous.ResponseJson, JsonDefaults.Options)!;
    }
    private void SaveReplay(string userId, string operation, string key, string hash, BookingResponse response) =>
        db.IdempotencyEntries.Add(new IdempotencyEntry { UserId = userId, Operation = operation, Key = key, RequestHash = hash, ResponseJson = JsonDefaults.Serialize(response) });
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120) throw new HotelException(400, "idempotency_key_required", "Provide an Idempotency-Key header with 1–120 characters.");
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private void AddEvent(Booking booking, string type) => db.Outbox.Add(new OutboxEvent { Type = type, UserId = booking.UserId, BookingId = booking.Id, BookingVersion = booking.Version,
        PayloadJson = JsonDefaults.Serialize(new NotificationMessage { Subject = $"{booking.BookingNumber}: {booking.Status}", Message = $"Your {booking.ResourceName} reservation is {booking.Status.ToString().ToLowerInvariant()}. Total: {booking.TotalCents / 100m:0.00} {booking.Currency}. Payment is due at the hotel." }) });
    private class Allocation
    {
        public int Count { get; set; }
        public long PriceCents { get; set; }
        public int Quantity { get; set; }
        public ResourceUnit? Unit { get; set; }
        public ActivitySession? Session { get; set; }
    }
}
