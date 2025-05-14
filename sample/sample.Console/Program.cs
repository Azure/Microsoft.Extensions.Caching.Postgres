using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        // Add configuration sources here
        config.AddEnvironmentVariables();
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddHybridCache();

        services.AddDistributedPostgresCache(options =>
        {
            // Configure the Postgres cache options here
            options.ConnectionString = context.Configuration.GetConnectionString("PostgresCache");
            options.SchemaName = "public";
            options.TableName = "Cache";
            //options.CreateIfNotExists = true;
        });
        // Register your services here
        services.AddScoped<IConsoleService, ConsoleService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders(); // Optional: Removes default providers
        logging.SetMinimumLevel(LogLevel.Information); // Sets log level
        logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.ffffff] "; // Custom timestamp format
            options.SingleLine = true; // Optional: Single line output
        });

    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Console logging is now enabled!");

var consoleService = host.Services.GetRequiredService<IConsoleService>();

await consoleService.RunAsync();

public interface IConsoleService
{
    Task RunAsync();
}

public class ConsoleService : IConsoleService
{

    private readonly ILogger<ConsoleService> _logger;
    private HybridCache _cache;

    public ConsoleService(ILogger<ConsoleService> logger, HybridCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Console Service Started.");

        var token = new CancellationToken();
        var stopwatch = new Stopwatch();

        var entryOptions = new HybridCacheEntryOptions
        {
            LocalCacheExpiration = TimeSpan.FromSeconds(5), // Local cache expiration time
            Expiration = TimeSpan.FromSeconds(10), // Distributed cache expiration time
        };

        while (true)
        {
            stopwatch.Restart();

            var response = await _cache.GetOrCreateAsync(
                $"weather", // Unique key to the cache entry
                cancel => new ValueTask<IEnumerable<WeatherForecast>>(GetDataFromTheSource()),
                cancellationToken: token,
                options: entryOptions
            );

            stopwatch.Stop();
            
            Thread.Sleep(500); // take a break for 500ms
            _logger.LogInformation("Elapsed Milliseconds: {Elapsed} - Forecast - {Data}", stopwatch.ElapsedTicks/1000, response);
        }
    }

    IEnumerable<WeatherForecast> GetDataFromTheSource()
    {
        Thread.Sleep(2000); // Simulate a long-running operation
        _logger.LogInformation("Fetching Weather");

        return Enumerable.Range(1, 1).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = WeatherForecast.Summaries[Random.Shared.Next(WeatherForecast.Summaries.Length)]
        })
        .ToArray();
    }
}
