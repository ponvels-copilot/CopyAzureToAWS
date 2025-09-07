using System.Text.Json.Serialization;

namespace CopyAzureToAWS.Data.DTOs;

public class StorageConfig
{
    [JsonPropertyName("MSAzureBlob")]
    public AzureBlobConfig? MSAzureBlob { get; set; }

    [JsonPropertyName("MSAzureKeyVault")]
    public AzureKeyVaultConfig? MSAzureKeyVault { get; set; }
}

public class AzureBlobConfig
{
    public string? EndPoint { get; set; }
    public string? AccountKey { get; set; }
    public string? AccountName { get; set; }
    public string? ConnectionString { get; set; }
}

public class AzureKeyVaultConfig
{
    public string? ClientId { get; set; }
    public string? TenantID { get; set; }
    public string? KeyVaultURI { get; set; }
    public string? ClientSecret { get; set; }
}