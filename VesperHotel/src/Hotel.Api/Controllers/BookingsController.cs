using System.Security.Claims;
using Hotel.Domain;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController, Authorize, Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingService bookings;
    public BookingsController(BookingService bookings) { this.bookings = bookings; }
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpPost] public async Task<ActionResult<BookingResponse>> Hold(HoldRequest request)
    { var result = await bookings.HoldAsync(UserId, request, Request.Headers["Idempotency-Key"].ToString()); return CreatedAtAction(nameof(Get), new { id = result.Id }, result); }
    [HttpGet("mine")] public Task<PagedBookings> Mine(BookingStatus? status, int page = 1, int pageSize = 20) => bookings.ListAsync(UserId, status, null, null, null, null, page, pageSize);
    [HttpGet("{id}")] public Task<BookingResponse> Get(string id) => bookings.GetAsync(id, UserId, User.IsInRole("Admin"));
    [HttpPost("{id}/confirm")] public Task<BookingResponse> Confirm(string id) => bookings.TransitionAsync(id, UserId, User.IsInRole("Admin"), "confirm", "", Request.Headers["Idempotency-Key"].FirstOrDefault(), User.FindFirstValue(ClaimTypes.Email) ?? "", HttpContext.TraceIdentifier);
    [HttpPost("{id}/cancel")] public Task<BookingResponse> Cancel(string id, CancelRequest request) => bookings.TransitionAsync(id, UserId, User.IsInRole("Admin"), "cancel", request.Reason, Request.Headers["Idempotency-Key"].FirstOrDefault(), User.FindFirstValue(ClaimTypes.Email) ?? "", HttpContext.TraceIdentifier);
}
