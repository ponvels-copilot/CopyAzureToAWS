using CopyAzureToAWS.Api.Configuration;
using CopyAzureToAWS.Api.Infrastructure.Logging; // <-- added
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
    private readonly ILogger<CallDetailsController> _logger;

    public CallDetailsController(
        ISqsService sqsService,
        IUserAccessService userAccessService,
        IConfiguration configuration,
        ILogger<CallDetailsController> logger)
    {
        _sqsService = sqsService ?? throw new ArgumentNullException(nameof(sqsService));
        _userAccessService = userAccessService;
        _connResolver = new ConnectionStringResolver(configuration);
        _logger = logger;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        var requestId = HttpContext.ResolveRequestId();
        _logger.WriteLog("CallDetails.Health", "Health check OK", requestId);
        return Ok(new { status = "ok", service = "calldetails", timeUtc = DateTime.UtcNow });
    }

    /// <summary>
    /// Creates a call detail entry. Uses the Writer connection for the specified country (default US).
    /// </summary>
    [HttpPost("CreateCallDetail")]
    public async Task<IActionResult> CreateCallDetail([FromBody] AzureToAWSRequest request)
    {
        var requestId = HttpContext.ResolveRequestId();
        var country = string.IsNullOrWhiteSpace(request.CountryCode) ? "US" : request.CountryCode.Trim().ToUpperInvariant();
        _logger.WriteLog("CallDetails.Create.Received", $"Request CallDetailID={request.CallDetailID} AudioFile='{request.AudioFile}' Country={country}", requestId);

        try
        {
            await using var dbContext = CreateDbContext(country, writer: false);

            //Check if CallDetailID exists in call_recording_details and is marked as IsAzureCloudAudio = true
            var callRecordingDetails = await dbContext.TableCallRecordingDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(crd =>
                    crd.CallDetailID == request.CallDetailID &&
                    crd.AudioFile != null &&
                    crd.AudioFile.ToLower().Equals(request.AudioFile.ToLower()));

            if (callRecordingDetails == null)
            {
                _logger.WriteLog("CallDetails.Validation.NotFound",
                    $"No record for CallDetailID={request.CallDetailID} AudioFile='{request.AudioFile}'",
                    requestId,
                    success: false);

                return StatusCode((int)HttpStatusCode.BadRequest,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Message = $"AudioFile: {request.AudioFile}, CallDetailID: {request.CallDetailID} does not exist.",
                        RequestId = requestId
                    });
            }

            if (callRecordingDetails.IsAzureCloudAudio != true)
            {
                _logger.WriteLog("CallDetails.Validation.NotAzure",
                    $"Not AzureCloudAudio CallDetailID={request.CallDetailID}",
                    requestId,
                    success: false);

                return StatusCode((int)HttpStatusCode.BadRequest,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Message = $"AudioFile: {request.AudioFile}, CallDetailID: {request.CallDetailID} is not a Azure call recording.",
                        RequestId = requestId
                    });
            }

            // Existence check (Writer; could use reader if you prefer)
            var exists = await dbContext.TableAzureToAWSRequest
                .AnyAsync(cd => cd.CallDetailID == request.CallDetailID);

            if (exists)
            {
                _logger.WriteLog("CallDetails.Duplicate",
                    $"Already queued CallDetailID={request.CallDetailID}",
                    requestId,
                    success: false);

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

            _logger.WriteLog("CallDetails.Status.Write", $"Recording {StatusCode.INPROGRESS} status. CallDetailID={request.CallDetailID}", requestId);
            // Stored proc write (connection resolved by user access service – must also be country aware)
            var recorded = await RecordAzureToAWSStatus(row, country);
            if (!recorded)
            {
                _logger.WriteLog("CallDetails.Status.WriteFailed",
                    $"Failed to persist status CallDetailID={request.CallDetailID}",
                    requestId,
                    success: false);

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.InternalServerError,
                        Message = $"Failed to record status for CallDetailID {request.CallDetailID}",
                        RequestId = requestId
                    });
            }

            var sqsMessage = new SqsMessage
            {
                CallDetailID = request.CallDetailID,
                CountryCode = request.CountryCode!,
                AudioFile = request.AudioFile,
                RequestId = requestId
            };

            _logger.WriteLog("CallDetails.Queue.Send", $"Sending SQS message. CallDetailID={request.CallDetailID}", requestId);
            // Queue message
            var (queued, sqsexception) = await _sqsService.SendMessageAsync(sqsMessage);
            if (!queued)
            {
                #region
                var row_ERROR = new TableAzureToAWSRequest
                {
                    CallDetailID = request.CallDetailID,
                    AudioFile = request.AudioFile,
                    Status = StatusCode.ERROR.ToString(),
                    ErrorDescription = sqsexception!.ToString()
                };

                _logger.WriteLog("CallDetails.Status.Write", $"Recording {StatusCode.ERROR} status. CallDetailID={request.CallDetailID}", requestId);
                // Stored proc write (connection resolved by user access service – must also be country aware)
                recorded = await RecordAzureToAWSStatus(row_ERROR, country);
                if (!recorded)
                {
                    _logger.WriteLog("CallDetails.Status.WriteFailed",
                        $"Failed to persist status CallDetailID={request.CallDetailID}",
                        requestId,
                        success: false);

                    return StatusCode((int)HttpStatusCode.InternalServerError,
                        new ApiResponse
                        {
                            IsSuccess = false,
                            StatusCode = (int)HttpStatusCode.InternalServerError,
                            Message = $"Failed to record status {StatusCode.ERROR} for CallDetailID {request.CallDetailID}",
                            RequestId = requestId
                        });
                }
                #endregion

                _logger.WriteLog("CallDetails.Queue.Failed",
                    $"SQS enqueue failed CallDetailID={request.CallDetailID}",
                    requestId,
                    success: false);

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.InternalServerError,
                        Message = $"Failed to queue message for CallDetailID {request.CallDetailID}",
                        RequestId = requestId
                    });
            }

            _logger.WriteLog("CallDetails.Queue.Success",
                $"Enqueued successfully CallDetailID={request.CallDetailID}",
                requestId);

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
            _logger.WriteLog("CallDetails.Create.Error",
                $"Unhandled exception CallDetailID={request.CallDetailID}",
                requestId,
                success: false,
                exception: ex);

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
        var cs = writer ? _connResolver.GetWriter(countryCode) : _connResolver.GetReader(countryCode);
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
            _logger.WriteLog("CallDetails.Status.StoredProcError",
                $"Stored proc failure CallDetailID={row.CallDetailID} Country={countryCode}",
                HttpContext.ResolveRequestId(),
                success: false,
                exception: ex);
            return false;
        }
    }

    [HttpGet("db-resolve")]
    public IActionResult Resolve([FromQuery] string country = "US")
    {
        var requestId = HttpContext.ResolveRequestId();
        _logger.WriteLog("CallDetails.DbResolve", $"Resolving connection strings for {country}", requestId);
        return Ok(new
        {
            country = country.ToUpperInvariant(),
            writer = Trunc(_connResolver.GetWriter(country)),
            reader = Trunc(_connResolver.GetReader(country))
        });

        static string Trunc(string s) => s.Length <= 90 ? s : s[..90] + "...";
    }
}