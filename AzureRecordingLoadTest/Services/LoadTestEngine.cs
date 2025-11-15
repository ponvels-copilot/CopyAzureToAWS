using AzureRecordingLoadTest.Models;
using AzureRecordingLoadTest.Services;
using Microsoft.Extensions.Logging;

namespace AzureRecordingLoadTest.Services;

public class LoadTestEngine
{
    private readonly AuthenticationService _authService;
    private readonly RecordingApiService _recordingService;
    private readonly TestDataService _testDataService;
    private readonly ILogger<LoadTestEngine> _logger;

    public LoadTestEngine(
        AuthenticationService authService, 
        RecordingApiService recordingService,
        TestDataService testDataService,
        ILogger<LoadTestEngine> logger)
    {
        _authService = authService;
        _recordingService = recordingService;
        _testDataService = testDataService;
        _logger = logger;
    }

    public async Task<List<LoadTestResult>> ExecuteLoadTestAsync(
        string accessKey, 
        string accessSecret, 
        string testDataFilePath, 
        int requestCount = 100)
    {
        _logger.LogInformation($"Starting load test with {requestCount} requests");
        
        // Step 1: Authenticate and get JWT token
        _logger.LogInformation("Step 1: Authenticating...");
        var authResult = await _authService.AuthenticateAsync(accessKey, accessSecret);
        
        if (!authResult.IsSuccess)
        {
            _logger.LogError($"Authentication failed: {authResult.Message}");
            return new List<LoadTestResult>();
        }

        _logger.LogInformation("Authentication successful, JWT token obtained");

        // Step 2: Load test data
        _logger.LogInformation("Step 2: Loading test data...");
        var sourceData = await _testDataService.LoadTestDataAsync(testDataFilePath);
        
        if (sourceData.Count == 0)
        {
            _logger.LogError("No test data available");
            return new List<LoadTestResult>();
        }

        // Step 3: Generate load test data
        var loadTestData = _testDataService.GenerateTestDataForLoad(sourceData, requestCount);

        // Step 4: Execute load test
        _logger.LogInformation($"Step 3: Executing {requestCount} concurrent requests...");
        
        var results = new List<LoadTestResult>();
        var tasks = new List<Task<LoadTestResult>>();

        var semaphore = new SemaphoreSlim(20, 20); // Limit concurrent requests to 20

        for (int i = 0; i < loadTestData.Count; i++)
        {
            var requestId = i + 1;
            var testData = loadTestData[i];
            
            var task = Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await _recordingService.PostRecordingRequestAsync(testData, authResult.Token, requestId);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            tasks.Add(task);
        }

        // Wait for all requests to complete
        results.AddRange(await Task.WhenAll(tasks));

        // Log summary
        LogLoadTestSummary(results);

        return results;
    }

    private void LogLoadTestSummary(List<LoadTestResult> results)
    {
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count - successCount;
        var averageResponseTime = results.Average(r => r.ResponseTime.TotalMilliseconds);
        var minResponseTime = results.Min(r => r.ResponseTime.TotalMilliseconds);
        var maxResponseTime = results.Max(r => r.ResponseTime.TotalMilliseconds);

        _logger.LogInformation("=== LOAD TEST SUMMARY ===");
        _logger.LogInformation($"Total Requests: {results.Count}");
        _logger.LogInformation($"Successful Requests: {successCount} ({(double)successCount / results.Count:P})");
        _logger.LogInformation($"Failed Requests: {failureCount} ({(double)failureCount / results.Count:P})");
        _logger.LogInformation($"Average Response Time: {averageResponseTime:F2} ms");
        _logger.LogInformation($"Min Response Time: {minResponseTime:F2} ms");
        _logger.LogInformation($"Max Response Time: {maxResponseTime:F2} ms");

        // Log failure details
        var failures = results.Where(r => !r.IsSuccess).ToList();
        if (failures.Any())
        {
            _logger.LogWarning("=== FAILURE DETAILS ===");
            foreach (var failure in failures.Take(10)) // Show first 10 failures
            {
                _logger.LogWarning($"Request {failure.RequestId}: {failure.Message}");
            }
            
            if (failures.Count > 10)
            {
                _logger.LogWarning($"... and {failures.Count - 10} more failures");
            }
        }
    }
}