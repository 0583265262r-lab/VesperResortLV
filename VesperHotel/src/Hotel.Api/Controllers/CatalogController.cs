using Hotel.Domain;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api")]
public class CatalogController : ControllerBase
{
    private readonly CatalogService catalog;
    private readonly BookingService bookings;
    public CatalogController(CatalogService catalog, BookingService bookings) { this.catalog = catalog; this.bookings = bookings; }
    [HttpGet("catalog")] public Task<List<CatalogItem>> List(ResourceKind? kind) => catalog.ListAsync(kind);
    [HttpGet("catalog/{id}")] public Task<CatalogItem> Get(string id) => catalog.GetAsync(id);
    [HttpGet("availability")] public Task<AvailabilityResponse> Availability(string resourceId, string start, string end, int guests = 1, string? sessionId = null)
        => bookings.AvailabilityAsync(resourceId, start, end, guests, sessionId);
}
