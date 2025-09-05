using System.ComponentModel.DataAnnotations;

namespace CopyAzureToAWS.Data.DTOs;

public class AzureToAWSRequest
{
    [Required, StringLength(2, MinimumLength = 2, ErrorMessage = "CountryCode must be a 2-letter code")]
    public string? CountryCode { get; set; }

    [Required]
    public long CallDetailID { get; set; }

    [Required]
    public string AudioFile { get; set; } = string.Empty;
}