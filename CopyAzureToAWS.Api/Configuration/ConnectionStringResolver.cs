using Microsoft.Extensions.Configuration;

namespace AzureToAWS.Api.Configuration
{
    public class ConnectionStringResolver : IConnectionStringResolver
    {
        private readonly IConfiguration _config;

        public ConnectionStringResolver(IConfiguration config) => _config = config;

        public string GetWriter(string? countryCode = null) => Resolve(countryCode, isWriter: true);
        public string GetReader(string? countryCode = null) => Resolve(countryCode, isWriter: false);

        private string Resolve(string? cc, bool isWriter)
        {
            var country = string.IsNullOrWhiteSpace(cc) ? "US" : cc.Trim().ToUpperInvariant();
            var rolePart = isWriter ? "Writer" : "Reader"; // e.g. USWriterConnection
            var key = $"{country}{rolePart}Connection";

            // Try exact key
            var val = _config.GetConnectionString(key) ?? _config[$"ConnectionStrings:{key}"];
            if (!string.IsNullOrWhiteSpace(val))
                return val;

            // Fallback to generic WriterConnection / ReaderConnection if you later add them
            var generic = _config.GetConnectionString($"{rolePart}Connection") ?? _config[$"ConnectionStrings:{rolePart}Connection"];
            if (!string.IsNullOrWhiteSpace(generic))
                return generic;

            // Final fallback: US writer/reader
            if (country != "US")
            {
                var usKey = $"US{rolePart}Connection";
                var usVal = _config.GetConnectionString(usKey) ?? _config[$"ConnectionStrings:{usKey}"];
                if (!string.IsNullOrWhiteSpace(usVal))
                    return usVal;
            }

            throw new InvalidOperationException($"No connection string found for key '{key}' (role={rolePart}, country={country}).");
        }
    }
}