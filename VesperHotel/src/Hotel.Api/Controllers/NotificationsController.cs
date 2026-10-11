using System.Security.Claims;
using Hotel.Domain;
using Hotel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Controllers;

[ApiController, Authorize, Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly HotelDbContext db;
    public NotificationsController(HotelDbContext db) { this.db = db; }
    [HttpGet] public async Task<object> List()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await db.Notifications.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new { x.Id, x.Subject, x.Message, x.CreatedAtUtc, x.IsRead }).ToListAsync();
    }
    [HttpPost("{id}/read")] public async Task<IActionResult> Read(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await using var write = await DatabaseWrite.BeginAsync(db, "system");
        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId) ?? throw new HotelException(404, "notification_not_found", "Notification not found.");
        notification.IsRead = true; await db.SaveChangesAsync(); await write.CommitAsync(); return NoContent();
    }
}
