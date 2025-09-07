using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using CopyAzureToAWS.Common.Utilities;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using CopyAzureToAWS.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Npgsql.EntityFrameworkCore.PostgreSQL; // Add this using directive at the top of the file
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

//[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CopyAzureToAWS.Lambda;

public class Function
{
    private readonly IAmazonS3? _s3Client;
    private readonly AmazonSQSClient? _sqsClient;
    private readonly IAmazonSecretsManager? _secretsManagerClient;
    private readonly AmazonDynamoDBClient? _dynamoDBClient;

    private readonly string SecretId = Environment.GetEnvironmentVariable("SECRET_ID").ToString();
    private readonly int SecretsManagerTimeOutInSeconds = int.Parse(Environment.GetEnvironmentVariable("SecretsManagerTimeOutInSeconds").ToString());

    private static bool EnableXrayTrace
    {
        get
        {
            if (Environment.GetEnvironmentVariable("EnableXrayTrace") == null)
                return false;
            else
                return bool.Parse(Environment.GetEnvironmentVariable("EnableXrayTrace").ToString());
        }
    }

    private static bool VerboseLoggging
    {
        get
        {
            if (Environment.GetEnvironmentVariable("VerboseLoggging") == null)
                return false;
            else
                return bool.Parse(Environment.GetEnvironmentVariable("VerboseLoggging").ToString());
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

    public Function()
    {
        WriteLog($"{BuildVersion}");

        if (VerboseLoggging)
        {
            //logging-in-the-aws-net-sdk
            //https://stackoverflow.com/questions/60435957/how-do-i-turn-on-verbose-logging-in-the-aws-net-sdk
            AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Console;
            WriteLog("SDK Debug logging is enabled");
        }
        else
            WriteLog("SDK Debug logging is not enabled");

        if (EnableXrayTrace)
        {
            //https://docs.aws.amazon.com/lambda/latest/dg/csharp-tracing.html
            AWSSDKHandler.RegisterXRayForAllServices();
            WriteLog("Intrumenting Lambda with x-ray is enabled");
        }
        else
            WriteLog("Intrumenting Lambda with x-ray is not enabled");


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

        WriteLog("AmazonDynamoDBClient and AmazonSQSClient initialization completed - Function()");
    }

    public Function(IAmazonS3 _s3Client, AmazonSQSClient _sqsClient, IAmazonSecretsManager _secretsManagerClient, AmazonDynamoDBClient _dynamoDBClient)
    {
        this._s3Client = _s3Client;
        this._sqsClient = _sqsClient;
        this._secretsManagerClient = _secretsManagerClient;
        this._dynamoDBClient = _dynamoDBClient;

        WriteLog("AmazonDynamoDBClient and AmazonSQSClient initialization completed - Function(parameter)");
    }

    /// <summary>
    /// Lambda function handler for processing SQS messages to copy files from Azure to AWS S3
    /// </summary>
    /// <param name="evnt">SQS event containing messages</param>
    /// <param name="context">Lambda context</param>
    /// <returns>Task</returns>
    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        foreach (var message in evnt.Records)
        {
            await ProcessMessage(message, context);
        }
    }

    private async Task ProcessMessage(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        try
        {
            context.Logger.LogInformation($"Processing message: {message.MessageId}");

            // Parse the SQS message
            var sqsMessage = JsonConvert.DeserializeObject<SqsMessage>(message.Body);
            if (sqsMessage == null)
            {
                context.Logger.LogError("Failed to deserialize SQS message");
                return;
            }

            // ADDED: Load secrets once (cached)
            await EnsureSecretConnectionsLoadedAsync();

            var country = string.IsNullOrWhiteSpace(sqsMessage.CountryCode) ? "US" : sqsMessage.CountryCode.Trim().ToUpperInvariant();

            // ADDED: Resolve from cache first (Reader role here)
            var connectionString = ResolveConnectionString(country, writer: false);
            if (string.IsNullOrEmpty(connectionString))
            {
                context.Logger.LogError("Database connection string not configured");
                return;
            }

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using var dbContext = new ApplicationDbContext(optionsBuilder.Options);

            // FIX: Use AudioFileName (property in SqsMessage) instead of AudioFile
            var callDetailsInfo = await GetCallDetailsInfoAsync(
                dbContext,
                sqsMessage.CallDetailID,
                sqsMessage.AudioFile,
                context);

            if (callDetailsInfo == null)
            {
                context.Logger.LogWarning($"No joined call detail found CallDetailID={sqsMessage.CallDetailID} AudioFile={sqsMessage.AudioFile}");
                return;
            }

            context.Logger.LogInformation(
                $"Joined Call Detail -> CallDetailID={callDetailsInfo.CallDetailID} ProgramCode={callDetailsInfo.ProgramCode ?? "NULL"} " +
                $"AudioFile={callDetailsInfo.AudioFile ?? "NULL"} IsAzureCloudAudio={callDetailsInfo.IsAzureCloudAudio} Location={callDetailsInfo.AudioFileLocation ?? "NULL"}");

            var azureStorage = await GetDefaultAzureStorageAsync(dbContext);
            if (azureStorage == null)
            {
                context.Logger.LogWarning("Default Azure storage configuration not found (storagetype='azure' AND defaultstorage=true).");
                return;
            }

            var storageConfig = ParseStorageConfig(azureStorage, context);
            if (storageConfig?.MSAzureBlob == null)
            {
                context.Logger.LogError("Failed to parse Azure Blob configuration from storage JSON.");
                return;
            }

            context.Logger.LogInformation(
                $"Azure Storage Config -> StorageID={azureStorage.StorageID} Endpoint={storageConfig.MSAzureBlob.EndPoint ?? "NULL"} " +
                $"Bucket(Computed)={azureStorage.BucketName ?? "NULL"}");
            
            // TODO: Upload to S3 / checksum / status update

            FileVault fileVault = new(storageConfig);
            //download unencrypted file
            //await fileVault.DownloadBlobAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, "c:\\temp\test.wav");
            //decrypt the stream before saving to file

            try
            {
                string sFilePath = $"D:\\github\\{callDetailsInfo.AudioFile}";

                #region DownloadByteArrayAsync
                File.Delete(sFilePath);
                byte[] byArray = await fileVault.DownloadByteArrayAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, 60);
                await File.WriteAllBytesAsync(sFilePath, byArray);
                #endregion

                #region DownloadAndDecryptIfNeededAsync
                File.Delete(sFilePath);
                Stream stream = await fileVault.DownloadDecryptedStreamAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, 60);
                //write the stream to file
                using var fileStream = File.Create(sFilePath);
                stream.Position = 0;
                await stream.CopyToAsync(fileStream);
                #endregion
            }
            catch (Exception ex)
            {
                string err = ex.ToString();
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing message {message.MessageId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the default active Azure storage record:
    /// storagetype = 'azure' AND defaultstorage = true AND activeind = true.
    /// If multiple rows exist, prefers most recently updated.
    /// </summary>
    private async Task<TableStorage?> GetDefaultAzureStorageAsync(
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
    /// Deserialize the JSON column of the storage row into strongly typed StorageConfig.
    /// </summary>
    private StorageAZURE? ParseStorageConfig(TableStorage storage, ILambdaContext ctx)
    {
        if (string.IsNullOrWhiteSpace(storage.Json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<StorageAZURE>(
                storage.Json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError($"ParseStorageConfig failed StorageID={storage.StorageID}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Joins TableCallDetails and TableCallRecordingDetails on CallDetailID and returns
    /// ProgramCode, AudioFile, AudioFileLocation, IsAzureCloudAudio.
    /// Optional audioFile filter (case-insensitive) if provided.
    /// </summary>
    private async Task<CallDetailStorageInfo?> GetCallDetailsInfoAsync(
        ApplicationDbContext db,
        long callDetailId,
        string? audioFileName,
        ILambdaContext context,
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
                select new CallDetailStorageInfo
                {
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

            return result;
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"GetCallStorageInfoAsync failed CallDetailID={callDetailId}: {ex.Message}");
            return null;
        }
    }

    // ADDED: Load secret and cache connection strings once per container
    private async Task EnsureSecretConnectionsLoadedAsync()
    {
        if (_secretLoaded) return;
        lock (_secretLock)
        {
            if (_secretLoaded) return;
            _secretLoaded = true; // mark to prevent duplicate load attempts
        }

        var secretId = Environment.GetEnvironmentVariable("SECRET_ID");

        if (string.IsNullOrWhiteSpace(secretId))
        {
            Console.WriteLine("SECRET_ID not set");
            return;
        }

        if (_secretsManagerClient == null)
        {
            Console.WriteLine("Secrets Manager client not initialized.");
            return;
        }

        try
        {
            var resp = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretId
            });

            if (string.IsNullOrWhiteSpace(resp.SecretString))
            {
                WriteLog($"Secret '{secretId}' empty.");
                return;
            }

            using var jsonDoc = JsonDocument.Parse(resp.SecretString);
            var root = jsonDoc.RootElement;

            LoadConn(root, "ConnectionStrings_USReaderConnection", "USReaderConnection");
            LoadConn(root, "ConnectionStrings_USWriterConnection", "USWriterConnection");
            LoadConn(root, "ConnectionStrings_CAReaderConnection", "CAReaderConnection");
            LoadConn(root, "ConnectionStrings_CAWriterConnection", "CAWriterConnection");

            Console.WriteLine($"Secret '{secretId}' loaded with {_connCache.Count} connection entries.");
        }
        catch (ResourceNotFoundException e)
        {
            Console.WriteLine($"Secret '{secretId}' not found.", e);
        }
        catch (AmazonSecretsManagerException ax)
        {
            Console.WriteLine($"Secrets Manager error: {ax.Message}", ax);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected secret load error: {ex.Message}", ex);
        }

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

    // ADDED: Resolve connection from cache (country + role)
    private string? ResolveConnectionString(string country, bool writer)
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

    private static string CalculateMD5(Stream stream)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static void WriteLog(string message, Exception? ex = null)
    {
        string sJsonMsg = JsonConvert.SerializeObject(new Logging()
        {
            Message = message.Replace(Environment.NewLine, "\r"),
            Exception = ex,
            IsSuccess = ex == null
        });

        Console.WriteLine(sJsonMsg);
    }
}

public class AzureConnection
{
    public string ConnectionString { get; set; } = string.Empty;
    public BlobClientOptions option { get; set; } = new BlobClientOptions();
}

public class WrappedContentKey
{
    public string KeyId { get; set; } = string.Empty;
    public string EncryptedKey { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
}