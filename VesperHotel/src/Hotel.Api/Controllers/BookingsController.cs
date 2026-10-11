using System.Security.Claims;
using Hotel.Api.Models;
using Hotel.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingService _bookingService;

    public BookingsController(BookingService bookingService)
    {
        _bookingService = bookingService;
    }

    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    public async Task<ActionResult<BookingResponse>> CreateHold(
        HoldRequest request)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        var booking = await _bookingService.HoldAsync(UserId, request, key);

        return CreatedAtAction(
            nameof(GetBooking),
            new { id = booking.Id },
            booking
        );
    }

    [HttpGet("mine")]
    public async Task<PagedBookings> GetMyBookings(
        BookingStatus? status,
        int page = 1,
        int pageSize = 20)
    {
        return await _bookingService.ListAsync(
            UserId,
            status,
            null,
            null,
            null,
            null,
            page,
            pageSize
        );
    }

    [HttpGet("{id}")]
    public async Task<BookingResponse> GetBooking(string id)
    {
        return await _bookingService.GetAsync(
            id,
            UserId,
            User.IsInRole("Admin")
        );
    }

    [HttpPost("{id}/confirm")]
    public async Task<BookingResponse> ConfirmBooking(string id)
    {
        return await ChangeStatus(id, "confirm", "");
    }

    [HttpPost("{id}/cancel")]
    public async Task<BookingResponse> CancelBooking(
        string id,
        CancelRequest request)
    {
        return await ChangeStatus(id, "cancel", request.Reason);
    }

    private async Task<BookingResponse> ChangeStatus(
        string id,
        string action,
        string reason)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";

        return await _bookingService.TransitionAsync(
            id,
            UserId,
            User.IsInRole("Admin"),
            action,
            reason,
            key,
            email,
            HttpContext.TraceIdentifier
        );
    }
}
