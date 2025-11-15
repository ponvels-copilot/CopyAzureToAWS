using AzureRecordingLoadTest.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AzureRecordingLoadTest.Services;

public class RecordingApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecordingApiService> _logger;
    private readonly string _recordingUrl;

    public RecordingApiService(HttpClient httpClient, ILogger<RecordingApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _recordingUrl = "https://interactionmetadata-qa.iqor.com/v1/api/calldetails/GetAzureRecording";
    }

    public async Task<LoadTestResult> PostRecordingRequestAsync(TestDataItem testData, string jwtToken, int requestId)
    {
        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var request = new RecordingRequest
            {
                CountryCode = testData.CountryCode,
                AudioFile = testData.AudioFile,
                CallDetailID = testData.CallDetailID
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Add JWT token to request headers
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwtToken}");

            _logger.LogInformation($"Posting recording request {requestId} for CallDetailID: {testData.CallDetailID}");
            
            var response = await _httpClient.PostAsync(_recordingUrl, content);
            stopwatch.Stop();

            var responseContent = await response.Content.ReadAsStringAsync();

            var result = new LoadTestResult
            {
                RequestId = requestId,
                IsSuccess = response.IsSuccessStatusCode,
                ResponseTime = stopwatch.Elapsed,
                RequestTime = startTime,
                CountryCode = testData.CountryCode,
                AudioFile = testData.AudioFile,
                CallDetailID = testData.CallDetailID,
                Message = response.IsSuccessStatusCode 
                    ? $"Success: {response.StatusCode}" 
                    : $"Failed: {response.StatusCode} - {responseContent}"
            };

            _logger.LogInformation($"Request {requestId} completed in {stopwatch.ElapsedMilliseconds}ms - {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Exception in request {requestId}");
            
            return new LoadTestResult
            {
                RequestId = requestId,
                IsSuccess = false,
                ResponseTime = stopwatch.Elapsed,
                RequestTime = startTime,
                CountryCode = testData.CountryCode,
                AudioFile = testData.AudioFile,
                CallDetailID = testData.CallDetailID,
                Message = $"Exception: {ex.Message}"
            };
        }
    }
}