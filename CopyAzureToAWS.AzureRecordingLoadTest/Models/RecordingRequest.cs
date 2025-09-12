namespace AzureRecordingLoadTest.Models;

public class RecordingRequest
{
    public string CountryCode { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string CallDetailID { get; set; } = string.Empty;
}