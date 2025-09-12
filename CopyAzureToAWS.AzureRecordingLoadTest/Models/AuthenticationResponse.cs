namespace AzureRecordingLoadTest.Models;

public class AuthenticationResponse
{
    public string token { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    
    public DateTime expires { get; set; }
}