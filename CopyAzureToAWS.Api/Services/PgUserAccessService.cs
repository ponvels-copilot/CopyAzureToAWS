using AzureToAWS.Api.Infrastructure;
using AzureToAWS.Data.DTOs;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace AzureToAWS.Api.Services;

public interface IUserAccessService
{
    Task<bool> ValidateCredentialsAsync(string accessKey, string accessSecret);
    (string, string) GetRecordAzureToAWSStatusConnectionString(string countryCode);
}

public class PgUserAccessService : IUserAccessService
{
    private const string CacheKey = "ExtendedDataUsersCache";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public PgUserAccessService(IConfiguration configuration, IMemoryCache cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<bool> ValidateCredentialsAsync(string accessKey, string accessSecret)
    {
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(accessSecret))
            return false;

        var users = await LoadOrGetCacheAsync();
        return users.Any(u =>
            string.Equals(u.AccessKey, accessKey, StringComparison.Ordinal) &&
            string.Equals(u.AccessSecret, accessSecret, StringComparison.Ordinal));
    }

    public (string, string) GetRecordAzureToAWSStatusConnectionString(string countryCode)
    {
        // The method does not contain any await, so it should not be marked async.
        // Remove 'async' and return synchronously.
        var (fnName, role) = DbConfigHelper.ParseFnAndRole(
                _configuration["RecordAzureToAWSStatus"],
                "dbo.usp_record_azure_to_aws_status",
                "Writer");

        var cs = DbConfigHelper.ResolveConnectionString(_configuration, role, countryCode);

        return (fnName, cs);
    }

    private async Task<IReadOnlyList<ExtendedDataUsers>> LoadOrGetCacheAsync()
    {
        var users = await _cache.GetOrCreateAsync<IReadOnlyList<ExtendedDataUsers>>(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var (fnName, role) = DbConfigHelper.ParseFnAndRole(
                _configuration["GetExtendedDataUsersCache"],
                "dbo.fn_get_extended_data_users_cache",
                "Reader");

            var cs = DbConfigHelper.ResolveConnectionString(_configuration, role);
            var sql =
                $"select \"Indicator\", \"CountryCode\", \"ClientCode\", \"ExtendedDataType\", " +
                $"\"AccessKey\", \"AccessSecret\", \"ApplicationID\", \"ExtendedDataUsersMapID\" " +
                $"from {fnName}()";

            var list = new List<ExtendedDataUsers>();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            var ordIndicator = rdr.GetOrdinal("Indicator");
            var ordCountryCode = rdr.GetOrdinal("CountryCode");
            var ordClientCode = rdr.GetOrdinal("ClientCode");
            var ordExtendedDataType = rdr.GetOrdinal("ExtendedDataType");
            var ordAccessKey = rdr.GetOrdinal("AccessKey");
            var ordAccessSecret = rdr.GetOrdinal("AccessSecret");
            var ordApplicationID = rdr.GetOrdinal("ApplicationID");
            var ordMapId = rdr.GetOrdinal("ExtendedDataUsersMapID");

            while (await rdr.ReadAsync())
            {
                var item = new ExtendedDataUsers
                {
                    Indicator = rdr.IsDBNull(ordIndicator) ? string.Empty : rdr.GetString(ordIndicator),
                    CountryCode = rdr.IsDBNull(ordCountryCode) ? string.Empty : rdr.GetString(ordCountryCode),
                    ClientCode = rdr.IsDBNull(ordClientCode) ? string.Empty : rdr.GetString(ordClientCode),
                    ExtendedDataType = rdr.IsDBNull(ordExtendedDataType) ? string.Empty : rdr.GetString(ordExtendedDataType),
                    AccessKey = rdr.IsDBNull(ordAccessKey) ? string.Empty : rdr.GetString(ordAccessKey),
                    AccessSecret = rdr.IsDBNull(ordAccessSecret) ? string.Empty : rdr.GetString(ordAccessSecret),
                    ApplicationID = rdr.IsDBNull(ordApplicationID) ? string.Empty : rdr.GetString(ordApplicationID),
                    ExtendedDataUsersMapID = rdr.IsDBNull(ordMapId) ? 0L : rdr.GetInt64(ordMapId)
                };
                list.Add(item);
            }

            return (IReadOnlyList<ExtendedDataUsers>)list;
        });

        // Ensure a non-null return value
        return users ?? Array.Empty<ExtendedDataUsers>();
    }
}