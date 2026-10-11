using Hotel.Api.Models;
using Hotel.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api")]
public class CatalogController : ControllerBase
{
    private readonly CatalogService _catalogService;
    private readonly BookingService _bookingService;

    public CatalogController(
        CatalogService catalogService,
        BookingService bookingService)
    {
        _catalogService = catalogService;
        _bookingService = bookingService;
    }

    [HttpGet("catalog")]
    public async Task<List<CatalogItem>> GetCatalog(ResourceKind? kind)
    {
        return await _catalogService.ListAsync(kind);
    }

    [HttpGet("catalog/{id}")]
    public async Task<CatalogItem> GetResource(string id)
    {
        return await _catalogService.GetAsync(id);
    }

    [HttpGet("availability")]
    public async Task<AvailabilityResponse> CheckAvailability(
        string resourceId,
        string start,
        string end,
        int guests = 1,
        string? sessionId = null)
    {
        return await _bookingService.AvailabilityAsync(
            resourceId,
            start,
            end,
            guests,
            sessionId
        );
    }
}
