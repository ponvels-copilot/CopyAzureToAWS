using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model; // ADD this near other using directives
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using CopyAzureToAWS.Common.Utilities;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using CopyAzureToAWS.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

//[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CopyAzureToAWS.Lambda;

public class Function
{
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

    private readonly string SecretId = Environment.GetEnvironmentVariable("SECRET_ID").ToString();
    private readonly int SecretsManagerTimeOutInSeconds = int.Parse(Environment.GetEnvironmentVariable("SecretsManagerTimeOutInSeconds").ToString());
    private readonly string TableClientCountryKMSMap = Environment.GetEnvironmentVariable("TableClientCountryKMSMap");
    private readonly string USS3BucketName = Environment.GetEnvironmentVariable("USS3BucketName");
    private readonly string CAS3BucketName = Environment.GetEnvironmentVariable("CAS3BucketName");

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

    private static readonly ConcurrentDictionary<string, (string Arn, string Alias, string ClientCode, string SystemName)> _kmsCache = new(StringComparer.OrdinalIgnoreCase);

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

        WriteLog("IAmazonS3, IAmazonSecretsManager, AmazonSQSClient and AmazonDynamoDBClient initialization completed - Function()");
    }

    public Function(IAmazonS3 _s3Client, IAmazonS3 _sqsClientCA, AmazonSQSClient _sqsClient, IAmazonSecretsManager _secretsManagerClient, AmazonDynamoDBClient _dynamoDBClient)
    {
        this._s3Client = _s3Client;
        this._s3ClientCanada = _sqsClientCA;
        this._secretsManagerClient = _secretsManagerClient;
        this._dynamoDBClient = _dynamoDBClient;
        this._sqsClient = _sqsClient;

        WriteLog("IAmazonS3, IAmazonSecretsManager, AmazonSQSClient and AmazonDynamoDBClient initialization completed - Function(parameter)");
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

            var sqsMessage = JsonConvert.DeserializeObject<SqsMessage>(message.Body);
            if (sqsMessage == null)
            {
                context.Logger.LogError("Failed to deserialize SQS message");
                return;
            }

            await EnsureSecretConnectionsLoadedAsync();

            var country = string.IsNullOrWhiteSpace(sqsMessage.CountryCode) ? "US" : sqsMessage.CountryCode.Trim().ToUpperInvariant();

            var connectionString = ResolveConnectionString(country, writer: false);
            if (string.IsNullOrEmpty(connectionString))
            {
                context.Logger.LogError("Database connection string not configured");
                return;
            }

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using var dbContext = new ApplicationDbContext(optionsBuilder.Options);

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

            //download unencrypted file
            //FileVault fileVault = new(storageConfig);
            //await fileVault.DownloadBlobAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, "c:\\temp\test.wav");
            //decrypt the stream before saving to file

            //string sFilePath = $"D:\\github\\{callDetailsInfo.AudioFile}";

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
            var (kmsArn, kmsAlias, clientcode, systemname) = await GetKmsKeyForProgramAsync(
                    callDetailsInfo.ProgramCode,
                    context);

            FileVault fileVault = new(storageConfig);
            Stream stream = await fileVault.DownloadDecryptedStreamAsync(callDetailsInfo.AudioFileLocation, callDetailsInfo.AudioFile, 60);

            var (Success, Bucket, Key, newaudiofilelocation, AzureMd5, S3Md5, S3SizeBytes, Error) = await UploadToS3AndVerifyAsync(
                stream,
                sqsMessage.CountryCode!,
                callDetailsInfo.AudioFile!,
                kmsArn ?? string.Empty,
                clientcode ?? "default",
                systemname ?? RegionEndpoint.USEast1.SystemName,
                callDetailsInfo.CallDate,
                context);

            if (Success)
            {
                context.Logger.LogInformation($"Upload succeeded. S3 Key={Key} Size={S3SizeBytes} MD5={S3Md5}");

                connectionString = ResolveConnectionString(country, writer: true);
                if (string.IsNullOrEmpty(connectionString))
                {
                    context.Logger.LogError("Database connection string not configured");
                    return;
                }

                optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                optionsBuilder.UseNpgsql(connectionString);

                using var dbContextWriter = new ApplicationDbContext(optionsBuilder.Options);

                // Update call_recording_details with new S3 key (location), MD5 and size
                var updated = await UpdateRecordingDetailsAsync(
                    dbContextWriter,
                    callDetailsInfo.CallDetailID,
                    callDetailsInfo.AudioFile!,
                    newaudiofilelocation,
                    S3Md5,
                    S3SizeBytes,
                    context);

                if (!updated)
                    context.Logger.LogWarning($"Recording details update failed for CallDetailID={callDetailsInfo.CallDetailID}");
            }
            else
            {
                context.Logger.LogError($"Upload failed. Error={Error}");
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing message {message.MessageId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates AudioFileLocation, AudioFileMd5Hash, AudioFileSize for the matching audio record (case-insensitive filename).
    /// Returns true if row updated.
    /// </summary>
    private static async Task<bool> UpdateRecordingDetailsAsync(
        ApplicationDbContext db,
        long callDetailId,
        string audioFileName,
        string newLocation,
        string md5,
        long sizeBytes,
        ILambdaContext ctx,
        CancellationToken ct = default)
    {
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
                ctx.Logger.LogWarning($"UpdateRecordingDetailsAsync: Record not found CallDetailID={callDetailId} AudioFile={audioFileName}");
                return false;
            }

            record.AudioFileLocation = newLocation;
            record.AudioFileMd5Hash = md5;
            record.AudioFileSize = sizeBytes;
            record.AudioStorageID = null; // Reset to default storage
            record.IsEncryptedAudio = null;
            record.IsAzureCloudAudio = false; // Mark as no longer Azure

            await db.SaveChangesAsync(ct);
            ctx.Logger.LogInformation($"UpdateRecordingDetailsAsync: Updated CallDetailID={callDetailId} File={audioFileName} Size={sizeBytes} MD5={md5}");
            return true;
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError($"UpdateRecordingDetailsAsync error CallDetailID={callDetailId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the default active Azure storage record:
    /// storagetype = 'azure' AND defaultstorage = true AND activeind = true.
    /// If multiple rows exist, prefers most recently updated.
    /// </summary>
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
    /// Deserialize the JSON column of the storage row into strongly typed StorageConfig.
    /// </summary>
    private static StorageAZURE? ParseStorageConfig(TableStorage storage, ILambdaContext ctx)
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
    private static async Task<CallDetailInfo?> GetCallDetailsInfoAsync(
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
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException e)
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

    /// <summary>
    /// Query DynamoDB (TableClientCountryKMSMap) for KMS mapping by ProgramCode (partition key).
    /// If CountryCode is also stored (non-key) it is applied as a FilterExpression.
    /// Caches results in-memory for the lifetime of the Lambda container.
    /// Expected item attributes: ProgramCode (PK), KmsKeyArn, KmsAlias, CountryCode (optional).
    /// </summary>
    private async Task<(string? Arn, string? Alias, string? ClientCode, string? SystemName)> GetKmsKeyForProgramAsync(
        string? programCode,
        ILambdaContext ctx,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(programCode))
            return (null, null, null, null);

        if (string.IsNullOrWhiteSpace(TableClientCountryKMSMap))
        {
            ctx.Logger.LogError("DynamoDB table name (TableClientCountryKMSMap) not configured in environment variables.");
            return (null, null, null, null);
        }

        if (_dynamoDBClient == null)
        {
            ctx.Logger.LogError("DynamoDB client not initialized.");
            return (null, null, null, null);
        }

        var cacheKey = programCode;
        if (_kmsCache.TryGetValue(cacheKey, out var cached))
            return (cached.Arn, cached.Alias, cached.ClientCode, cached.SystemName);

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
                return (null, null, null, null);

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
                return (arn, alias, clientCode, systemName);
            }
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError($"GetKmsKeyForProgramAsync Query failed ProgramCode={programCode}: {ex.Message}");
        }

        return (null, null, null, null);
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
    private async Task<(bool Success,
                        string? Bucket,
                        string? Key,
                        string newaudiofilelocation,
                        string AzureMd5,
                        string S3Md5,
                        long S3SizeBytes,
                        string? Error)> UploadToS3AndVerifyAsync(
        Stream azureStream,
        string countryCode,
        string fileName,
        string kmsArn,
        string clientcode,
        string systemname,
        DateTime? CallDate,
        ILambdaContext ctx,
        CancellationToken ct = default)
    {
        if (_s3Client == null)
            return (false, null, null, string.Empty, string.Empty,string.Empty, 0, "S3 client not initialized");

        if (azureStream == null || !azureStream.CanRead)
            return (false, null, null, string.Empty, string.Empty, string.Empty, 0, "Azure stream invalid");

        var bucket = countryCode?.ToUpperInvariant() switch
        {
            "CA" => CAS3BucketName,
            _ => USS3BucketName // default US
        };

        if (string.IsNullOrWhiteSpace(bucket))
            return (false, bucket, null, string.Empty, string.Empty, string.Empty, 0, "Resolved bucket name empty (check env USS3BucketName / CAS3BucketName)");

        // Replace the problematic line in UploadToS3AndVerifyAsync with the following:

        //how the key will get generated for date 2025/07/01 01:01:55

        DateTime dateTime = CallDate ?? DateTime.UtcNow;
        var key = $"callrecordings/{clientcode}/{dateTime:yyyy}/{dateTime:MM}/{dateTime:dd}/{dateTime:HH}/{dateTime:mm}/{fileName}";
        var newaudiofilelocation = $"{bucket}/callrecordings/{clientcode}/{dateTime:yyyy}/{dateTime:MM}/{dateTime:dd}/{dateTime:HH}/{dateTime:mm}";

        if (string.IsNullOrWhiteSpace(key))
            return (false, bucket, null, newaudiofilelocation, string.Empty, string.Empty, 0, "Key empty");

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

            ctx.Logger.LogInformation($"Uploading to S3 Bucket={bucket} Key={key} KMS={(string.IsNullOrEmpty(kmsArn) ? "None" : kmsArn)} MD5={azureMd5Hex}");

            var putResp = await GetS3Client(systemname).PutObjectAsync(putReq, ct);
            if (putResp.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return (false, bucket, key, newaudiofilelocation, azureMd5Hex, "", 0, $"PutObject non-OK: {putResp.HttpStatusCode}");
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
                ctx.Logger.LogError($"MD5 mismatch Azure={azureMd5Hex} S3={s3Md5Hex} Bucket={bucket} Key={key}");
                return (false, bucket, key, newaudiofilelocation, azureMd5Hex, s3Md5Hex, s3Size, "MD5 mismatch");
            }

            ctx.Logger.LogInformation($"Upload verified MD5={azureMd5Hex} Size={s3Size} Bucket={bucket} Key={key}");
            return (true, bucket, key, newaudiofilelocation, azureMd5Hex, s3Md5Hex, s3Size, null);
        }
        catch (Exception ex)
        {
            return (false, bucket, key, newaudiofilelocation, azureMd5Hex, "", 0, ex.Message);
        }
        finally
        {
            if (azureStream.CanSeek)
                azureStream.Position = 0;
        }
    }

    /// <summary>
    /// Computes MD5 for a stream for S3 PutObject (returns hex + base64). 
    /// If stream is non-seekable, it is buffered into memory once.
    /// Stream position restored to 0 after computation if seekable.
    /// </summary>
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
        if (_s3Client.Config.RegionEndpoint.Equals(RegionEndpoint.GetBySystemName(sSystemName)))
        {
            WriteLog($"S3 client already created with region : {_s3Client.Config.RegionEndpoint.SystemName}. Instantiation skipped.");
            return _s3Client;
        }

        if (_s3ClientCanada == null)
        {
            _s3ClientCanada = new AmazonS3Client(new AmazonS3Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(sSystemName)
            });
            WriteLog($"S3 client created with region : {_s3ClientCanada.Config.RegionEndpoint.SystemName}. Instantiation completed.");
        }

        return _s3ClientCanada;
    }
}