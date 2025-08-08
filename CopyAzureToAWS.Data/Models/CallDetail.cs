using System.ComponentModel.DataAnnotations;

namespace CopyAzureToAWS.Data.Models;

public class CallDetail
{
    [Key]
    public int Id { get; set; }
    
    public string CallDetailId { get; set; } = string.Empty;
    
    public string AudioFileName { get; set; } = string.Empty;
    
    public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed
    
    public string? AzureConnectionString { get; set; }
    
    public string? AzureBlobUrl { get; set; }
    
    public string? S3BucketName { get; set; }
    
    public string? S3Key { get; set; }
    
    public string? Md5Checksum { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
}
