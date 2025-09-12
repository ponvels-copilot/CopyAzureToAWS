using AzureRecordingLoadTest.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzureRecordingLoadTest;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddCommandLine(args)
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        try
        {
            logger.LogInformation("Azure Recording Load Test Started");
            
            // Get configuration values
            var accessKey = configuration["Authentication:AccessKey"] ?? throw new InvalidOperationException("AccessKey not configured");
            var accessSecret = configuration["Authentication:AccessSecret"] ?? throw new InvalidOperationException("AccessSecret not configured");
            var requestCount = int.Parse(configuration["LoadTest:RequestCount"] ?? "100");
            var testDataFile = configuration["LoadTest:TestDataFile"] ?? "testdata.csv";

            // Validate configuration
            if (string.IsNullOrEmpty(accessKey))
            {
                logger.LogError("Please configure your AccessKey in appsettings.json or via command line --Authentication:AccessKey");
                return;
            }

            if (string.IsNullOrEmpty(accessSecret))
            {
                logger.LogError("Please configure your AccessSecret in appsettings.json or via command line --Authentication:AccessSecret");
                return;
            }

            // Execute load test
            var loadTestEngine = serviceProvider.GetRequiredService<LoadTestEngine>();
            var results = await loadTestEngine.ExecuteLoadTestAsync(accessKey, accessSecret, testDataFile, requestCount);

            // Save results to file
            await SaveResultsToFileAsync(results, logger);

            logger.LogInformation("Azure Recording Load Test Completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during load test execution");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        // HTTP Client
        services.AddHttpClient<AuthenticationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<RecordingApiService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Services
        services.AddScoped<AuthenticationService>();
        services.AddScoped<RecordingApiService>();
        services.AddScoped<TestDataService>();
        services.AddScoped<LoadTestEngine>();

        // Configuration
        services.AddSingleton<IConfiguration>(configuration);
    }

    private static async Task SaveResultsToFileAsync(List<AzureRecordingLoadTest.Models.LoadTestResult> results, ILogger logger)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"load_test_results_{timestamp}.csv";
            
            var csvLines = new List<string>
            {
                "RequestId,IsSuccess,ResponseTimeMs,RequestTime,CountryCode,AudioFile,CallDetailID,Message"
            };

            foreach (var result in results)
            {
                var line = $"{result.RequestId},{result.IsSuccess},{result.ResponseTime.TotalMilliseconds:F2}," +
                          $"{result.RequestTime:yyyy-MM-dd HH:mm:ss},{result.CountryCode},{result.AudioFile}," +
                          $"{result.CallDetailID},\"{result.Message.Replace("\"", "\"\"")}\"";
                csvLines.Add(line);
            }

            await File.WriteAllLinesAsync(fileName, csvLines);
            logger.LogInformation($"Load test results saved to: {fileName}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving results to file");
        }
    }
}
