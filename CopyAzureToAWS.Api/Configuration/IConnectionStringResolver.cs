namespace AzureToAWS.Api.Configuration
{
    public interface IConnectionStringResolver
    {
        string GetWriter(string? countryCode = null);
        string GetReader(string? countryCode = null);
    }
}