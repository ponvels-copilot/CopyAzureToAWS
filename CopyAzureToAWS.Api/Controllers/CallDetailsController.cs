using AzureToAWS.Api.Configuration;
using AzureToAWS.Api.Infrastructure.Logging;
using AzureToAWS.Api.Services;
using AzureToAWS.Data;
using AzureToAWS.Data.DTOs;
using AzureToAWS.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using System.Net;
using System.Runtime.InteropServices;

namespace AzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallDetailsController : ControllerBase
{
    private const string CreatedBy = "API.CallDetailsController";

    private new enum StatusCode { INPROGRESS, SUCCESS, ERROR }

    private readonly ISqsService _sqsService;
    private readonly IUserAccessService _userAccessService;
    private readonly IConnectionStringResolver _connResolver;
    private readonly ILogger<CallDetailsController> _logger;

    public DateTime GetDateTimeInEST
    {
        get
        {
            return DateTime.SpecifyKind(GetCurrentEasternTime(), DateTimeKind.Unspecified);
        }
    }

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
    /// Processes a request to queue an Azure call recording for transfer to AWS.
    /// </summary>
    /// <remarks>This method validates the provided call detail ID and audio file, ensures the recording is an
    /// Azure call recording, and checks for duplicate requests before queuing the recording for transfer to AWS.  If
    /// the operation fails at any step, an appropriate HTTP status code and error message are returned.</remarks>
    /// <param name="request">The request containing details about the Azure call recording, including the call detail ID, audio file name,
    /// and country code.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Possible responses include: <list
    /// type="bullet"> <item><description><see cref="StatusCodeResult"/> with status 200 (OK) if the recording is
    /// successfully queued.</description></item> <item><description><see cref="StatusCodeResult"/> with status 400 (Bad
    /// Request) if the call detail ID or audio file is invalid, or if the recording is not an Azure call
    /// recording.</description></item> <item><description><see cref="StatusCodeResult"/> with status 409 (Conflict) if
    /// the recording is already queued.</description></item> <item><description><see cref="StatusCodeResult"/> with
    /// status 500 (Internal Server Error) if an unexpected error occurs.</description></item> </list></returns>
    [HttpPost("GetAzureRecording")]
    public async Task<IActionResult> GetAzureRecording([FromBody] AzureToAWSRequest request)
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

            var rowInProgress = new TableAzureToAWSRequest
            {
                CallDetailID = request.CallDetailID,
                AudioFile = request.AudioFile,
                Status = StatusCode.INPROGRESS.ToString(),
                RequestId = requestId,
                ErrorDescription = null,
                CreatedBy = CreatedBy,
                CreatedDate = GetDateTimeInEST
            };

            _logger.WriteLog("CallDetails.Status.Write", $"Recording {StatusCode.INPROGRESS} status. CallDetailID={request.CallDetailID}", requestId);

