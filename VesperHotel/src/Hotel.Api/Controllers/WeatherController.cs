using Hotel.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api/weather")]
public class WeatherController : ControllerBase
{
    private readonly WeatherService _weatherService;

    public WeatherController(WeatherService weatherService)
    {
        _weatherService = weatherService;
    }

    [HttpGet]
    public async Task<ActionResult<WeatherResponse>> GetWeather(
        CancellationToken cancellationToken)
    {
        try
        {
            var weather = await _weatherService.GetLasVegasAsync(
                cancellationToken
            );

            return Ok(weather);
        }
        catch (HttpRequestException)
        {
            return WeatherUnavailable();
        }
        catch (TaskCanceledException)
        {
            return WeatherUnavailable();
        }
    }

    private ObjectResult WeatherUnavailable()
    {
        return StatusCode(503, new
        {
            code = "weather_unavailable",
            message = "Las Vegas weather is temporarily unavailable."
        });
    }
}
