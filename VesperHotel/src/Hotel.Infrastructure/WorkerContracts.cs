namespace Hotel.Infrastructure;

public class NotificationMessage
{
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
}

public class EventEnvelope
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? BookingId { get; set; }
    public int BookingVersion { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
