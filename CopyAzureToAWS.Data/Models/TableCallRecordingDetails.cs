using System.ComponentModel.DataAnnotations.Schema;

namespace CopyAzureToAWS.Data.Models;

[Table("call_recording_details", Schema = "dbo")]
public class TableCallRecordingDetails
{
    [Column("callrecordingdetailsid")]
    public long CallRecordingDetailsID { get; set; }

    [Column("calldetailid")]
    public long CallDetailID { get; set; }

    [Column("audiofile")]
    public string? AudioFile { get; set; }

    [Column("videofile")]
    public string? VideoFile { get; set; }

    [Column("isazurecloudaudio")]
    public bool? IsAzureCloudAudio { get; set; }

    [Column("isazurecloudvideo")]
    public bool? IsAzureCloudVideo { get; set; }

    [Column("audiofilelocation")]
    public string? AudioFileLocation { get; set; }

    [Column("videofilelocation")]
    public string? VideoFileLocation { get; set; }
}