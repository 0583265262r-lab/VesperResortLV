using System.Security.Claims;
using Hotel.Api.Data;
using Hotel.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly HotelDbContext _db;

    public NotificationsController(HotelDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<object> GetNotifications()
    {
        var userId = GetUserId();

        return await _db.Notifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                x.Subject,
                x.Message,
                x.CreatedAtUtc,
                x.IsRead
            })
            .ToListAsync();
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id)
    {
        var userId = GetUserId();

        var notification = await _db.Notifications
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);

        if (notification == null)
        {
            throw new HotelException(
                404,
                "notification_not_found",
                "Notification not found."
            );
        }

        notification.IsRead = true;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private string GetUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    }
}
