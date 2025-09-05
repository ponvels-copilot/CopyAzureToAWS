using CopyAzureToAWS.Api.Configuration;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using System.Net;

namespace CopyAzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallDetailsController : ControllerBase
{
    private new enum StatusCode { INPROGRESS, SUCCESS, ERROR }

    private readonly ISqsService _sqsService;
    private readonly IUserAccessService _userAccessService;
    private readonly IConnectionStringResolver _connResolver;

    // Removed resolver from DI, build it from IConfiguration
    public CallDetailsController(
        ISqsService sqsService,
        IUserAccessService userAccessService,
        IConfiguration configuration)
    {
        _sqsService = sqsService;
        _userAccessService = userAccessService;
        _connResolver = new ConnectionStringResolver(configuration);
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() =>
        Ok(new { status = "ok", service = "calldetails", timeUtc = DateTime.UtcNow });

    /// <summary>
    /// Creates a call detail entry. Uses the Writer connection for the specified country (default US).
    /// </summary>
    [HttpPost("CreateCallDetail")]
    public async Task<IActionResult> CreateCallDetail([FromBody] AzureToAWSRequest request)
    {
        var requestId = Guid.NewGuid().ToString();
        var country = string.IsNullOrWhiteSpace(request.CountryCode) ? "US" : request.CountryCode.Trim().ToUpperInvariant();

        try
        {
            await using var dbContext = CreateDbContext(country, writer: false);

            //Check if CallDetailID exists in call_recording_details and is marked as IsAzureCloudAudio = true
            var callRecordingDetails = await dbContext.TableCallRecordingDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(crd => crd.CallDetailID == request.CallDetailID && crd.AudioFile != null && crd.AudioFile.ToLower().Equals(request.AudioFile.ToLower()));

            if (callRecordingDetails == null || callRecordingDetails.IsAzureCloudAudio != true)
            {
                return StatusCode((int)HttpStatusCode.BadRequest,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Message = callRecordingDetails == null ? $"AudioFile: {request.AudioFile}, CallDetailID: {request.CallDetailID} does not exist." : $"AudioFile: {request.AudioFile}, CallDetailID: {request.CallDetailID} is not a Azure call recording.",
                        RequestId = requestId
                    });
            }

            // Existence check (Writer; could use reader if you prefer)
            var exists = await dbContext.TableAzureToAWSRequest
            .AnyAsync(cd => cd.CallDetailID == request.CallDetailID);

            if (exists)
            {
                return StatusCode((int)HttpStatusCode.Conflict,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.Conflict,
                        Message = $"Calldetailid: {request.CallDetailID} already queued and in processing state now.",
                        RequestId = requestId
                    });
            }

            var row = new TableAzureToAWSRequest
            {
                CallDetailID = request.CallDetailID,
                AudioFile = request.AudioFile,
                Status = StatusCode.INPROGRESS.ToString(),
                CreatedBy = "API",
                CreatedDate = DateTime.UtcNow
            };

            // Stored proc write (connection resolved by user access service – must also be country aware)
            var recorded = await RecordAzureToAWSStatus(row, country);
            if (!recorded)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.InternalServerError,
                        Message = $"Failed to record status for CallDetailID {request.CallDetailID}",
                        RequestId = requestId
                    });
            }

            return StatusCode((int)HttpStatusCode.OK,
                    new ApiResponse
                    {
                        IsSuccess = true,
                        StatusCode = (int)HttpStatusCode.OK,
                        Message = $"Calldetailid: {request.CallDetailID} is queued sucessfully.",
                        RequestId = requestId
                    });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Internal server error",
                error = ex.Message,
                requestId
            });
        }
    }

    /// <summary>
    /// Build a context for a specific country & role using dynamic connection strings:
    /// Keys expected: USWriterConnection, USReaderConnection, CAWriterConnection, CAReaderConnection, etc.
    /// </summary>
    private ApplicationDbContext CreateDbContext(string countryCode, bool writer)
    {
        var cs = writer
            ? _connResolver.GetWriter(countryCode)
            : _connResolver.GetReader(countryCode);

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs)
            .EnableSensitiveDataLogging(false)
            .Options;

        return new ApplicationDbContext(opts);
    }

    private async Task<bool> RecordAzureToAWSStatus(TableAzureToAWSRequest row, string countryCode)
    {
        try
        {
            (string procName, string connString) = _userAccessService.GetRecordAzureToAWSStatusConnectionString(countryCode);

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand
            {
                Connection = conn,
                CommandText = $"CALL {procName}(:p_json);",
                CommandType = System.Data.CommandType.Text,
                CommandTimeout = 300
            };
            cmd.Parameters.AddWithValue("p_json", NpgsqlDbType.Jsonb, JsonConvert.SerializeObject(row));
            await cmd.ExecuteNonQueryAsync();

            return true;
        }
        catch (Exception ex)
        {
            //Console.WriteLine(JsonConvert.SerializeObject(new
            //{
            //    row.RequestId,
            //    row.AudioFile,
            //    Country = countryCode,
            //    Error = ex.Message,
            //    Op = "RecordAzureToAWSStatus"
            //}));
            return false;
        }
    }

    [HttpGet("db-resolve")]
    public IActionResult Resolve([FromQuery] string country = "US")
    {
        return Ok(new
        {
            country = country.ToUpperInvariant(),
            writer = Trunc(_connResolver.GetWriter(country)),
            reader = Trunc(_connResolver.GetReader(country))
        });

        static string Trunc(string s) => s.Length <= 90 ? s : s[..90] + "...";
    }
}