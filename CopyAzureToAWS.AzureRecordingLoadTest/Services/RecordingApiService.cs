using AzureRecordingLoadTest.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

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
            var requestPayload = new RecordingRequest
            {
                CountryCode = testData.CountryCode,
                AudioFile = testData.AudioFile,
                CallDetailID = testData.CallDetailID
            };

            var json = JsonSerializer.Serialize(requestPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _recordingUrl)
            {
                Content = content
            };
            // Set per-request Authorization header (thread-safe)
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

            _logger.LogInformation("Posting recording request {RequestId} for CallDetailID: {CallDetailID}", requestId, testData.CallDetailID);

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
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

            _logger.LogInformation("Request {RequestId} completed in {Elapsed} ms - {Status}",
                requestId, stopwatch.ElapsedMilliseconds, result.IsSuccess ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception in request {RequestId}", requestId);

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