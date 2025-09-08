using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Azure.Core;
using CopyAzureToAWS.Common.Utilities;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using CopyAzureToAWS.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CopyAzureToAWS.Processor.Lambda;

public class Function
{
    private enum StatusCode { INPROGRESS, SUCCESS, ERROR }

    private const string Const_arn = "arn";
    private const string Const_alias = "alias";
    private const string Const_clientcode = "clientcode";
    private const string Const_systemname = "systemname";

    private const string Const_programcode = "programcode";

    private IAmazonS3? _s3Client;
    private IAmazonS3? _s3ClientCanada;
    private readonly AmazonSQSClient? _sqsClient;
    private readonly IAmazonSecretsManager? _secretsManagerClient;
    private readonly AmazonDynamoDBClient? _dynamoDBClient;

    private readonly string secretId = Environment.GetEnvironmentVariable("SECRET_ID")!;
    private readonly int SecretsManagerTimeOutInSeconds = int.TryParse(Environment.GetEnvironmentVariable("SecretsManagerTimeOutInSeconds"), out var timeout) ? timeout : 30;
    private readonly string TableClientCountryKMSMap = Environment.GetEnvironmentVariable("TableClientCountryKMSMap")!;
    private readonly string RECORD_AZURE_TO_AWS_STATUS = Environment.GetEnvironmentVariable("RECORD_AZURE_TO_AWS_STATUS")!;
    private readonly string USS3BucketName = Environment.GetEnvironmentVariable("USS3BucketName")!;
    private readonly string CAS3BucketName = Environment.GetEnvironmentVariable("CAS3BucketName")!;
    private readonly int CommandTimeout = int.TryParse(Environment.GetEnvironmentVariable("CommandTimeout"), out var timeout) ? timeout : 300;

    private static string RequestId = string.Empty;
    private static string Key = string.Empty;
    private static bool EnableXrayTrace
    {
        get
        {
            if (Environment.GetEnvironmentVariable("EnableXrayTrace") == null)
                return false;
            else
                return bool.Parse(Environment.GetEnvironmentVariable("EnableXrayTrace")!.ToString());
        }
    }

    private static bool VerboseLoggging
    {
        get
        {
            if (Environment.GetEnvironmentVariable("VerboseLoggging") == null)
                return false;
            else
                return bool.Parse(Environment.GetEnvironmentVariable("VerboseLoggging")!.ToString());
        }
    }

    private static string BuildVersion
    {
        get
        {
            // Get the executing assembly
            var assembly = Assembly.GetExecutingAssembly();

            // Retrieve the file version attribute
            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

            // If you also want the assembly version
            var assemblyVersion = assembly.GetName().Version?.ToString();

            // Combine and return the version information
            return $"Assembly Version: {assemblyVersion}, File Version: {fileVersion}";
        }
    }

    // ADDED: Secret cache
    private static readonly ConcurrentDictionary<string, string> _connCache = new();
    private static volatile bool _secretLoaded = false;
    private static readonly object _secretLock = new();

    private static readonly ConcurrentDictionary<string, (string Arn, string Alias, string ClientCode, string SystemName)> _kmsCache = new(StringComparer.OrdinalIgnoreCase);

    public Function()
    {
        WriteLog("BuildVersion", $"{BuildVersion}");

        if (VerboseLoggging)
        {
            //logging-in-the-aws-net-sdk
            //https://stackoverflow.com/questions/60435957/how-do-i-turn-on-verbose-logging-in-the-aws-net-sdk
            AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Console;
            WriteLog("VerboseLoggging", "SDK Debug logging is enabled");
        }
        else
            WriteLog("VerboseLoggging", "SDK Debug logging is not enabled");

        if (EnableXrayTrace)
        {
            //https://docs.aws.amazon.com/lambda/latest/dg/csharp-tracing.html
            AWSSDKHandler.RegisterXRayForAllServices();
            WriteLog("EnableXrayTrace", "Intrumenting Lambda with x-ray is enabled");
        }
        else
            WriteLog("EnableXrayTrace", "Intrumenting Lambda with x-ray is not enabled");

        _s3Client = new AmazonS3Client();
        _sqsClient = new AmazonSQSClient();
        _secretsManagerClient = new AmazonSecretsManagerClient(new AmazonSecretsManagerConfig()
        {
            // Avoid 'Signature expired' exceptions by resigning the retried requests.
            ResignRetries = true,
            MaxErrorRetry = 3,
            Timeout = TimeSpan.FromSeconds(SecretsManagerTimeOutInSeconds)
        });
        _dynamoDBClient = new AmazonDynamoDBClient();

        WriteLog("Function-Init", "IAmazonS3, IAmazonSecretsManager, AmazonSQSClient and AmazonDynamoDBClient initialization completed - Function()");
    }

    public Function(IAmazonS3 _s3Client, IAmazonS3 _sqsClientCA, AmazonSQSClient _sqsClient, IAmazonSecretsManager _secretsManagerClient, AmazonDynamoDBClient _dynamoDBClient)
    {
        this._s3Client = _s3Client;
        this._s3ClientCanada = _sqsClientCA;
        this._secretsManagerClient = _secretsManagerClient;
        this._dynamoDBClient = _dynamoDBClient;
        this._sqsClient = _sqsClient;

        WriteLog("Function-Init", "IAmazonS3, IAmazonSecretsManager, AmazonSQSClient and AmazonDynamoDBClient initialization completed - Function(parameter)");
    }

    /// <summary>
    /// Lambda function handler for processing SQS messages to copy files from Azure to AWS S3
    /// </summary>
    /// <param name="evnt">SQS event containing messages</param>
    /// <returns>Task</returns>
    public async Task FunctionHandler(SQSEvent evnt)
    {
        foreach (var message in evnt.Records)
        {
            await ProcessMessage(message);
        }
    }

