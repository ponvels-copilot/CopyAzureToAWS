namespace CopyAzureToAWS.Data.DTOs;

public class CallDetailInfo
{
    public  DateTime? CallDate { get; set; }
    public long CallDetailID { get; set; }
    public string? ProgramCode { get; set; }
    public string? AudioFile { get; set; }
    public string? AudioFileLocation { get; set; }
    public bool? IsAzureCloudAudio { get; set; }
}