using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureToAWS.Data.Models;

[Table("storage", Schema = "dbo")]
public class TableStorage
{
    [Key]
    [Column("storageid")]
    public int StorageID { get; set; }

    [Required]
    [Column("storagetype")]
    public string StorageType { get; set; } = string.Empty; // e.g. AWS / AZURE / ...

    [Required]
    [Column("countryid")]
    public int CountryID { get; set; }

    // Raw JSON document (jsonb). Keep as string for simplicity; could be JsonDocument if desired.
    [Required]
    [Column("json", TypeName = "jsonb")]
    public string Json { get; set; } = "{}";

    [Required]
    [Column("defaultstorage")]
    public bool DefaultStorage { get; set; }

    [Required]
    [Column("activeind")]
    public bool ActiveInd { get; set; }

    [Required]
    [MaxLength(30)]
    [Column("createdby")]
    public string CreatedBy { get; set; } = string.Empty;

    [Required]
    [Column("createddate")]
    public DateTime CreatedDate { get; set; }

    [MaxLength(30)]
    [Column("updatedby")]
    public string? UpdatedBy { get; set; }

    [Column("updateddate")]
    public DateTime? UpdatedDate { get; set; }

    // Generated columns (ALWAYS STORED)
    [Column("bucketname")]
    public string? BucketName { get; set; }

    [Column("azureblobendpoint")]
    public string? AzureBlobEndpoint { get; set; }
}