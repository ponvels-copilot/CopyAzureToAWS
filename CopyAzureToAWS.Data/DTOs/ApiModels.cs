using System.ComponentModel.DataAnnotations;

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

public class ApiResponse
{
    public bool IsSuccess { get; set; } = false;
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}

public class CallDetailRequest
{
    public long CallDetailID { get; set; }
    public string AudioFileName { get; set; } = string.Empty;
    public string AzureConnectionString { get; set; } = string.Empty;
    public string AzureBlobUrl { get; set; } = string.Empty;
    public string S3BucketName { get; set; } = string.Empty;
}

public class CallDetailResponse
{
    public int Id { get; set; }
    public long CallDetailID { get; set; }
    public string AudioFileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ErrorMessage { get; set; }
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

public class Logging
{
    public string RequestId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;
    public Exception? Exception { get; set; }
}