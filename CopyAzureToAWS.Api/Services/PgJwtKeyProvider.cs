using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using AzureToAWS.Api.Infrastructure;

namespace AzureToAWS.Api.Services;

public interface IJwtKeyProvider
{
    SymmetricSecurityKey GetSigningKey();
}

public class PgJwtKeyProvider : IJwtKeyProvider
{
    private const string CacheKey = "JwtSigningKeyBytes";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public PgJwtKeyProvider(IConfiguration configuration, IMemoryCache cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public SymmetricSecurityKey GetSigningKey()
    {
        var keyBytes = _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            var (fnName, role) = DbConfigHelper.ParseFnAndRole(
                _configuration["GetJwtTokenKey"],
                "dbo.fn_get_jwt_token_key",
                "Writer");

            var cs = DbConfigHelper.ResolveConnectionString(_configuration, role);

            using var conn = new NpgsqlConnection(cs);
            using var cmd = new NpgsqlCommand($"select {fnName}()", conn);
            conn.Open();
            var result = cmd.ExecuteScalar()?.ToString();

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("Database returned empty JWT token key.");

            return Encoding.UTF8.GetBytes(result);
        })!;

        return new SymmetricSecurityKey(keyBytes);
    }
}