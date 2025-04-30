using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Postgres;

namespace sample.API.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    private readonly ILogger<WeatherForecastController> _logger;
    private HybridCache _cache;

    public WeatherForecastController(ILogger<WeatherForecastController> logger, HybridCache cache)
    {
        _logger = logger;
        _cache = cache; 
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        return GeWeatherForecastAsync().Result;

    }

    async Task<IEnumerable<WeatherForecast>> GeWeatherForecastAsync(CancellationToken token = default)
    {

        var entryOptions = new HybridCacheEntryOptions
        {
            LocalCacheExpiration = TimeSpan.FromSeconds(5), // Local cache expiration time
            Expiration = TimeSpan.FromSeconds(10), // Distributed cache expiration time
        };

        var response = await _cache.GetOrCreateAsync(
            $"weather", // Unique key to the cache entry
            cancel => new ValueTask<IEnumerable<WeatherForecast>>(GetDataFromTheSource()),
            cancellationToken: token,
            options: entryOptions
        );

        return response;
    }

    IEnumerable<WeatherForecast> GetDataFromTheSource()
    {
       Thread.Sleep(2000); // Simulate a long-running operation

        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })
        .ToArray();
    }


}
