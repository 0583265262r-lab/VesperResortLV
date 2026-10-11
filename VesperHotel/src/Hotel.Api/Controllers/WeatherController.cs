using Hotel.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Hotel.Api.Controllers;

[ApiController]
[Route("api/weather")]
public class WeatherController : ControllerBase
{
    private readonly WeatherService weather;
    public WeatherController(WeatherService weather) { this.weather = weather; }

    [HttpGet]
    public async Task<ActionResult<WeatherResponse>> Get(CancellationToken cancellationToken)
    {
        try { return Ok(await weather.GetLasVegasAsync(cancellationToken)); }
        catch (HttpRequestException) { return StatusCode(503, new { code = "weather_unavailable", message = "Las Vegas weather is temporarily unavailable." }); }
        catch (TaskCanceledException) { return StatusCode(503, new { code = "weather_unavailable", message = "Las Vegas weather is temporarily unavailable." }); }
    }
}
