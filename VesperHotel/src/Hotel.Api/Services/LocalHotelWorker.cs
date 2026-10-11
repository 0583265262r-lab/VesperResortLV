using Hotel.Api.Data;
using System.Text.Json;
using Hotel.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hotel.Api.Services;

// Local development uses the same persistent outbox tables without requiring Kafka.
// Docker runs HotelWorker instead, where events are published through Kafka.
public class LocalHotelWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly IConfiguration config;
    private readonly ILogger<LocalHotelWorker> logger;

    public LocalHotelWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<LocalHotelWorker> logger)
    {
        this.scopes = scopes;
        this.config = config;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepBookingsAsync(stoppingToken);
                await ProcessOutboxAsync(stoppingToken);
                await DeliverNotificationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Local background processing will retry after {ErrorType}.", exception.GetType().Name);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task SweepBookingsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        var now = DateTime.UtcNow;
        var resourceIds = await db.Bookings.AsNoTracking()
            .Where(x => (x.Status == BookingStatus.Pending && x.ExpiresAtUtc <= now) ||
                        (x.Status == BookingStatus.Confirmed && x.EndUtc <= now))
            .Select(x => x.ResourceId)
            .Distinct()
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var resourceId in resourceIds)
        {
            using var bookingScope = scopes.CreateScope();
            await bookingScope.ServiceProvider.GetRequiredService<BookingService>()
                .SweepResourceAsync(resourceId);
        }
    }

    private async Task ProcessOutboxAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
        var now = DateTime.UtcNow;
        var item = await db.Outbox
            .Where(x => x.Status != "Published" && x.NextAttemptUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (item == null)
            return;

        if (!await db.Inbox.AnyAsync(x => x.Id == item.Id, cancellationToken))
        {
            var payload = JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson, JsonDefaults.Options)
                ?? throw new InvalidOperationException("Invalid outbox notification.");
            db.Notifications.Add(new Notification
            {
                EventId = item.Id,
                UserId = item.UserId,
                Subject = payload.Subject,
                Message = payload.Message
            });
            db.Inbox.Add(new InboxEvent { Id = item.Id });
        }

        item.Attempts++;
        item.Status = "Published";
        item.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);
    }

    private async Task DeliverNotificationAsync(CancellationToken cancellationToken)
    {
        Notification? notification;
        string? email;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
            await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
            var now = DateTime.UtcNow;
            notification = await db.Notifications
                .Where(x => x.DeliveredAtUtc == null &&
                            (x.DeliveryLeaseUntilUtc == null || x.DeliveryLeaseUntilUtc <= now))
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (notification == null)
                return;

            notification.DeliveryLeaseUntilUtc = now.AddMinutes(1);
            email = await db.Users.Where(x => x.Id == notification.UserId)
                .Select(x => x.Email)
                .SingleOrDefaultAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
        }

        var directory = Path.GetFullPath(config["Notifications:Directory"] ?? ".local/notifications");
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, notification.Id + ".json");
        var temporaryPath = finalPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonDefaults.Serialize(new
        {
            To = email,
            notification.Subject,
            notification.Message,
            notification.CreatedAtUtc
        }), cancellationToken);
        File.Move(temporaryPath, finalPath, true);

        using var completionScope = scopes.CreateScope();
        var completionDb = completionScope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var completionWrite = await DatabaseWrite.BeginAsync(completionDb, "system", cancellationToken);
        var completed = await completionDb.Notifications.SingleAsync(x => x.Id == notification.Id, cancellationToken);
        completed.DeliveredAtUtc = DateTime.UtcNow;
        completed.DeliveryLeaseUntilUtc = null;
        await completionDb.SaveChangesAsync(cancellationToken);
        await completionWrite.CommitAsync(cancellationToken);
    }
}
