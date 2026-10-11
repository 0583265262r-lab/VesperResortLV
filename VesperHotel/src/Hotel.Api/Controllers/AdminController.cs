using System.Security.Claims;
using Hotel.Domain;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminService admin;
    private readonly CatalogService catalog;
    private readonly BookingService bookings;
    private readonly AuthService auth;
    private readonly HotelDbContext db;
    public AdminController(AdminService admin, CatalogService catalog, BookingService bookings, AuthService auth, HotelDbContext db)
    { this.admin = admin; this.catalog = catalog; this.bookings = bookings; this.auth = auth; this.db = db; }
    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? "";
    [HttpGet("dashboard")] public Task<object> Dashboard(DateTime? from, DateTime? to) => admin.DashboardAsync(from, to);
    [HttpGet("bookings")] public Task<PagedBookings> Bookings(BookingStatus? status, ResourceKind? kind, string? search, DateTime? from, DateTime? to, int page = 1, int pageSize = 20)
        => bookings.ListAsync(null, status, kind, search, from, to, page, pageSize);
    [HttpGet("resources")] public Task<List<CatalogItem>> Resources() => catalog.ListAsync(null, true);
    [HttpPost("resources")] public async Task<ActionResult<CatalogItem>> CreateResource(ResourceRequest request) => StatusCode(201, await admin.SaveResourceAsync(null, request, Email, HttpContext.TraceIdentifier));
    [HttpPut("resources/{id}")] public Task<CatalogItem> UpdateResource(string id, ResourceRequest request) => admin.SaveResourceAsync(id, request, Email, HttpContext.TraceIdentifier);
    [HttpGet("resources/{id}/units")] public async Task<object> Units(string id) => await db.Units.AsNoTracking().Where(x => x.ResourceId == id).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync();
    [HttpPost("resources/{id}/units")] public async Task<ActionResult<object>> CreateUnit(string id, UnitRequest request) { var unit = await admin.SaveUnitAsync(null, id, request, Email, HttpContext.TraceIdentifier); return StatusCode(201, new { unit.Id, unit.Name, unit.IsActive }); }
    [HttpPut("units/{id}")] public async Task<object> UpdateUnit(string id, UnitRequest request) { var unit = await admin.SaveUnitAsync(id, null, request, Email, HttpContext.TraceIdentifier); return new { unit.Id, unit.Name, unit.IsActive }; }
    [HttpPost("resources/{id}/sessions")] public async Task<ActionResult<SessionResponse>> CreateSession(string id, SessionRequest request) => StatusCode(201, await admin.SaveSessionAsync(null, id, request, Email, HttpContext.TraceIdentifier));
    [HttpPut("sessions/{id}")] public Task<SessionResponse> UpdateSession(string id, SessionRequest request) => admin.SaveSessionAsync(id, null, request, Email, HttpContext.TraceIdentifier);
    [HttpGet("users")] public Task<object> Users(int page = 1, int pageSize = 20, string? search = null) => auth.ListUsersAsync(page, pageSize, search);
    [HttpGet("audit")] public async Task<object> Audit(int page = 1, int pageSize = 20)
    { page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); return new { Items = await db.Audit.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), TotalCount = await db.Audit.CountAsync(), Page = page, PageSize = pageSize }; }
    [HttpGet("system")] public async Task<object> SystemStatus() => new { OutboxPending = await db.Outbox.CountAsync(x => x.Status == "Pending"), OutboxFailed = await db.Outbox.CountAsync(x => x.Status == "Failed"), NotificationsPending = await db.Notifications.CountAsync(x => x.DeliveredAtUtc == null),
        RecentEvents = await db.Outbox.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(30).Select(x => new { x.Id, x.Type, x.CreatedAtUtc, x.Status, x.Attempts }).ToListAsync() };
}
