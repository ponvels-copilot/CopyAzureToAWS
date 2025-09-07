namespace CopyAzureToAWS.Data.DTOs;

public class SqsMessage
{
    public long CallDetailID { get; set; }
    public string? CountryCode { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}