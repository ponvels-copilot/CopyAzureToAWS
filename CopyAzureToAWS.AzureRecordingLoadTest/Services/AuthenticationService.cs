using AzureRecordingLoadTest.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AzureRecordingLoadTest.Services;

public class AuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly string _authUrl;

    public AuthenticationService(HttpClient httpClient, ILogger<AuthenticationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authUrl = "https://interactionmetadata-qa.iqor.com/v1/api/auth/login";
    }

    public async Task<AuthenticationResponse> AuthenticateAsync(string accessKey, string accessSecret)
    {
        try
        {
            var request = new AuthenticationRequest
            {
                AccessKey = accessKey,
                AccessSecret = accessSecret
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Attempting authentication...");
            var response = await _httpClient.PostAsync(_authUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<AuthenticationResponse>(responseContent);
                
                if (authResponse != null && !string.IsNullOrEmpty(authResponse.token))
                {
                    _logger.LogInformation("Authentication successful");
                    return new AuthenticationResponse 
                    { 
                        token = authResponse.token, 
                        IsSuccess = true, 
                        Message = "Authentication successful" 
                    };
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Authentication failed: {response.StatusCode} - {errorContent}");
            
            return new AuthenticationResponse 
            { 
                IsSuccess = false, 
                Message = $"Authentication failed: {response.StatusCode} - {errorContent}" 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during authentication");
            return new AuthenticationResponse 
            { 
                IsSuccess = false, 
                Message = $"Authentication exception: {ex.Message}" 
            };
        }
    }
}