using System.Globalization;

namespace Hotel.Api.Models;

public class HotelException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public HotelException(int status, string code, string message) : base(message)
    { Status = status; Code = code; }
}

public class BookingPeriod
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime InventoryEndUtc { get; set; }
    public int Quantity { get; set; }
}

public static class BookingRules
{
    public static long ToCents(decimal price)
    {
        if (price < 0 || price > 1000000 || decimal.Round(price, 2) != price)
            throw new HotelException(400, "invalid_price", "Price must be between 0 and 1,000,000 with at most two decimals.");
        return checked((long)(price * 100));
    }

    public static BookingPeriod Period(ResourceKind kind, string start, string end, DateTime now)
    {
        var period = new BookingPeriod();
        if (kind == ResourceKind.Room)
        {
            if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ||
                !DateOnly.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last))
                throw new HotelException(400, "invalid_dates", "Room dates must use YYYY-MM-DD.");
            period.Quantity = last.DayNumber - first.DayNumber;
            if (period.Quantity < 1 || period.Quantity > 30)
                throw new HotelException(400, "invalid_stay", "Room stays must be between 1 and 30 nights.");
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            period.StartUtc = TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(new TimeOnly(15, 0)), zone);
            period.EndUtc = TimeZoneInfo.ConvertTimeToUtc(last.ToDateTime(new TimeOnly(11, 0)), zone);
        }
        else
        {
            if (!DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var first) ||
                !DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var last))
                throw new HotelException(400, "invalid_dates", "Use valid ISO timestamps.");
            period.StartUtc = first.UtcDateTime;
            period.EndUtc = last.UtcDateTime;
            if (period.EndUtc <= period.StartUtc || period.EndUtc - period.StartUtc > TimeSpan.FromDays(7))
                throw new HotelException(400, "invalid_period", "The end must follow the start, within seven days.");
            period.Quantity = (int)Math.Ceiling((period.EndUtc - period.StartUtc).TotalHours);
        }
        if (period.StartUtc <= now)
            throw new HotelException(400, "past_booking", "The booking must begin in the future.");
        period.InventoryEndUtc = kind == ResourceKind.Transportation ? period.EndUtc.AddMinutes(30) : period.EndUtc;
        return period;
    }

    public static void ValidateGuests(int guests, int maxGuests)
    {
        if (guests < 1 || guests > maxGuests)
            throw new HotelException(400, "invalid_guests", $"Guests must be between 1 and {maxGuests}.");
    }
    public static bool Overlaps(DateTime firstStart, DateTime firstEnd, DateTime secondStart, DateTime secondEnd)
        => firstStart < secondEnd && secondStart < firstEnd;
}
