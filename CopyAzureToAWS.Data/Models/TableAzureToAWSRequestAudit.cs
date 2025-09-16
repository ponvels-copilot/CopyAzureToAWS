using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AzureToAWS.Data.Models
{
    [Keyless]
    [Table("azure_to_aws_request", Schema = "audit")]
    public class TableAzureToAWSRequestAudit
    {
        [Column("id")]
        public long ID { get; set; }

        [Column("calldetailid")]
        public long CallDetailID { get; set; }

        [Required]
        [Column("audiofile")]
        public string AudioFile { get; set; } = string.Empty;

        [Required]
        [Column("status")]
        public string Status { get; set; } = string.Empty;

        [Column("errordescription")]
        public string? ErrorDescription { get; set; }

        [Column("requestid")]
        public string RequestId { get; set; } = string.Empty;

        [Required]
        [Column("createddate")]
        public DateTime CreatedDate { get; set; }

        [Required]
        [MaxLength(30)]
        [Column("createdby")]
        public string CreatedBy { get; set; } = string.Empty;

        [Column("updatedby")]
        public string? UpdatedBy { get; set; }

        [Column("updateddate")]
        public DateTime? UpdatedDate { get; set; }
    }
}