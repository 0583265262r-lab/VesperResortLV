namespace Hotel.Api.Models;

public enum ResourceKind { Room, Activity, Transportation }
public enum BookingStatus { Pending, Confirmed, Cancelled, Completed, Expired, Failed }

public class CatalogResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ResourceKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string GalleryImagesJson { get; set; } = "[]";
    public long BasePriceCents { get; set; }
    public string PriceUnit { get; set; } = "";
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; } = true;
    public int OfferVersion { get; set; } = 1;
    public string FeaturesJson { get; set; } = "[]";
    public List<ResourceUnit> Units { get; set; } = new();
    public List<ActivitySession> Sessions { get; set; } = new();
}

public class ResourceUnit
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResourceId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ActivitySession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResourceId { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int Capacity { get; set; }
    public long PriceCents { get; set; }
    public int Version { get; set; } = 1;
}

public abstract class Booking
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BookingNumber { get; set; } = "VS-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    public string UserId { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string? UnitId { get; set; }
    public string? SessionId { get; set; }
    public ResourceKind Kind { get; set; }
    public string ResourceName { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string? AssignedUnit { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime InventoryEndUtc { get; set; }
    public int Guests { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CancellationDeadlineUtc { get; set; }
    public int CancellationHours { get; set; }
    public string HotelTimeZone { get; set; } = "America/Los_Angeles";
    public string? PickupLocation { get; set; }
    public string? DropoffLocation { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public string PriceLinesJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
}
public class RoomBooking : Booking { }
public class ActivityBooking : Booking { }
public class TransportationBooking : Booking { }

public class IdempotencyEntry
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public class OutboxEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? BookingId { get; set; }
    public int BookingVersion { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime? IndexedAtUtc { get; set; }
    public DateTime NextIndexAttemptUtc { get; set; } = DateTime.UtcNow;
    public int IndexAttempts { get; set; }
}
public class InboxEvent
{
    public string Id { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EventId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? DeliveryLeaseUntilUtc { get; set; }
}
public class AuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string AdminEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = "";
}
public class RefreshSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
