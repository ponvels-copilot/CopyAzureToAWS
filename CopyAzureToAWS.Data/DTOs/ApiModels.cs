namespace CopyAzureToAWS.Data.DTOs;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
}

public class CallDetailRequest
{
    public string CallDetailId { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string AzureConnectionString { get; set; } = string.Empty;
    public string AzureBlobUrl { get; set; } = string.Empty;
    public string S3BucketName { get; set; } = string.Empty;
}

public class CallDetailResponse
{
    public int Id { get; set; }
    public string CallDetailId { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SqsMessage
{
    public string CallDetailId { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string AzureConnectionString { get; set; } = string.Empty;
    public string AzureBlobUrl { get; set; } = string.Empty;
    public string S3BucketName { get; set; } = string.Empty;
}