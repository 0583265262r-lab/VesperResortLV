using Hotel.Api.Data;
using Hotel.Api.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Hotel.Api.Services;

public class WeatherService
{
    private readonly HttpClient http;
    private readonly IMemoryCache cache;

    public WeatherService(HttpClient http, IMemoryCache cache)
    {
        this.http = http;
        this.cache = cache;
    }

    public async Task<WeatherResponse> GetLasVegasAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue("las-vegas-weather", out WeatherResponse? saved) && saved != null) return saved;

        const string url = "/v1/forecast?latitude=36.1716&longitude=-115.1391&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=America%2FLos_Angeles&forecast_days=4&temperature_unit=fahrenheit&wind_speed_unit=mph";
        var data = await http.GetFromJsonAsync<OpenMeteoResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException("The weather provider returned an empty response.");

        var result = new WeatherResponse
        {
            Location = "Las Vegas",
            Temperature = Math.Round(data.Current.Temperature),
            ApparentTemperature = Math.Round(data.Current.ApparentTemperature),
            WindSpeed = Math.Round(data.Current.WindSpeed),
            WeatherCode = data.Current.WeatherCode,
            IsDay = data.Current.IsDay == 1,
            Condition = WeatherCondition(data.Current.WeatherCode),
            UpdatedAt = data.Current.Time,
            Daily = data.Daily.Time.Select((date, index) => new DailyWeather
            {
                Date = date,
                Minimum = Math.Round(data.Daily.Minimum.ElementAtOrDefault(index)),
                Maximum = Math.Round(data.Daily.Maximum.ElementAtOrDefault(index)),
                WeatherCode = data.Daily.WeatherCode.ElementAtOrDefault(index),
                Condition = WeatherCondition(data.Daily.WeatherCode.ElementAtOrDefault(index))
            }).ToList()
        };
        cache.Set("las-vegas-weather", result, TimeSpan.FromMinutes(15));
        return result;
    }

    private static string WeatherCondition(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Mostly clear",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Rain",
        71 or 73 or 75 or 77 or 85 or 86 => "Snow",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Variable conditions"
    };
}

public class WeatherResponse
{
    public string Location { get; set; } = "";
    public double Temperature { get; set; }
    public double ApparentTemperature { get; set; }
    public double WindSpeed { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }
    public string Condition { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public List<DailyWeather> Daily { get; set; } = new();
}

public class DailyWeather
{
    public string Date { get; set; } = "";
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public int WeatherCode { get; set; }
    public string Condition { get; set; } = "";
}

public class OpenMeteoResponse
{
    [JsonPropertyName("current")] public OpenMeteoCurrent Current { get; set; } = new();
    [JsonPropertyName("daily")] public OpenMeteoDaily Daily { get; set; } = new();
}

public class OpenMeteoCurrent
{
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("temperature_2m")] public double Temperature { get; set; }
    [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
    [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
    [JsonPropertyName("wind_speed_10m")] public double WindSpeed { get; set; }
    [JsonPropertyName("is_day")] public int IsDay { get; set; }
}

public class OpenMeteoDaily
{
    [JsonPropertyName("time")] public List<string> Time { get; set; } = new();
    [JsonPropertyName("weather_code")] public List<int> WeatherCode { get; set; } = new();
    [JsonPropertyName("temperature_2m_max")] public List<double> Maximum { get; set; } = new();
    [JsonPropertyName("temperature_2m_min")] public List<double> Minimum { get; set; } = new();
}
