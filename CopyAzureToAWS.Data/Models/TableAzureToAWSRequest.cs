using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("azure_to_aws_request", Schema = "dbo")]
public class TableAzureToAWSRequest
{
    [Required]
    [Column("calldetailid")]
    public long CallDetailID { get; set; }

    [Required]
    [Column("audiofile")]
    public string AudioFile { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public string Status { get; set; } = string.Empty;

    [Column("requestid")]
    public string RequestId { get; set; } = string.Empty;

    [Column("errordescription")]
    public string? ErrorDescription { get; set; }


    //[Column("countrycode")]
    //public string CountryCode { get; set; } = string.Empty;

    [Required]
    [Column("createdby")]
    public string CreatedBy { get; set; } = string.Empty;

    [Required]
    [Column("createddate")]
    public DateTime CreatedDate { get; set; }

    [Column("updatedby")]
    public string? UpdatedBy { get; set; }

    [Column("updateddate")]
    public DateTime? UpdatedDate { get; set; }
}