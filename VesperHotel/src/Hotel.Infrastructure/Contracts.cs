using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Domain;

namespace Hotel.Infrastructure;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}
public class HoldRequest
{
    public ResourceKind Kind { get; set; }
    [Required] public string ResourceId { get; set; } = "";
    public string? SessionId { get; set; }
    [Required] public string Start { get; set; } = "";
    [Required] public string End { get; set; } = "";
    public int Guests { get; set; }
    public int ExpectedOfferVersion { get; set; }
    [MaxLength(500)] public string? PickupLocation { get; set; }
    [MaxLength(500)] public string? DropoffLocation { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}
public class PriceLine
{
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
public class BookingResponse
{
    public string Id { get; set; } = "";
    public string BookingNumber { get; set; } = "";
    public ResourceKind Kind { get; set; }
    public string ResourceId { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public BookingStatus Status { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int Guests { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime CancellationDeadlineUtc { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropoffLocation { get; set; }
    public string? Notes { get; set; }
    public string? AssignedUnit { get; set; }
    public int Version { get; set; }
    public List<PriceLine> PriceLines { get; set; } = new();
    public static BookingResponse From(Booking b) => new()
    {
        Id = b.Id, BookingNumber = b.BookingNumber, Kind = b.Kind, ResourceId = b.ResourceId,
        ResourceName = b.ResourceName, ImageUrl = b.ImageUrl, Status = b.Status, StartUtc = DateTime.SpecifyKind(b.StartUtc, DateTimeKind.Utc),
        EndUtc = DateTime.SpecifyKind(b.EndUtc, DateTimeKind.Utc), Guests = b.Guests, TotalAmount = b.TotalCents / 100m,
        Currency = b.Currency, ExpiresAtUtc = b.ExpiresAtUtc.HasValue ? DateTime.SpecifyKind(b.ExpiresAtUtc.Value, DateTimeKind.Utc) : null,
        CreatedAtUtc = DateTime.SpecifyKind(b.CreatedAtUtc, DateTimeKind.Utc), CancellationDeadlineUtc = DateTime.SpecifyKind(b.CancellationDeadlineUtc, DateTimeKind.Utc),
        PickupLocation = b.PickupLocation, DropoffLocation = b.DropoffLocation, Notes = b.Notes, AssignedUnit = b.AssignedUnit,
        Version = b.Version, PriceLines = JsonSerializer.Deserialize<List<PriceLine>>(b.PriceLinesJson, JsonDefaults.Options) ?? new()
    };
}
public class PagedBookings
{
    public List<BookingResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
public class SessionResponse
{
    public string Id { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int Capacity { get; set; }
    public int ReservedSeats { get; set; }
    public decimal Price { get; set; }
    public int Version { get; set; }
}
public class CatalogItem
{
    public string Id { get; set; } = "";
    public ResourceKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public List<string> GalleryImages { get; set; } = new();
    public decimal BasePrice { get; set; }
    public string PriceUnit { get; set; } = "";
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; }
    public int OfferVersion { get; set; }
    public List<string> Features { get; set; } = new();
    public int AvailableUnits { get; set; }
    public List<SessionResponse> Sessions { get; set; } = new();
}
public class AvailabilityResponse
{
    public bool Available { get; set; }
    public int AvailableCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public int OfferVersion { get; set; }
    public int CancellationHours { get; set; }
}
public class ResourceRequest
{
    public ResourceKind Kind { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    [Required, MaxLength(5000)] public string Description { get; set; } = "";
    [MaxLength(200)] public string Tagline { get; set; } = "";
    public decimal BasePrice { get; set; }
    [Required, MaxLength(30)] public string PriceUnit { get; set; } = "";
    [Range(1, 1000)] public int MaxGuests { get; set; }
    [Required, MaxLength(500)] public string ImageUrl { get; set; } = "";
    public List<string> GalleryImages { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public int ExpectedVersion { get; set; }
}
public class UnitRequest
{
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public class SessionRequest
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    [Range(1, 10000)] public int Capacity { get; set; }
    public decimal Price { get; set; }
    public int ExpectedVersion { get; set; }
}
public class CancelRequest { [MaxLength(500)] public string Reason { get; set; } = ""; }