    /// <summary>
    /// Processes an SQS message, performing a series of operations including deserialization,  database lookups, Azure
    /// Blob storage interactions, and S3 uploads.
    /// </summary>
    /// <remarks>This method performs the following high-level operations: <list type="bullet">
    /// <item><description>Deserializes the SQS message body into a <see cref="SqsMessage"/>
    /// object.</description></item> <item><description>Ensures necessary secret connections are
    /// loaded.</description></item> <item><description>Resolves the appropriate database connection string based on the
    /// country code.</description></item> <item><description>Retrieves call details from the database and validates the
    /// data.</description></item> <item><description>Interacts with Azure Blob storage to retrieve and process audio
    /// files.</description></item> <item><description>Uploads the processed audio file to AWS S3 and verifies the
    /// upload.</description></item> <item><description>Updates the database with the new S3 file
    /// details.</description></item> </list> If any step fails, the method logs the error and attempts to finalize the
    /// request appropriately.</remarks>
    /// <param name="message">The SQS message to process. The message body is expected to contain  a JSON payload that can be deserialized
    /// into an <see cref="SqsMessage"/> object.</param>
    /// <returns></returns>
    private async Task ProcessMessage(SQSEvent.SQSMessage message)
    {
        string sMsg = string.Empty;
        string sMsgFormat = "ProcessMessage: {0}";

        try
        {
            string sJson = JsonConvert.SerializeObject(message);
            WriteLog("SQS.Message.Received", string.Format(sMsgFormat, $"Processing message: {sJson}, MessageId: {message.MessageId}"));

            var sqsMessage = JsonConvert.DeserializeObject<SqsMessage>(message.Body);
            if (sqsMessage == null)
            {
                sMsg = "Failed to deserialize SQS message";
                WriteLog("SQS.Message.Failed", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return;
            }

            RequestId = sqsMessage.RequestId ?? string.Empty;

            await EnsureSecretConnectionsLoadedAsync();

            var country = string.IsNullOrWhiteSpace(sqsMessage.CountryCode) ? "US" : sqsMessage.CountryCode.Trim().ToUpperInvariant();

            var connectionString = ResolveConnectionString(country, writer: false);
            if (string.IsNullOrEmpty(connectionString))
            {
                sMsg = string.Format(sMsgFormat, "Database connection string not configured");
                WriteLog("ConnectionString.Not.Configured", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return;
            }

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using var dbContext = new ApplicationDbContext(optionsBuilder.Options);

            var (callDetailsInfo, exception) = await GetCallDetailsInfoAsync(
                dbContext,
                sqsMessage.CallDetailID,
                sqsMessage.AudioFile);

            if (callDetailsInfo == null || exception is not null)
            {
                sMsg = string.Format(sMsgFormat, $"No joined call detail found CallDetailID={sqsMessage.CallDetailID} AudioFile={sqsMessage.AudioFile}");
                WriteLog("CallDetailId.No.Match", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                await MoveAndFinalizeRequestAsync(
                    sqsMessage,
                    exception);

                return;
            }

            WriteLog("CallDetails.Info", string.Format(sMsgFormat,
                $"Joined Call Detail -> CallDetailID={callDetailsInfo.CallDetailID} ProgramCode={callDetailsInfo.ProgramCode ?? "NULL"} " +
                $"AudioFile={callDetailsInfo.AudioFile ?? "NULL"} IsAzureCloudAudio={callDetailsInfo.IsAzureCloudAudio} Location={callDetailsInfo.AudioFileLocation ?? "NULL"}"));

            var azureStorage = await GetDefaultAzureStorageAsync(dbContext);
            if (azureStorage == null)
            {
                sMsg = string.Format(sMsgFormat, "Default Azure storage configuration not found (storagetype='azure' AND defaultstorage=true).");
                WriteLog("Azure.Storage.NotFound", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return;
            }

            var (storageConfig, exceptionstorage) = ParseStorageConfig(azureStorage);
            if (storageConfig?.MSAzureBlob == null || exceptionstorage is not null)
            {
                sMsg = string.Format(sMsgFormat, "Failed to parse Azure Blob configuration from storage JSON.");
                WriteLog("Azure.Blog.Config.NotFound", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                await MoveAndFinalizeRequestAsync(
                    sqsMessage,
                    exceptionstorage);

                return;
            }

            WriteLog("Azure.Details", string.Format(sMsgFormat,
                $"Azure Storage Config -> StorageID={azureStorage.StorageID} Endpoint={storageConfig.MSAzureBlob.EndPoint ?? "NULL"} " +
                $"Bucket(Computed)={azureStorage.BucketName ?? "NULL"}"));

            #region DownloadByteArrayAsync
            //if(System.Diagnostics.Debugger.IsAttached)
            //{
            //    File.Delete(sFilePath);
            //    byte[] byArray = await fileVault.DownloadByteArrayAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, 60);
            //    await File.WriteAllBytesAsync(sFilePath, byArray);
            //}
            #endregion

            #region DownloadAndDecryptIfNeededAsync
            //if (System.Diagnostics.Debugger.IsAttached)
            //{
            //    File.Delete(sFilePath);
            //    Stream stream = await fileVault.DownloadDecryptedStreamAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, 60);
            //    //write the stream to file
            //    using var fileStream = File.Create(sFilePath);
            //    stream.Position = 0;
            //    await stream.CopyToAsync(fileStream);
            //}
            #endregion

            //Get customer managed encryption id from dynamodb based on the programcode 
            var (kmsArn, kmsAlias, clientcode, systemname, exceptionkmskey) = await GetKmsKeyForProgramAsync(callDetailsInfo.ProgramCode);
            if (string.IsNullOrWhiteSpace(kmsArn) || exceptionkmskey is not null)
            {
                sMsg = string.Format(sMsgFormat, $"Failed to retrieve KMS key for program: {callDetailsInfo.ProgramCode} from dynamodb table: {TableClientCountryKMSMap}.");
                WriteLog("KMS.Program.NoMap", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                await MoveAndFinalizeRequestAsync(
                    sqsMessage,
                    exceptionkmskey);

                return;
            }

            var (AzureStream, AzureException) = await GetAzureStreamAsync(callDetailsInfo, storageConfig);
            if (AzureException != null)
            {
                sMsg = string.Format(sMsgFormat, $"Failed to get Azure stream for CallDetailID={callDetailsInfo.CallDetailID} AudioFile={callDetailsInfo.AudioFile}: {AzureException.Message}");
                WriteLog("Azure.Stream.Exception", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                await MoveAndFinalizeRequestAsync(
                    sqsMessage,
                    AzureException);

                return;
            }

            var (Bucket, Key, newaudiofilelocation, AzureMd5, S3Md5, S3SizeBytes, ExceptionUpload) = await UploadToS3AndVerifyAsync(
                AzureStream!,
                sqsMessage.CountryCode!,
                callDetailsInfo.AudioFile!,
                kmsArn ?? string.Empty,
                clientcode!,
                systemname!,
                callDetailsInfo.CallDate);

            if (ExceptionUpload == null)
            {
                WriteLog("AWS.S3.Upload.Success", string.Format(sMsgFormat, $"Upload succeeded. S3 Key={Key} Size={S3SizeBytes} MD5={S3Md5}"));

                #region "EF"
                //connectionString = ResolveConnectionString(country, writer: true);
                //if (string.IsNullOrEmpty(connectionString))
                //{
                //    string smsg = $"Database connection string not configured correctly for countrycode:{country} Writer";
                //    WriteLog(string.Format(sMsgFormat, "Exception"), new Exception(smsg));
                //    await MoveAndFinalizeRequestAsync(
                //    sqsMessage,
                //    new Exception(smsg));

                //    return;
                //}

                //optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                //optionsBuilder.UseNpgsql(connectionString);

                //using var dbContextWriter = new ApplicationDbContext(optionsBuilder.Options);

                //// Update call_recording_details with new S3 key (location), MD5 and size
                //var (updated, UpdatException) = await UpdateRecordingDetailsAsync(
                //    dbContextWriter,
                //    callDetailsInfo.CallDetailID,
                //    callDetailsInfo.AudioFile!,
                //    newaudiofilelocation,
                //    S3Md5,
                //    S3SizeBytes);

                //if (!updated)
                //{
                //    sMsg = string.Format(sMsgFormat, $"Recording details update failed for CallDetailID={callDetailsInfo.CallDetailID}");
                //    WriteLog(string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                //    await MoveAndFinalizeRequestAsync(
                //        sqsMessage,
                //        UpdatException);
                //}
                //else
                //{
                //    await MoveAndFinalizeRequestAsync(
                //        sqsMessage,
                //        null);
                //    WriteLog(string.Format(sMsgFormat, $"Recording details updated successfully in CallRecordingDetails table for CallDetailID={callDetailsInfo.CallDetailID}"));
                //}
                #endregion

                var (updated, UpdatException) = await UpdateRecordingDetailsAsync(new UpdateCallRecordingDetails()
                {
                    CallDetailID = callDetailsInfo.CallDetailID,
                    AudioFile = callDetailsInfo.AudioFile!,
                    AudioFileLocation = newaudiofilelocation,
                    S3Md5 = S3Md5,
                    S3SizeBytes = S3SizeBytes,
                    Status = StatusCode.SUCCESS.ToString(),
                }, country);

                if (!updated)
                {
                    sMsg = string.Format(sMsgFormat, $"Recording details update failed for CallDetailID={callDetailsInfo.CallDetailID}");
                    WriteLog("CallRecordingDetails.Update.Failure", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                    await MoveAndFinalizeRequestAsync(
                        sqsMessage,
                        UpdatException);
                }
                else
                {
                    //await MoveAndFinalizeRequestAsync(
                    //    sqsMessage,
                    //    null);
                    WriteLog("CallRecordingDetails.Update.Success", string.Format(sMsgFormat, $"Recording details updated successfully in CallRecordingDetails table for CallDetailID={callDetailsInfo.CallDetailID}"));

                    //Performing Azure Delete
                    var (AzureFileDeleted, DelAzureException) = await DeleteAzueBlobAsync(callDetailsInfo, storageConfig);

                    if (AzureFileDeleted)
                    {
                        WriteLog("Azure.File.Delete.Success", string.Format(sMsgFormat, $"Azure file deleted sucessful for CallDetailID={callDetailsInfo.CallDetailID}, AudioFile:{callDetailsInfo.AudioFile}, AudioFileLocation: {callDetailsInfo.AudioFileLocation}"));
                    }
                    else
                    {
                        WriteLog("Azure.File.Delete.Failure", string.Format(sMsgFormat, $"Azure file deletion unsuccessful for CallDetailID={callDetailsInfo.CallDetailID}, AudioFile:{callDetailsInfo.AudioFile}, AudioFileLocation: {callDetailsInfo.AudioFileLocation}"), DelAzureException);
                    }
                }
            }
            else
            {
                sMsg = string.Format(sMsgFormat, $"Azure call upload to AWS is failed for Calldetailid: {sqsMessage.CallDetailID}.");
                WriteLog("AWS.S3.Upload.Failure", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                await MoveAndFinalizeRequestAsync(
                    sqsMessage,
                    ExceptionUpload);

                return;
            }
        }
        catch (Exception ex)
        {
            WriteLog("Process.Failure", string.Format(sMsgFormat, $"Error processing message(MessageId): {message.MessageId}"), ex);
        }
    }

    /// <summary>
    /// Asynchronously retrieves a decrypted stream from Azure storage based on the specified call details.
    /// </summary>
    /// <remarks>This method attempts to download and decrypt a file from Azure storage. If an error occurs
    /// during the operation, the exception is captured and returned as part of the tuple. The caller is responsible for
    /// handling the exception and disposing of the stream, if it is not <see langword="null"/>.</remarks>
    /// <param name="callDetailsInfo">The details of the call, including the audio file location and name.</param>
    /// <param name="storageConfig">The Azure storage configuration used to access the file.</param>
    /// <returns>A tuple containing the decrypted stream and an exception, if any. The first item is the <see cref="Stream"/>
    /// representing the decrypted file, or <see langword="null"/> if an error occurs. The second item is an <see
    /// cref="Exception"/> representing the error, or <see langword="null"/> if the operation succeeds.</returns>
    private static async Task<(Stream? AzureStream, Exception? AzureException)> GetAzureStreamAsync(CallDetailInfo callDetailsInfo, StorageAZURE storageConfig)
    {
        try
        {
            FileVault fileVault = new(storageConfig);
            Stream stream = await fileVault.DownloadDecryptedStreamAsync(callDetailsInfo.AudioFileLocation!, callDetailsInfo.AudioFile!, 60);
            return (stream, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>
    /// Deletes an Azure Blob associated with the specified call details.
    /// </summary>
    /// <remarks>This method attempts to delete the specified Azure Blob and returns the result along with any
    /// exception encountered. If the blob cannot be deleted, the method returns <see langword="false"/> and the
    /// exception that caused the failure.</remarks>
    /// <param name="callDetailsInfo">An object containing details about the call, including the location and name of the audio file to delete.</param>
    /// <param name="storageConfig">The Azure storage configuration used to access the blob storage.</param>
    /// <returns>A tuple containing a boolean value indicating whether the blob was successfully deleted and an exception object
    /// if an error occurred during the operation. The exception will be <see langword="null"/> if the operation
    /// succeeds.</returns>
    private static async Task<(bool Deleted, Exception? AzureException)> DeleteAzueBlobAsync(CallDetailInfo callDetailsInfo, StorageAZURE storageConfig)
    {
        try
        {
            FileVault fileVault = new(storageConfig);
            return (await fileVault.DeleteAzueBlobAsync(callDetailsInfo.AudioFileLocation!, callDetailsInfo.AudioFile!, 60), null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    /// <summary>
    /// Updates the details of a call recording in the database, including its location, MD5 hash, and size.
    /// </summary>
    /// <remarks>This method retrieves the call recording details from the database based on the provided
    /// <paramref name="callDetailId"/>  and <paramref name="audioFileName"/>. If a matching record is found, it updates
    /// the recording's location, MD5 hash,  and size, and resets certain fields to their default values. If no matching
    /// record is found, the method logs a warning  and returns a failure result.</remarks>
    /// <param name="db">The database context used to access and update the call recording details.</param>
    /// <param name="callDetailId">The unique identifier of the call detail record to update.</param>
    /// <param name="audioFileName">The name of the audio file associated with the call recording.</param>
    /// <param name="newLocation">The new storage location of the audio file.</param>
    /// <param name="md5">The MD5 hash of the audio file for integrity verification.</param>
    /// <param name="sizeBytes">The size of the audio file in bytes.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A tuple containing a boolean indicating success or failure, and an <see cref="Exception"/> if an error occurred.
    /// The boolean is <see langword="true"/> if the update was successful; otherwise, <see langword="false"/>. The
    /// exception is <see langword="null"/> if the operation succeeded.</returns>
    private static async Task<(bool, Exception?)> UpdateRecordingDetailsAsync(
        ApplicationDbContext db,
        long callDetailId,
        string audioFileName,
        string newLocation,
        string md5,
        long sizeBytes,
        CancellationToken ct = default)
    {
        string sMsg = string.Empty;
        string sMsgFormat = "UpdateRecordingDetailsAsync: {0}";

        try
        {
            var record = await db.TableCallRecordingDetails
                .FirstOrDefaultAsync(r =>
                        r.CallDetailID == callDetailId &&
                        r.AudioFile != null &&
                        r.AudioFile.ToLower() == audioFileName.ToLower() &&
                        r.IsAzureCloudAudio == true,
                    ct);

            if (record == null)
            {
                sMsg = string.Format(sMsgFormat, $"Record not found CallDetailID={callDetailId} AudioFile={audioFileName}");
                WriteLog("CallRecordingDetails.Update.No.Match", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return (false, new Exception(sMsg));
            }

            record.AudioFileLocation = newLocation;
            record.AudioFileMd5Hash = md5;
            record.AudioFileSize = sizeBytes;
            record.AudioStorageID = null; // Reset to default storage
            record.IsEncryptedAudio = null;
            record.IsAzureCloudAudio = false; // Mark as no longer Azure

            await db.SaveChangesAsync(ct);
            WriteLog("CallRecordingDetails.Update.Details", string.Format(sMsgFormat, $"Updated CallDetailID={callDetailId} File={audioFileName} Size={sizeBytes} MD5={md5}"));
            return (true, null);
        }
        catch (Exception ex)
        {
            WriteLog("CallRecordingDetails.Exception", string.Format(sMsgFormat, $"error CallDetailID={callDetailId}", ex));
            return (false, ex);
        }
    }

    /// <summary>
    /// Retrieves the default Azure storage configuration from the database.
    /// </summary>
    /// <remarks>The method filters storage configurations to include only those that are active, marked as default,
    /// and have a storage type of "Azure". If <paramref name="countryId"/> is provided, the results are further filtered to
    /// include only configurations associated with the specified country. The most recently updated or created
    /// configuration is returned.</remarks>
    /// <param name="db">The database context used to query the storage configurations. Cannot be null.</param>
    /// <param name="countryId">An optional country identifier to filter the storage configurations. If specified, only storage configurations
    /// associated with the given country are considered.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="TableStorage"/> object representing the default Azure storage configuration, or <see langword="null"/>
    /// if no matching configuration is found.</returns>
    private static async Task<TableStorage?> GetDefaultAzureStorageAsync(
        ApplicationDbContext db,
        int? countryId = null,
        CancellationToken ct = default)
    {
        var query = db.TableStorage
            .AsNoTracking()
            .Where(s =>
                s.StorageType != null &&
                s.StorageType.ToLower() == "azure" &&
                s.DefaultStorage &&
                s.ActiveInd);

        if (countryId.HasValue)
            query = query.Where(s => s.CountryID == countryId.Value);

        return await query
            .OrderByDescending(s => s.UpdatedDate ?? s.CreatedDate)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Parses the JSON configuration for a storage object and returns a deserialized <see cref="StorageAZURE"/>
    /// instance.
    /// </summary>
    /// <remarks>This method attempts to deserialize the JSON configuration provided in the <paramref
    /// name="storage"/> parameter. If the JSON is invalid or deserialization fails, the method logs the error using the
    /// provided <paramref name="ctx"/> and returns the exception.</remarks>
    /// <param name="storage">The <see cref="TableStorage"/> object containing the JSON configuration to parse.</param>
    /// <returns>A tuple containing the deserialized <see cref="StorageAZURE"/> object if parsing is successful, or <c>null</c> if
    /// the JSON is empty or invalid. The second item in the tuple is an <see cref="Exception"/> if an error occurs
    /// during parsing; otherwise, <c>null</c>.</returns>
    private static (StorageAZURE?, Exception?) ParseStorageConfig(TableStorage storage)
    {
        if (string.IsNullOrWhiteSpace(storage.Json))
            return (null, null);

        try
        {
            JsonSerializerOptions jsonSerializerOptions = new()
            {
                PropertyNameCaseInsensitive = true
            };
            JsonSerializerOptions options = jsonSerializerOptions;
            return (System.Text.Json.JsonSerializer.Deserialize<StorageAZURE>(
                storage.Json,
                options), null);
        }
        catch (Exception ex)
        {
            WriteLog("Storage.Exception", $"ParseStorageConfig failed StorageID={storage.StorageID}", ex);
            return (null, ex);
        }
    }

    /// <summary>
    /// Retrieves detailed information about a specific call, including associated audio file details, based on the
    /// provided call detail ID and optional audio file name.
    /// </summary>
    /// <remarks>If multiple matching records are found, the first record is returned based on a deterministic
    /// ordering by the audio file name. The method logs any exceptions encountered during execution using the provided
    /// Lambda context.</remarks>
    /// <param name="db">The database context used to query call details and recording information.</param>
    /// <param name="callDetailId">The unique identifier of the call detail to retrieve.</param>
    /// <param name="audioFileName">An optional parameter specifying the name of the audio file to filter the results. If provided, the query will
    /// match the audio file name case-insensitively.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A tuple containing the following: <list type="bullet"> <item> <description> <see cref="CallDetailInfo"/>: The
    /// detailed information about the call, or <see langword="null"/> if no matching record is found. </description>
    /// </item> <item> <description> <see cref="Exception"/>: An exception instance if an error occurs during the
    /// operation, or <see langword="null"/> if the operation completes successfully. </description> </item> </list></returns>
    private static async Task<(CallDetailInfo?, Exception?)> GetCallDetailsInfoAsync(
        ApplicationDbContext db,
        long callDetailId,
        string? audioFileName,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedAudio = audioFileName?.Trim();
            // Base query (join)
            var query =
                from cd in db.TableCallDetails.AsNoTracking()
                join cr in db.TableCallRecordingDetails.AsNoTracking()
                    on cd.CallDetailID equals cr.CallDetailID
                where cd.CallDetailID == callDetailId
                select new CallDetailInfo
                {
                    CallDate = cd.CallDate,
                    CallDetailID = cd.CallDetailID,
                    ProgramCode = cd.ProgramCode,
                    AudioFile = cr.AudioFile,
                    AudioFileLocation = cr.AudioFileLocation,
                    IsAzureCloudAudio = cr.IsAzureCloudAudio
                };

            if (!string.IsNullOrEmpty(normalizedAudio))
            {
                var lowered = normalizedAudio.ToLower();
                query = query.Where(r => r.AudioFile != null && r.AudioFile.ToLower() == lowered);
            }

            // If multiple rows (rare), take first deterministic (order by audio file)
            var result = await query
                .OrderBy(r => r.AudioFile) // deterministic ordering
                .FirstOrDefaultAsync(ct);

            return (result, null);
        }
        catch (Exception ex)
        {
            WriteLog("CallDetails.Exception", $"GetCallDetailsInfoAsync: failed CallDetailID={callDetailId}", ex);
            return (null, ex);
        }
    }

    /// <summary>
    /// Ensures that secret connection strings are loaded from AWS Secrets Manager and cached for use.
    /// </summary>
    /// <remarks>This method retrieves a secret from AWS Secrets Manager, parses it as JSON, and extracts
    /// specific  connection strings to cache them for later use. The secret name is read from the "SECRET_ID" 
    /// environment variable. If the secret is not configured, missing, or empty, the method logs a message  and exits
    /// without caching any connections.  The method uses a double-checked locking mechanism to ensure that the secret
    /// is loaded only once  per application lifecycle, even in multi-threaded scenarios. If the secret is already
    /// loaded, the  method returns immediately.  Exceptions related to AWS Secrets Manager (e.g., resource not found,
    /// throttling, or permissions  issues) are logged, and the method continues execution without throwing. Unexpected
    /// errors during  parsing or network operations are also logged.</remarks>
    /// <returns></returns>
    private async Task EnsureSecretConnectionsLoadedAsync()
    {
        string sMsg = string.Empty;
        string sMsgFormat = "EnsureSecretConnectionsLoadedAsync: {0}";

        // Fast path: already loaded for this Lambda container lifetime.
        if (_secretLoaded) return;

        // Double-checked lock: guarantee only one thread loads the secret.
        lock (_secretLock)
        {
            if (_secretLoaded) return;
            _secretLoaded = true; // Mark now to prevent duplicate load attempts even if an error occurs.
        }

        // If not configured, log to console and abort (fallback: no DB access later).
        if (string.IsNullOrWhiteSpace(secretId))
        {
            WriteLog("Secret.Empty", string.Format(sMsgFormat, "Exception"), new Exception("SECRET_ID not set"));
            _secretLoaded = false;
            return;
        }

        // If the Secrets Manager client was not constructed (should not normally happen) abort.
        if (_secretsManagerClient == null)
        {
            WriteLog("Secret.Client.Not.Init", string.Format(sMsgFormat, "Exception"), new Exception("Secrets Manager client not initialized."));
            _secretLoaded = false;
            return;
        }

        try
        {
            // Fetch secret value (string expected JSON).
            var resp = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretId
            });

            // Empty secret -> nothing to cache.
            if (string.IsNullOrWhiteSpace(resp.SecretString))
            {
                sMsg = string.Format(sMsgFormat, $"Secret '{secretId}' empty.");
                WriteLog("Secret.Empty", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));

                _secretLoaded = false;
                return;
            }

            // Parse JSON once; extract each connection string if present.
            using var jsonDoc = JsonDocument.Parse(resp.SecretString);
            var root = jsonDoc.RootElement;

            // Each call safely ignores missing keys.
            LoadConn(root, "ConnectionStrings_USReaderConnection", "USReaderConnection");
            LoadConn(root, "ConnectionStrings_USWriterConnection", "USWriterConnection");
            LoadConn(root, "ConnectionStrings_CAReaderConnection", "CAReaderConnection");
            LoadConn(root, "ConnectionStrings_CAWriterConnection", "CAWriterConnection");

            WriteLog("Secret.Success", string.Format(sMsgFormat, $"Secret '{secretId}' loaded with {_connCache.Count} connection entries."));
        }
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException e)
        {
            // Secret does not exist (environment/config issue).
            WriteLog("Secret.ResourceNotFound", string.Format(sMsgFormat, $"Secret '{secretId}' not found."), e);
            _secretLoaded = false;
        }
        catch (AmazonSecretsManagerException ax)
        {
            // AWS service-side errors (throttling, permissions, etc).
            WriteLog("Secret.ManagerException", string.Format(sMsgFormat, $"Secrets Manager error: {ax.Message}"), ax);
            _secretLoaded = false;
        }
        catch (Exception ex)
        {
            // Any unexpected parsing/network/runtime issue.
            WriteLog("Secret.Exception", string.Format(sMsgFormat, $"Unexpected secret load error: {ex.Message}"), ex);
            _secretLoaded = false;
        }

        // Local helper: conditionally adds a connection string to cache if found and non-empty.
        static void LoadConn(JsonElement root, string jsonKey, string cacheKey)
        {
            if (root.TryGetProperty(jsonKey, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var cs = val.GetString();
                if (!string.IsNullOrWhiteSpace(cs))
                    _connCache[cacheKey] = cs!;
            }
        }
    }

    /// <summary>
    /// Resolves the connection string for the specified country and role (reader or writer).
    /// </summary>
    /// <remarks>The method trims and converts the <paramref name="country"/> parameter to uppercase before
    /// resolving the connection string. Connection strings are retrieved from an internal cache based on the
    /// combination of the country code and role.</remarks>
    /// <param name="country">The country code used to determine the connection string. If null or whitespace, defaults to "US".</param>
    /// <param name="writer">A value indicating whether the connection string is for a writer role.  <see langword="true"/> for writer;
    /// otherwise, <see langword="false"/> for reader.</param>
    /// <returns>The resolved connection string if found; otherwise, <see langword="null"/>. If the connection string for the
    /// specified country is not found, the method attempts to fall back to the "US" connection string.</returns>
    private static string? ResolveConnectionString(string country, bool writer)
    {
        var c = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();
        var role = writer ? "Writer" : "Reader";
        var key = $"{c}{role}Connection";
        if (_connCache.TryGetValue(key, out var cs))
            return cs;

        // Fallback to US if missing
        if (!c.Equals("US", StringComparison.OrdinalIgnoreCase) &&
            _connCache.TryGetValue($"US{role}Connection", out var csUs))
            return csUs;

        return null;
    }

    /// <summary>
    /// Computes the MD5 hash of the data from the specified stream and returns it as a lowercase hexadecimal string.
    /// </summary>
    /// <param name="stream">The input stream containing the data to hash. The stream must be readable and positioned at the start of the
    /// data to hash.</param>
    /// <returns>A lowercase hexadecimal string representing the MD5 hash of the data in the stream.</returns>
    private static string CalculateMD5(Stream stream)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Writes a log message to the console in JSON format.
    /// </summary>
    /// <remarks>The log message is serialized into a JSON object containing the message, exception details
    /// (if provided),  and a success flag indicating whether an exception was included. The JSON object is then written
    /// to the console.</remarks>
    /// <param name="message">The log message to be written. Cannot be null or empty.</param>
    /// <param name="ex">An optional exception to include in the log. If null, the log entry is marked as successful.</param>
    private static void WriteLog(string key, string message, Exception? ex = null)
    {
        string sJsonMsg = JsonConvert.SerializeObject(new Logging()
        {
            Key = key,
            RequestId = RequestId,
            Message = message.Replace(Environment.NewLine, "\r"),
            Exception = ex,
            IsSuccess = ex == null
        });

        Console.WriteLine(sJsonMsg);
    }

    /// <summary>
    /// Retrieves the AWS KMS key details associated with the specified program code.
    /// </summary>
    /// <remarks>This method queries a DynamoDB table to retrieve the KMS key mapping for the specified
    /// program code.  If the mapping is found, the result is cached for subsequent calls. If the mapping is not found
    /// or an  error occurs, the method returns an exception in the tuple. <para> The DynamoDB table name must be
    /// configured in the environment variable <c>TableClientCountryKMSMap</c>,  and the DynamoDB client must be
    /// initialized before calling this method. </para> <para> The method performs a defensive query with a limit of 5
    /// items, although only one item is expected  for a given program code. If the KMS key ARN is missing in the
    /// retrieved item, the method logs an  error and returns an exception. </para></remarks>
    /// <param name="programCode">The program code used to query the KMS key mapping. Cannot be null, empty, or whitespace.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation. Optional.</param>
    /// <returns>A tuple containing the following elements: <list type="bullet"> <item><description>The ARN of the KMS key, or
    /// <see langword="null"/> if not found.</description></item> <item><description>The alias of the KMS key, or <see
    /// langword="null"/> if not found.</description></item> <item><description>The client code associated with the
    /// program, or <see langword="null"/> if not found.</description></item> <item><description>The system name
    /// associated with the program, or <see langword="null"/> if not found.</description></item> <item><description>An
    /// exception if an error occurred, or <see langword="null"/> if the operation was successful.</description></item>
    /// </list></returns>
    private async Task<(string? Arn, string? Alias, string? ClientCode, string? SystemName, Exception? exception)> GetKmsKeyForProgramAsync(
        string? programCode,
        CancellationToken ct = default)
    {
        string sMsg = string.Empty;
        string sMsgFormat = "GetKmsKeyForProgramAsync: {0}";
        if (string.IsNullOrWhiteSpace(programCode))
        {
            sMsg = string.Format(sMsgFormat, $"Programcode is empty");
            return (null, null, null, null, new Exception(sMsg));
        }

        if (string.IsNullOrWhiteSpace(TableClientCountryKMSMap))
        {
            sMsg = string.Format(sMsgFormat, $"DynamoDB table name (TableClientCountryKMSMap) not configured in environment variables.");
            WriteLog("DynamoDB.Table.Missing", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
            return (null, null, null, null, new Exception(sMsg));
        }

        if (_dynamoDBClient == null)
        {
            sMsg = string.Format(sMsgFormat, $"DynamoDB client not initialized.");
            WriteLog("DynamoDB.Client.NotInit", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
            return (null, null, null, null, new Exception(sMsg));
        }

        var cacheKey = programCode;
        if (_kmsCache.TryGetValue(cacheKey, out var cached))
            return (cached.Arn, cached.Alias, cached.ClientCode, cached.SystemName, null);

        try
        {
            // Build QueryRequest (ProgramCode is the partition key)
            var queryReq = new QueryRequest
            {
                TableName = TableClientCountryKMSMap,
                KeyConditionExpression = $"{Const_programcode} = :pc",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pc"] = new AttributeValue { S = programCode }
                },
                Limit = 5,                 // defensive (expecting 1 normally)
                ConsistentRead = false
            };

            var queryResp = await _dynamoDBClient.QueryAsync(queryReq, ct);
            var item = queryResp.Items.FirstOrDefault();
            if (item == null)
            {
                sMsg = string.Format(sMsgFormat, $"No mapping for programcode: {programCode} found in dynamodb table: {TableClientCountryKMSMap}.");
                WriteLog("DynamoDB.KMS.Program.NoMap", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return (null, null, null, null, new Exception(sMsg));
            }

            item.TryGetValue(Const_arn, out var avArn);
            item.TryGetValue(Const_alias, out var avAlias);
            item.TryGetValue(Const_clientcode, out var avClientCode);
            item.TryGetValue(Const_systemname, out var avSystemName);

            var arn = avArn?.S;
            var alias = avAlias?.S;
            var clientCode = avClientCode?.S;
            var systemName = avSystemName?.S;

            if (!string.IsNullOrWhiteSpace(arn))
            {
                //cache arn, alias and clientcode
                _kmsCache[cacheKey] = (arn!, alias!, clientCode!, systemName!);
                return (arn, alias, clientCode, systemName, null);
            }
            else
            {
                sMsg = string.Format(sMsgFormat, $"Mapping for programcode: {programCode} missing KmsKeyArn attribute in dynamodb table: {TableClientCountryKMSMap}.");
                WriteLog("DynamoDB.KMS.Program.NoMap", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
                return (null, null, null, null, new Exception(sMsg));
            }
        }
        catch (Exception ex)
        {
            WriteLog("DynamoDB.KMS.Exception", string.Format(sMsgFormat, $"Query failed ProgramCode={programCode}"), ex);
            return (null, null, null, null, ex);
        }
    }

    /// <summary>
    /// Uploads an Azure audio stream to S3 using the country-specific bucket and optional KMS CMK,
    /// validates MD5 integrity by re-downloading the object, and returns sizes/checksums.
    /// </summary>
    /// <param name="azureStream">Stream already downloaded from Azure (position will be reset).</param>
    /// <param name="countryCode">Country code (US / CA) to pick the bucket.</param>
    /// <param name="fileName">Target file name (key tail). Will be combined with optional prefix.</param>
    /// <param name="kmsArn">KMS Key ARN for server-side encryption.</param>
    /// <param name="clientcode">clientcode is used to frame the key</param>
    private async Task<(string? Bucket,
                        string? Key,
                        string newaudiofilelocation,
                        string AzureMd5,
                        string S3Md5,
                        long S3SizeBytes,
                        Exception? exception)> UploadToS3AndVerifyAsync(
        Stream azureStream,
        string countryCode,
        string fileName,
        string kmsArn,
        string clientcode,
        string systemname,
        DateTime? CallDate,
        CancellationToken ct = default)
    {
        string sMsg = string.Empty;
        string sMsgFormat = "UploadToS3AndVerifyAsync: {0}";

        if (_s3Client == null)
        {
            sMsg = string.Format(sMsgFormat, "S3 client not initialized");
            WriteLog("S3.Client.NotInit", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
            return (null, null, string.Empty, string.Empty, string.Empty, 0, new Exception(sMsg));
        }

        if (azureStream == null || !azureStream.CanRead)
        {
            sMsg = string.Format(sMsgFormat, "Azure stream invalid");
            WriteLog("Azure.Stream.Invalid", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
            return (null, null, string.Empty, string.Empty, string.Empty, 0, new Exception(sMsg));
        }

        var bucket = countryCode?.ToUpperInvariant() switch
        {
            "CA" => CAS3BucketName,
            _ => USS3BucketName // default US
        };

        if (string.IsNullOrWhiteSpace(bucket))
        {
            sMsg = string.Format(sMsgFormat, "Resolved bucket name empty (check env USS3BucketName / CAS3BucketName)");
            WriteLog("S3.Bucket.Empty", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
            return (bucket, null, string.Empty, string.Empty, string.Empty, 0, new Exception(sMsg));
        }

        DateTime dateTime = CallDate ?? DateTime.UtcNow;
        var key = $"callrecordings/{clientcode}/{dateTime:yyyy}/{dateTime:MM}/{dateTime:dd}/{dateTime:HH}/{dateTime:mm}/{fileName}";
        var newaudiofilelocation = $"{bucket}/callrecordings/{clientcode}/{dateTime:yyyy}/{dateTime:MM}/{dateTime:dd}/{dateTime:HH}/{dateTime:mm}";

        if (string.IsNullOrWhiteSpace(key))
        {
            sMsg = string.Format(sMsgFormat, "unable to frame the key (s3 prefix)");
            WriteLog("S3.Key.Empty", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
            return (bucket, null, newaudiofilelocation, string.Empty, string.Empty, 0, new Exception(sMsg));
        }

        // ----- FIX: Ensure stream positioned at start, compute MD5 safely (base64 for S3, hex for logging) -----
        var (azureMd5Hex, azureMd5Base64) = ComputeContentMd5ForPut(azureStream);

        try
        {
            // Reset again to guarantee PutObject reads full content
            if (azureStream.CanSeek) azureStream.Position = 0;

            var putReq = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = azureStream,
                AutoCloseStream = false,
                ContentType = "audio/wav",
                MD5Digest = azureMd5Base64,
                StorageClass = S3StorageClass.IntelligentTiering,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS,
                ServerSideEncryptionKeyManagementServiceKeyId = kmsArn,
            };

            WriteLog("S3.Info", string.Format(sMsgFormat, $"Uploading to S3 Bucket={bucket} Key={key} KMS={(string.IsNullOrEmpty(kmsArn) ? "None" : kmsArn)} MD5={azureMd5Hex}"));

            var putResp = await GetS3Client(systemname).PutObjectAsync(putReq, ct);
            if (putResp.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                sMsg = string.Format(sMsgFormat, $"PutObject non-OK: {putResp.HttpStatusCode}");
                WriteLog("S3.PutObject.Fail", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
                return (bucket, key, newaudiofilelocation, azureMd5Hex, "", 0, new Exception(sMsg));
            }

            // Single-part uploads: ETag (quotes removed) usually equals hex MD5. Still do full read-verify.
            using var getResp = await GetS3Client(systemname).GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucket,
                Key = key
            }, ct);

            await using var s3Mem = new MemoryStream();
            await getResp.ResponseStream.CopyToAsync(s3Mem, ct);
            s3Mem.Position = 0;
            var s3Md5Hex = CalculateMD5(s3Mem);
            var s3Size = getResp.ContentLength;

            var match = string.Equals(azureMd5Hex, s3Md5Hex, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                sMsg = string.Format(sMsgFormat, $"MD5 mismatch Azure={azureMd5Hex} S3={s3Md5Hex} Bucket={bucket} Key={key}");
                WriteLog("S3.Md5.Fail", string.Format(sMsgFormat, "Exception", new Exception(sMsg)));
                return (bucket, key, newaudiofilelocation, azureMd5Hex, s3Md5Hex, s3Size, new Exception(sMsg));
            }

            WriteLog("S3.Upload.Details", string.Format(sMsgFormat, $"Upload verified MD5={azureMd5Hex} Size={s3Size} Bucket={bucket} Key={key}"));
            return (bucket, key, newaudiofilelocation, azureMd5Hex, s3Md5Hex, s3Size, null);
        }
        catch (Exception ex)
        {
            WriteLog("S3.Upload.Fail", string.Format(sMsgFormat, "Exception"), ex);
            return (bucket, key, newaudiofilelocation, azureMd5Hex, "", 0, ex);
        }
        finally
        {
            if (azureStream.CanSeek)
                azureStream.Position = 0;
        }
    }

    /// <summary>
    /// Computes the MD5 hash of the content in the provided stream and returns the hash in both hexadecimal and
    /// Base64-encoded formats.
    /// </summary>
    /// <remarks>If the provided stream is seekable, its position will be reset to its original value after
    /// the hash computation. For non-seekable streams, the stream will be consumed and replaced with a buffered memory
    /// stream if further usage is required.</remarks>
    /// <param name="stream">The input <see cref="Stream"/> containing the content to hash. The stream must be readable. If the stream is
    /// non-seekable, it will be fully buffered into memory, and the original stream will be disposed.</param>
    /// <returns>A tuple containing the MD5 hash of the content in two formats: <list type="bullet"> <item> <term>Hex</term>
    /// <description>The MD5 hash as a lowercase hexadecimal string.</description> </item> <item> <term>Base64</term>
    /// <description>The MD5 hash as a Base64-encoded string.</description> </item> </list></returns>
    private static (string Hex, string Base64) ComputeContentMd5ForPut(Stream stream)
    {
        if (!stream.CanSeek)
        {
            // Buffer non-seekable stream
            using var md5 = MD5.Create();
            using var mem = new MemoryStream();
            stream.CopyTo(mem);
            var data = mem.ToArray();
            var hash = md5.ComputeHash(data);
            stream.Dispose(); // original non-seekable consumed
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            var b64 = Convert.ToBase64String(hash);
            // Replace stream with new memory stream if caller continues to use it (optional)
            // (Callers should supply seekable stream ideally.)
            return (hex, b64);
        }

        var originalPos = stream.Position;
        stream.Position = 0;
        using (var md5Seek = MD5.Create())
        {
            var hash = md5Seek.ComputeHash(stream);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            var b64 = Convert.ToBase64String(hash);
            stream.Position = originalPos; // caller expects original position (we reset to 0 later before upload)
            return (hex, b64);
        }
    }

    /// <summary>
    /// This function will return existing S3 client if the region is same as requested, otherwise it will create new S3 client for requested region
    /// as this lambda running under us-east-1, s3 client created in us-east-1 region by default.
    /// </summary>
    /// <param name="sSystemName"></param>
    /// <returns></returns>
    private IAmazonS3 GetS3Client(string sSystemName)
    {
        if (_s3Client!.Config.RegionEndpoint.Equals(RegionEndpoint.GetBySystemName(sSystemName)))
        {
            WriteLog($"S3.Client.{_s3Client.Config.RegionEndpoint.SystemName}", $"S3 client already created with region: {_s3Client.Config.RegionEndpoint.SystemName}. Instantiation skipped.");
            return _s3Client;
        }

        if (_s3ClientCanada == null)
        {
            _s3ClientCanada = new AmazonS3Client(new AmazonS3Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(sSystemName)
            });
            WriteLog($"S3.Client.{_s3ClientCanada.Config.RegionEndpoint.SystemName}", $"S3 client created with region: {_s3ClientCanada.Config.RegionEndpoint.SystemName}. Instantiation completed.");
        }

        return _s3ClientCanada;
    }

    /// <summary>
    /// Moves the specified request from the primary table to the audit table and finalizes its status.
    /// </summary>
    /// <remarks>This method performs the following operations: <list type="bullet"> <item><description>Fetches the
    /// request from the primary table if it is still in progress.</description></item> <item><description>Copies the
    /// request to the audit table to preserve its original state.</description></item> <item><description>Removes the
    /// request from the primary table to complete the "move" operation.</description></item> <item><description>Inserts a
    /// new record into the audit table with the final status, which is determined based on whether an exception was
    /// provided.</description></item> </list> If the database connection string for the specified country code is not
    /// configured, the method logs an error and returns <see langword="false"/>. If an error occurs during the database
    /// transaction, the transaction is rolled back, the error is logged, and the method returns <see
    /// langword="false"/>.</remarks>
    /// <param name="sqsMessage">The message containing details about the request, including the call detail ID and audio file name.</param>
    /// <param name="exception">An optional exception that, if provided, will be recorded in the audit table as part of the final status.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the operation
    /// completes successfully; otherwise, <see langword="false"/>.</returns>
    private static async Task<bool> MoveAndFinalizeRequestAsync(
        SqsMessage sqsMessage,
        Exception? exception,
        CancellationToken ct = default)
    {
        string sMsg = string.Empty;
        string sMsgFormat = "MoveAndFinalizeRequestAsync: {0}";

        string actor = "CopyAzureToAWS.Lambda";
        string countryCode = sqsMessage.CountryCode;
        long callDetailId = sqsMessage.CallDetailID;
        string audioFile = sqsMessage.AudioFile;

        var connectionString = ResolveConnectionString(countryCode, writer: true);
        if (string.IsNullOrEmpty(connectionString))
        {
            sMsg = string.Format(sMsgFormat, $"Database connection string not configured correctly for countrycode:{countryCode} Writer");
            WriteLog("Status_Update_Conn_Empty", string.Format(sMsgFormat, "Exception"), new Exception(sMsg));
            return false;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        using var db = new ApplicationDbContext(optionsBuilder.Options);

        string finalStatus = (exception == null ? StatusCode.SUCCESS.ToString() : StatusCode.ERROR.ToString());
        using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var nowUnspec = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            //var nowUnspec = DateTime.SpecifyKind(GetCurrentEasternTime(), DateTimeKind.Unspecified);

            // Fetch current INPROGRESS row (if still in primary table)
            var source = await db.TableAzureToAWSRequest
                .FirstOrDefaultAsync(r =>
                    r.CallDetailID == callDetailId &&
                    r.AudioFile.ToLower() == audioFile.ToLower(), ct);

            // If present, copy to audit (original state)
            if (source != null)
            {
                var srcCreated = source.CreatedDate;
                if (srcCreated.Kind != DateTimeKind.Unspecified)
                    srcCreated = DateTime.SpecifyKind(
                        srcCreated.Kind == DateTimeKind.Local ? srcCreated.ToUniversalTime() : srcCreated,
                        DateTimeKind.Unspecified);

                var originalAudit = new TableAzureToAWSRequestAudit
                {
                    CallDetailID = source.CallDetailID,
                    AudioFile = source.AudioFile,
                    Status = source.Status,        // should be INPROGRESS
                    ErrorDescription = null,
                    CreatedDate = srcCreated,
                    CreatedBy = source.CreatedBy,
                    UpdatedDate = nowUnspec,
                    UpdatedBy = actor
                };
                await db.TableAzureToAWSRequestAudit.AddAsync(originalAudit, ct);

                // Remove from live table (move semantics)
                db.TableAzureToAWSRequest.Remove(source);
            }
            else
            {
                WriteLog("CallDetailid.Moved", string.Format(sMsgFormat, $"Source row already moved (CallDetailID={callDetailId})."));
            }

            // Insert final status row
            var finalAudit = new TableAzureToAWSRequestAudit
            {
                CallDetailID = callDetailId,
                AudioFile = audioFile,
                Status = finalStatus,
                ErrorDescription = exception?.ToString(),
                CreatedDate = nowUnspec,
                CreatedBy = actor
            };
            await db.TableAzureToAWSRequestAudit.AddAsync(finalAudit, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            WriteLog("Status_Update_Success", string.Format(sMsgFormat, $"Final status '{finalStatus}' recorded CallDetailID={callDetailId}."));
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            WriteLog("Status_Exception", string.Format(sMsgFormat, $"failed CallDetailID={callDetailId}"), ex);
            return false;
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

    /// <summary>
    /// Updates the recording details in the database by invoking a stored procedure.
    /// </summary>
    /// <remarks>This method executes a stored procedure to update recording details in the database. The
    /// operation is performed  asynchronously and requires a valid database connection string, which is resolved based
    /// on the provided  countryCode. If the operation fails, the exception is logged and returned as part of the result
    /// tuple.</remarks>
    /// <param name="row">An object containing the recording details to be updated.</param>
    /// <param name="countryCode">The country code used to resolve the appropriate database connection string.</param>
    /// <returns>A tuple where the first value indicates whether the operation was successful, and the second value is an 
    /// Exception instance if an error occurred; otherwise, null.</returns>
    private async Task<(bool, Exception?)> UpdateRecordingDetailsAsync(UpdateCallRecordingDetails row, string countryCode)
    {
        try
        {
            string[] arr = RECORD_AZURE_TO_AWS_STATUS.Split('|');
            string spname = arr[0];
            string role = arr.Length > 1 ? arr[1] : "Writer";
            var connectionString = ResolveConnectionString(countryCode, role.ToLower().Equals("Writer".ToLower()));

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand
            {
                Connection = conn,
                CommandText = $"CALL {RECORD_AZURE_TO_AWS_STATUS}(:p_json);",
                CommandType = System.Data.CommandType.Text,
                CommandTimeout = CommandTimeout
            };
            cmd.Parameters.AddWithValue("p_json", NpgsqlDbType.Jsonb, JsonConvert.SerializeObject(row));
            await cmd.ExecuteNonQueryAsync();

            return (true, null);
        }
        catch (Exception ex)
        {
            WriteLog("Status.Update.Exception", $"Stored proc failure CallDetailID={row.CallDetailID} Country={countryCode}", ex);
            return (false, ex);
        }
    }
}