            var (inserted, insertErr) = await AddAzureToAWSRequestAsync(rowInProgress, country);
            if (!inserted)
            {
                _logger.WriteLog("AzureToAWS.Status.WriteFailed",
                    $"Failed to persist status CallDetailID={request.CallDetailID}",
                    requestId,
                    success: false);

                return StatusCode((int)HttpStatusCode.InternalServerError,
                    new ApiResponse
                    {
                        IsSuccess = false,
                        StatusCode = (int)HttpStatusCode.InternalServerError,
                        Message = $"Failed to record status for CallDetailID {request.CallDetailID} with {StatusCode.INPROGRESS}",
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
                    RequestId = requestId,
                    ErrorDescription = sqsexception!.ToString(),
                    CreatedDate = GetDateTimeInEST,
                    CreatedBy = CreatedBy
                };

                _logger.WriteLog("CallDetails.Status.Write", $"Recording {StatusCode.ERROR} status. CallDetailID={request.CallDetailID}", requestId);
                // Stored proc write (connection resolved by user access service – must also be country aware)
                var (recorded, recordedExp) = await AddAzureToAWSRequestAsync(row_ERROR, country);
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

    /// <summary>
    /// Adds or updates an Azure-to-AWS request in the database based on the provided details.
    /// </summary>
    /// <remarks>This method ensures that only one "INPROGRESS" request exists for a given call detail ID and
    /// audio file. If the request status is "INPROGRESS" and a duplicate exists, the operation fails. Otherwise, the
    /// method either inserts a new request or moves an existing "INPROGRESS" request to an audit table before inserting
    /// the new request.</remarks>
    /// <param name="row">The request details to be added or updated, including the call detail ID, audio file, and status.</param>
    /// <param name="countryCode">The country code used to determine the database connection. If null or whitespace, defaults to "US".</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A tuple containing a boolean indicating success and an exception if the operation fails. If the operation is
    /// successful, <c>Success</c> is <see langword="true"/> and <c>StatusFailed</c> is <see langword="null"/>. If the
    /// operation fails, <c>Success</c> is <see langword="false"/> and <c>StatusFailed</c> contains the exception
    /// describing the failure.</returns>
    private async Task<(bool Success, Exception? StatusFailed)> AddAzureToAWSRequestAsync(TableAzureToAWSRequest row, string countryCode, CancellationToken ct = default)
    {
        var requestId = HttpContext.ResolveRequestId();
        const string logCtx = "AzureToAWS.AddRequest";
        try
        {
            if (row.CallDetailID <= 0)
                return (false, new Exception("CallDetailID must be > 0"));

            if (string.IsNullOrWhiteSpace(row.AudioFile))
                return (false, new Exception("AudioFile is required"));

            var normCountry = string.IsNullOrWhiteSpace(countryCode)
                ? "US"
                : countryCode.Trim().ToUpperInvariant();

            var writerCs = _connResolver.GetWriter(normCountry);
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(writerCs)
                .EnableSensitiveDataLogging(false)
                .Options;

            await using var db = new ApplicationDbContext(opts);

            var loweredAudio = row.AudioFile.Trim().ToLowerInvariant();
            var isInProgressRequest = row.Status.Equals(StatusCode.INPROGRESS.ToString(), StringComparison.OrdinalIgnoreCase);

            // Load existing (if any) for this CallDetailID (we only store one row per CallDetailID)
            var existing = await db.TableAzureToAWSRequest
                .FirstOrDefaultAsync(r => r.CallDetailID == row.CallDetailID && r.AudioFile.ToLower() == loweredAudio, ct);

            if (isInProgressRequest)
            {
                if (existing != null)
                {
                    _logger.WriteLog($"{logCtx}.Duplicate",
                        $"INPROGRESS already exists CallDetailID={row.CallDetailID} AudioFile='{row.AudioFile}'",
                        requestId,
                        success: false);
                    return (false, new Exception("Request already exists"));
                }

                await db.TableAzureToAWSRequest.AddAsync(row, ct);
                await db.SaveChangesAsync(ct);

                _logger.WriteLog($"{logCtx}.Success",
                    $"Inserted INPROGRESS CallDetailID={row.CallDetailID} AudioFile='{row.AudioFile}'",
                    requestId);
                return (true, null);
            }
            else
            {
                using var tx = await db.Database.BeginTransactionAsync(ct);

                // Copy existing to audit
                var audit = new TableAzureToAWSRequestAudit
                {
                    CallDetailID = existing.CallDetailID,
                    AudioFile = existing.AudioFile,
                    Status = existing.Status,
                    RequestId = existing.RequestId,
                    ErrorDescription = existing.ErrorDescription,
                    CreatedDate = existing.CreatedDate,
                    CreatedBy = existing.CreatedBy,
                    UpdatedDate = GetDateTimeInEST,
                    UpdatedBy = CreatedBy
                };
                await db.TableAzureToAWSRequestAudit.AddAsync(audit, ct);

                // Remove existing INPROGRESS row
                db.TableAzureToAWSRequest.Remove(existing);

                // Insert new final (e.g., ERROR) row
                audit = new TableAzureToAWSRequestAudit
                {
                    CallDetailID = row.CallDetailID,
                    AudioFile = row.AudioFile,
                    Status = row.Status,
                    RequestId = row.RequestId,
                    ErrorDescription = row.ErrorDescription,
                    CreatedDate = row.CreatedDate,
                    CreatedBy = row.CreatedBy
                };

                await db.TableAzureToAWSRequestAudit.AddAsync(audit, ct);

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.WriteLog($"{logCtx}.InProgressMoved",
                    $"Moved INPROGRESS to audit and inserted {row.Status.ToString()} CallDetailID={row.CallDetailID}",
                    requestId);

                return (true, null);
            }
        }
        catch (Exception ex)
        {
            _logger.WriteLog($"{logCtx}.Exception",
                $"Insert failed CallDetailID={row.CallDetailID} AudioFile='{row.AudioFile}'",
                requestId,
                success: false,
                exception: ex);
            return (false, ex);
        }
    }

    // Cached Eastern TimeZoneInfo (handles Windows vs Linux). Falls back to fixed -05:00 if not found.
    private static readonly Lazy<TimeZoneInfo> _easternTimeZone = new(() =>
    {
        try
        {
            // Windows uses "Eastern Standard Time"; Linux/AL2 uses IANA "America/New_York"
            var id = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Eastern Standard Time"
                : "America/New_York";
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("EST", TimeSpan.FromHours(-5), "Eastern Standard Time", "Eastern Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("EST", TimeSpan.FromHours(-5), "Eastern Standard Time", "Eastern Standard Time");
        }
    });
    // Replace this line:
    // string const Actor = "CallDetailsController";

    /// <summary>
    /// Returns the current Eastern Time (America/New_York) as a DateTime with Kind=Unspecified.
    /// Includes DST (EDT) when in effect. Use only if you truly must store local ET.
    /// Prefer storing UTC plus a separate zone indicator when possible.
    /// </summary>
    public static DateTime GetCurrentEasternTime()
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _easternTimeZone.Value);
        // Mark Unspecified to avoid misleading consumers into thinking it is UTC or local server time.
        return DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Returns the current Eastern Time as a DateTimeOffset preserving the correct UTC offset (-05:00 or -04:00).
    /// Prefer this when you need an absolute point in time + local offset.
    /// </summary>
    public static DateTimeOffset GetCurrentEasternTimeOffset()
    {
        var tz = _easternTimeZone.Value;
        var utcNow = DateTime.UtcNow;
        var offset = tz.GetUtcOffset(utcNow);
        return new DateTimeOffset(utcNow).ToOffset(offset);
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