namespace AzureRecordingLoadTest.Models;

public class AuthenticationRequest
{
    public string AccessKey { get; set; } = string.Empty;
    public string AccessSecret { get; set; } = string.Empty;
}