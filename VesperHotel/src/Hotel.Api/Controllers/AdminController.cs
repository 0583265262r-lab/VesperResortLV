using System.Security.Claims;
using Hotel.Api.Data;
using Hotel.Api.Models;
using Hotel.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminService _adminService;
    private readonly CatalogService _catalogService;
    private readonly BookingService _bookingService;
    private readonly AuthService _authService;
    private readonly HotelDbContext _db;

    public AdminController(
        AdminService adminService,
        CatalogService catalogService,
        BookingService bookingService,
        AuthService authService,
        HotelDbContext db)
    {
        _adminService = adminService;
        _catalogService = catalogService;
        _bookingService = bookingService;
        _authService = authService;
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<object> GetDashboard(DateTime? from, DateTime? to)
    {
        return await _adminService.DashboardAsync(from, to);
    }

    [HttpGet("bookings")]
    public async Task<PagedBookings> GetBookings(
        BookingStatus? status,
        ResourceKind? kind,
        string? search,
        DateTime? from,
        DateTime? to,
        int page = 1,
        int pageSize = 20)
    {
        return await _bookingService.ListAsync(
            null,
            status,
            kind,
            search,
            from,
            to,
            page,
            pageSize
        );
    }

    [HttpGet("resources")]
    public async Task<List<CatalogItem>> GetResources()
    {
        return await _catalogService.ListAsync(null, true);
    }

    [HttpPost("resources")]
    public async Task<ActionResult<CatalogItem>> CreateResource(
        ResourceRequest request)
    {
        var resource = await _adminService.SaveResourceAsync(
            null,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );

        return StatusCode(201, resource);
    }

    [HttpPut("resources/{id}")]
    public async Task<CatalogItem> UpdateResource(
        string id,
        ResourceRequest request)
    {
        return await _adminService.SaveResourceAsync(
            id,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );
    }

    [HttpGet("resources/{id}/units")]
    public async Task<object> GetUnits(string id)
    {
        return await _db.Units
            .AsNoTracking()
            .Where(x => x.ResourceId == id)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.IsActive })
            .ToListAsync();
    }

    [HttpPost("resources/{id}/units")]
    public async Task<ActionResult<object>> CreateUnit(
        string id,
        UnitRequest request)
    {
        var unit = await _adminService.SaveUnitAsync(
            null,
            id,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );

        return StatusCode(201, new
        {
            unit.Id,
            unit.Name,
            unit.IsActive
        });
    }

    [HttpPut("units/{id}")]
    public async Task<object> UpdateUnit(string id, UnitRequest request)
    {
        var unit = await _adminService.SaveUnitAsync(
            id,
            null,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );

        return new { unit.Id, unit.Name, unit.IsActive };
    }

    [HttpPost("resources/{id}/sessions")]
    public async Task<ActionResult<SessionResponse>> CreateSession(
        string id,
        SessionRequest request)
    {
        var session = await _adminService.SaveSessionAsync(
            null,
            id,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );

        return StatusCode(201, session);
    }

    [HttpPut("sessions/{id}")]
    public async Task<SessionResponse> UpdateSession(
        string id,
        SessionRequest request)
    {
        return await _adminService.SaveSessionAsync(
            id,
            null,
            request,
            GetAdminEmail(),
            HttpContext.TraceIdentifier
        );
    }

    [HttpGet("users")]
    public async Task<object> GetUsers(
        int page = 1,
        int pageSize = 20,
        string? search = null)
    {
        return await _authService.ListUsersAsync(page, pageSize, search);
    }

    [HttpGet("audit")]
    public async Task<object> GetAudit(int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var items = await _db.Audit
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new
        {
            Items = items,
            TotalCount = await _db.Audit.CountAsync(),
            Page = page,
            PageSize = pageSize
        };
    }

    [HttpGet("system")]
    public async Task<object> GetSystemStatus()
    {
        var events = await _db.Outbox
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(30)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.CreatedAtUtc,
                x.Status,
                x.Attempts
            })
            .ToListAsync();

        return new
        {
            OutboxPending = await _db.Outbox.CountAsync(
                x => x.Status == "Pending"
            ),
            OutboxFailed = await _db.Outbox.CountAsync(
                x => x.Status == "Failed"
            ),
            NotificationsPending = await _db.Notifications.CountAsync(
                x => x.DeliveredAtUtc == null
            ),
            RecentEvents = events
        };
    }

    private string GetAdminEmail()
    {
        return User.FindFirstValue(ClaimTypes.Email) ?? "";
    }
}
