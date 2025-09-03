namespace CopyAzureToAWS.Data.DTOs;

public class LoginRequest
{
    public string AccessKey { get; set; } = string.Empty;
    public string AccessSecret { get; set; } = string.Empty;
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

public class ExtendedDataUsers
{
    public string Indicator { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string ClientCode { get; set; } = string.Empty;
    public string ExtendedDataType { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string AccessSecret { get; set; } = string.Empty;
    public string ApplicationID { get; set; } = string.Empty;
    public long ExtendedDataUsersMapID { get; set; }
}