using System.ComponentModel.DataAnnotations.Schema;

[Table("azure_to_aws_request", Schema = "dbo")]
public class TableAzureToAWSRequest
{
    [Column("calldetailid")]
    public long CallDetailID { get; set; }

    [Column("audiofile")]
    public string AudioFile { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = string.Empty;

    //[Column("requestid")]
    //public string RequestId { get; set; } = string.Empty;

    //[Column("countrycode")]
    //public string CountryCode { get; set; } = string.Empty;

    [Column("createdby")]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("createddate")]
    public DateTime CreatedDate { get; set; }
}