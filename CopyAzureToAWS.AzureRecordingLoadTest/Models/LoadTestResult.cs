namespace AzureRecordingLoadTest.Models;

public class LoadTestResult
{
    public int RequestId { get; set; }
    public bool IsSuccess { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string CallDetailID { get; set; } = string.Empty;
}