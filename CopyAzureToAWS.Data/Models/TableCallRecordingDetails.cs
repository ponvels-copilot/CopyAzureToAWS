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

    [Column("audiofilesize")]
    public long? AudioFileSize { get; set; }

    [Column("videofilesize")]
    public long? VideoFileSize { get; set; }

    [Column("audiofilemd5hash")]
    public string? AudioFileMd5Hash { get; set; }

    [Column("videofilemd5hash")]
    public string? VideoFileMd5Hash { get; set; }
    [Column("audiostorageid")]
    public int? AudioStorageID { get; set; }
    [Column("videostorageid")]
    public int? VideoStorageID { get; set; }
    [Column("isencryptedaudio")]
    public string? IsEncryptedAudio { get; set; }
    [Column("isencryptedvideo")]
    public string? IsEncryptedVideo { get; set; }
    [Column("updatedby")]
    public string? UpdatedBy { get; set; }
    [Column("updateddt")]
    public DateTime? UpdatedDate { get; set; }
}