using System.Text.Json.Serialization;

namespace AzureToAWS.Data.DTOs;

public class StorageAZURE
{
    [JsonPropertyName("MSAzureBlob")]
    public MSAzureBlob? MSAzureBlob { get; set; }

    [JsonPropertyName("MSAzureKeyVault")]
    public MSAzureKeyVault? MSAzureKeyVault { get; set; }
}

public class MSAzureBlob
{
    public string? EndPoint { get; set; }
    public string? AccountKey { get; set; }
    public string? AccountName { get; set; }
    public string? ConnectionString { get; set; }
}

public class MSAzureKeyVault
{
    public string? ClientId { get; set; }
    public string? TenantID { get; set; }
    public string? KeyVaultURI { get; set; }
    public string? ClientSecret { get; set; }
}