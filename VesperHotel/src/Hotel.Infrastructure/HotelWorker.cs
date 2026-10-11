using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Hotel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hotel.Infrastructure;

public class HotelWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly IConfiguration config;
    private readonly ILogger<HotelWorker> logger;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
    public HotelWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<HotelWorker> logger)
    { this.scopes = scopes; this.config = config; this.logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafka = string.Equals(config["Messaging:Mode"], "Kafka", StringComparison.OrdinalIgnoreCase);
        using var producer = kafka ? new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = config["Kafka:BootstrapServers"], EnableIdempotence = true, Acks = Acks.All, MessageTimeoutMs = 10000 }).Build() : null;
        using var consumer = kafka ? new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = config["Kafka:BootstrapServers"], GroupId = "vesper-notifications-v1", EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest }).Build() : null;
        var topic = config["Kafka:Topic"] ?? "vesper.bookings";
        consumer?.Subscribe(topic);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
                await PublishOneAsync(producer, topic, stoppingToken);
                if (consumer != null)
                {
                    var received = consumer.Consume(TimeSpan.FromMilliseconds(100));
                    if (received != null)
                    {
                        try
                        {
                            var envelope = JsonSerializer.Deserialize<EventEnvelope>(received.Message.Value, JsonDefaults.Options) ?? throw new InvalidOperationException("Empty event.");
                            await ConsumeAsync(envelope, stoppingToken);
                            consumer.Commit(received);
                        }
                        catch
                        {
                            // Do not let a later offset commit past a failed message.
                            consumer.Seek(received.TopicPartitionOffset);
                            throw;
                        }
                    }
                }
                await DeliverOneAsync(stoppingToken);
                await IndexOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Background delivery will retry after {ErrorType}.", exception.GetType().Name); }
            await Task.Delay(1000, stoppingToken);
        }
        consumer?.Close();
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        var now = DateTime.UtcNow;
        var resourceIds = await db.Bookings.AsNoTracking().Where(x => (x.Status == BookingStatus.Pending && x.ExpiresAtUtc <= now) || (x.Status == BookingStatus.Confirmed && x.EndUtc <= now))
            .Select(x => x.ResourceId).Distinct().Take(50).ToListAsync(cancellationToken);
        foreach (var id in resourceIds)
        {
            using var resourceScope = scopes.CreateScope();
            await resourceScope.ServiceProvider.GetRequiredService<BookingService>().SweepResourceAsync(id);
        }
    }

    private async Task PublishOneAsync(IProducer<string, string>? producer, string topic, CancellationToken cancellationToken)
    {
        EventEnvelope? envelope;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
            await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
            var now = DateTime.UtcNow;
            var pending = await db.Outbox.Where(x => x.Status != "Published" && x.NextAttemptUtc <= now &&
                (x.BookingId == null || !db.Outbox.Any(earlier => earlier.BookingId == x.BookingId && earlier.BookingVersion < x.BookingVersion && earlier.Status != "Published")))
                .OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (pending == null) return;
            pending.Attempts++;
            pending.Status = "Processing";
            pending.NextAttemptUtc = now.AddMinutes(1); // Durable lease, recovered after a process crash.
            envelope = new EventEnvelope { Id = pending.Id, UserId = pending.UserId, BookingId = pending.BookingId, BookingVersion = pending.BookingVersion, Type = pending.Type, PayloadJson = pending.PayloadJson, CreatedAtUtc = pending.CreatedAtUtc };
            await db.SaveChangesAsync(cancellationToken); await write.CommitAsync(cancellationToken);
        }
        string? error = null;
        try
        {
            // Network I/O takes place after the claim transaction has committed.
            if (producer != null) await producer.ProduceAsync(topic, new Message<string, string> { Key = envelope.BookingId ?? envelope.Id, Value = JsonDefaults.Serialize(envelope) }, cancellationToken);
            else await ConsumeAsync(envelope, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { error = exception.GetType().Name; }
        using var completionScope = scopes.CreateScope();
        var completionDb = completionScope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var completionWrite = await DatabaseWrite.BeginAsync(completionDb, "system", cancellationToken);
        var claimed = await completionDb.Outbox.SingleAsync(x => x.Id == envelope.Id, cancellationToken);
        claimed.Status = error == null ? "Published" : claimed.Attempts >= 10 ? "Failed" : "Pending";
        claimed.LastError = error;
        claimed.NextAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(8, claimed.Attempts))));
        await completionDb.SaveChangesAsync(cancellationToken); await completionWrite.CommitAsync(cancellationToken);
    }

    private async Task ConsumeAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
        await ConsumeInTransactionAsync(db, envelope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); await write.CommitAsync(cancellationToken);
    }
    private async Task ConsumeInTransactionAsync(HotelDbContext db, EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (await db.Inbox.AnyAsync(x => x.Id == envelope.Id, cancellationToken)) return;
        var payload = JsonSerializer.Deserialize<NotificationMessage>(envelope.PayloadJson, JsonDefaults.Options) ?? throw new InvalidOperationException("Invalid notification.");
        db.Notifications.Add(new Notification { EventId = envelope.Id, UserId = envelope.UserId, Subject = payload.Subject, Message = payload.Message });
        db.Inbox.Add(new InboxEvent { Id = envelope.Id });
    }
    private async Task DeliverOneAsync(CancellationToken cancellationToken)
    {
        Notification? notification;
        string? email;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
            await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
            var now = DateTime.UtcNow;
            notification = await db.Notifications.Where(x => x.DeliveredAtUtc == null && (x.DeliveryLeaseUntilUtc == null || x.DeliveryLeaseUntilUtc <= now)).OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (notification == null) return;
            notification.DeliveryLeaseUntilUtc = now.AddMinutes(1);
            email = await db.Users.Where(x => x.Id == notification.UserId).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await write.CommitAsync(cancellationToken);
        }
        var directory = Path.GetFullPath(config["Notifications:Directory"] ?? ".local/notifications");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, notification.Id + ".json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonDefaults.Serialize(new { To = email, notification.Subject, notification.Message, notification.CreatedAtUtc }), cancellationToken);
        File.Move(temporary, path, overwrite: true);
        using var completionScope = scopes.CreateScope();
        var completionDb = completionScope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var completionWrite = await DatabaseWrite.BeginAsync(completionDb, "system", cancellationToken);
        var completed = await completionDb.Notifications.SingleAsync(x => x.Id == notification.Id, cancellationToken);
        completed.DeliveredAtUtc = DateTime.UtcNow; completed.DeliveryLeaseUntilUtc = null;
        await completionDb.SaveChangesAsync(cancellationToken); await completionWrite.CommitAsync(cancellationToken);
    }
    private async Task IndexOneAsync(CancellationToken cancellationToken)
    {
        var elastic = config["Elasticsearch:Url"];
        if (string.IsNullOrWhiteSpace(elastic)) return;
        OutboxEvent? item;
        using (var scope = scopes.CreateScope())
        {
            var claimDb = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
            await using var claimWrite = await DatabaseWrite.BeginAsync(claimDb, "system", cancellationToken);
            var now = DateTime.UtcNow;
            item = await claimDb.Outbox.Where(x => x.Status == "Published" && x.Type != "PasswordReset" && x.IndexedAtUtc == null && x.NextIndexAttemptUtc <= now).OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (item == null) return;
            item.IndexAttempts++; item.NextIndexAttemptUtc = now.AddMinutes(1);
            await claimDb.SaveChangesAsync(cancellationToken); await claimWrite.CommitAsync(cancellationToken);
        }
        var succeeded = false;
        try
        {
            // Independent logging retry: an Elasticsearch outage never blocks notifications.
            using var response = await http.PutAsJsonAsync(elastic.TrimEnd('/') + "/vesper-events/_doc/" + Uri.EscapeDataString(item.Id), new { item.Id, item.Type, item.BookingId, item.BookingVersion, item.CreatedAtUtc }, cancellationToken);
            succeeded = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        using var completionScope = scopes.CreateScope();
        var db = completionScope.ServiceProvider.GetRequiredService<HotelDbContext>();
        await using var write = await DatabaseWrite.BeginAsync(db, "system", cancellationToken);
        var completed = await db.Outbox.SingleAsync(x => x.Id == item.Id, cancellationToken);
        if (succeeded) completed.IndexedAtUtc = DateTime.UtcNow;
        else completed.NextIndexAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(8, completed.IndexAttempts))));
        await db.SaveChangesAsync(cancellationToken); await write.CommitAsync(cancellationToken);
    }
    public override void Dispose() { http.Dispose(); base.Dispose(); }
}
