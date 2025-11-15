using Microsoft.Extensions.Configuration;

namespace AzureToAWS.Api.Infrastructure;

public static class DbConfigHelper
{
    public static (string FnName, string Role) ParseFnAndRole(string? cfg, string defaultFn, string defaultRole = "Writer")
    {
        if (string.IsNullOrWhiteSpace(cfg))
            return (defaultFn, defaultRole);

        var parts = cfg.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var fn = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : defaultFn;
        var role = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : defaultRole;

        if (!role.Equals("Reader", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Writer", StringComparison.OrdinalIgnoreCase))
        {
            role = defaultRole;
        }

        return (fn, role);
    }

    public static string ResolveConnectionString(IConfiguration configuration, string role, string countrycode = "US")
    {
        var name = role.Equals("Writer", StringComparison.OrdinalIgnoreCase) ? string.Concat(countrycode, "WriterConnection") : string.Concat(countrycode, "ReaderConnection");
        var cs = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"Missing ConnectionStrings:{name}");
        return cs!;
    }
}