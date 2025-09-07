using Npgsql.EntityFrameworkCore.PostgreSQL; // Add this using directive at the top of the file
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using CopyAzureToAWS.Data.Models;
using Amazon.SecretsManager;
using Amazon.SQS;
using Newtonsoft.Json;
using System.Reflection;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.SecretsManager.Model;
using System.Collections.Concurrent;
using CopyAzureToAWS.Common.Utilities;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System.Text;
using System.IO;

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
            var storageInfo = await GetCallDetailsInfoAsync(
                dbContext,
                sqsMessage.CallDetailID,
                sqsMessage.AudioFile,
                context);

            if (storageInfo == null)
            {
                context.Logger.LogWarning($"No joined call detail found CallDetailID={sqsMessage.CallDetailID} AudioFile={sqsMessage.AudioFile}");
                return;
            }

            context.Logger.LogInformation(
                $"Joined Call Detail -> CallDetailID={storageInfo.CallDetailID} ProgramCode={storageInfo.ProgramCode ?? "NULL"} " +
                $"AudioFile={storageInfo.AudioFile ?? "NULL"} IsAzureCloudAudio={storageInfo.IsAzureCloudAudio} Location={storageInfo.AudioFileLocation ?? "NULL"}");

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

            string accountname = Aes256CbcEncrypter.Decrypt(storageConfig.MSAzureBlob.AccountName);
            string AccountKey = Aes256CbcEncrypter.Decrypt(storageConfig.MSAzureBlob.AccountKey);
            string ConnectionString = Aes256CbcEncrypter.Decrypt(storageConfig.MSAzureBlob.ConnectionString);

            bool isEncryptedAudio = storageInfo.IsAzureCloudAudio == true && false; // TODO: real encryption flag

            // Choose blob path (prefer location then filename)
            //var blobPath = storageInfo.AudioFileLocation ?? storageInfo.AudioFile ?? sqsMessage.AudioFile;
            var blobPath = string.Concat(storageInfo.AudioFileLocation, "/",  storageInfo.AudioFile);
            if (string.IsNullOrWhiteSpace(blobPath))
            {
                context.Logger.LogError("Blob path could not be determined.");
                return;
            }

            var localFile = await DownloadAndDecryptIfNeededAsync(
                storageConfig,
                blobPath,
                null,
                isEncryptedAudio,
                null,
                context);

            if (localFile == null)
            {
                context.Logger.LogError("Failed to obtain audio file locally.");
                return;
            }

            // TODO: Upload to S3 / checksum / status update
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
    private StorageConfig? ParseStorageConfig(TableStorage storage, ILambdaContext ctx)
    {
        if (string.IsNullOrWhiteSpace(storage.Json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<StorageConfig>(
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

    /// <summary>
    /// High-level helper that downloads an Azure blob, writes it to /tmp,
    /// and optionally decrypts it using a key stored in Azure Key Vault.
    /// </summary>
    /// <param name="storageConfig">Parsed storage JSON (includes blob + key vault info).</param>
    /// <param name="blobVirtualPath">
    /// The full blob path (container/virtualfolders/file) OR if containerName supplied,
    /// this should be the blob name inside that container.
    /// </param>
    /// <param name="explicitContainer">Optional explicit container name (if you split path externally).</param>
    /// <param name="isEncrypted">Whether the blob content is encrypted.</param>
    /// <param name="encryptionKeySecretName">
    /// Secret name in Key Vault containing the encryption key (if null and encrypted, we will attempt 'audio-encryption-key').
    /// </param>
    /// <returns>Local file path (decrypted if encryption applied) or null on failure.</returns>
    private async Task<string?> DownloadAndDecryptIfNeededAsync(
        StorageConfig storageConfig,
        string blobVirtualPath,
        string? explicitContainer,
        bool isEncrypted,
        string? encryptionKeySecretName,
        ILambdaContext context,
        CancellationToken ct = default)
    {
        if (storageConfig?.MSAzureBlob == null)
        {
            context.Logger.LogError("DownloadAndDecryptIfNeededAsync: Missing Azure blob configuration.");
            return null;
        }

        var connStr = Aes256CbcEncrypter.Decrypt(storageConfig.MSAzureBlob.ConnectionString);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            context.Logger.LogError("DownloadAndDecryptIfNeededAsync: Azure blob connection string absent (ensure decrypted first).");
            return null;
        }

        // 1. Download blob into memory
        var (containerName, blobName) = ResolveContainerAndBlob(explicitContainer, blobVirtualPath, context);
        if (containerName == null || blobName == null)
            return null;

        var originalStream = await DownloadBlobToStreamAsync(connStr, containerName, blobName, context, ct);
        if (originalStream == null)
        {
            context.Logger.LogError("DownloadAndDecryptIfNeededAsync: Blob download failed.");
            return null;
        }

        // 2. Write raw (possibly encrypted) file
        var tmpDir = ".";
        var rawFileName = Path.GetFileName(blobName);
        if (string.IsNullOrWhiteSpace(rawFileName))
            rawFileName = $"blob_{Guid.NewGuid():N}";

        var rawPath = Path.Combine(tmpDir, rawFileName);
        await using (var fs = File.Create(rawPath))
        {
            originalStream.Position = 0;
            await originalStream.CopyToAsync(fs, ct);
        }

        context.Logger.LogInformation($"Saved blob to {rawPath} (Encrypted={isEncrypted}) Size={new FileInfo(rawPath).Length}");

        if (!isEncrypted)
            return rawPath;

        // 3. If encrypted: fetch key from Key Vault and decrypt to a new file
        if (storageConfig.MSAzureKeyVault == null)
        {
            context.Logger.LogError("DownloadAndDecryptIfNeededAsync: Encryption flagged but no Key Vault configuration provided.");
            return null;
        }

        var secretName = encryptionKeySecretName ?? "audio-encryption-key";
        var (keyBytes, ivBytes) = await ResolveEncryptionKeyAndIvAsync(storageConfig.MSAzureKeyVault, secretName, context, ct);
        if (keyBytes == null)
        {
            context.Logger.LogError($"DownloadAndDecryptIfNeededAsync: Unable to resolve encryption secret '{secretName}'.");
            return null;
        }

        var decryptedPath = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(rawFileName) + "_decrypted" + Path.GetExtension(rawFileName));
        try
        {
            await DecryptFileAesCbcAsync(rawPath, decryptedPath, keyBytes, ivBytes, context, ct);
            context.Logger.LogInformation($"Decrypted file written to {decryptedPath}");
            return decryptedPath;
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Decryption failed: {ex.Message}");
            return null;
        }
        finally
        {
            // Optional: remove raw encrypted file to save /tmp space if not needed
            // File.Delete(rawPath);
        }
    }

    /// <summary>
    /// Splits container and blob name if container not explicitly supplied.
    /// </summary>
    private (string? container, string? blob) ResolveContainerAndBlob(string? containerOverride, string fullOrPartialPath, ILambdaContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(containerOverride))
            return (containerOverride.Trim(), fullOrPartialPath.TrimStart('/'));

        var trimmed = fullOrPartialPath.TrimStart('/');
        var parts = trimmed.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Logger.LogError("ResolveContainerAndBlob: Cannot infer container from path. Provide explicit container.");
            return (null, null);
        }
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Core Azure blob downloader into MemoryStream.
    /// </summary>
    private async Task<MemoryStream?> DownloadBlobToStreamAsync(
        string connectionString,
        string container,
        string blobName,
        ILambdaContext context,
        CancellationToken ct)
    {
        try
        {
            var containerClient = new BlobContainerClient(connectionString, container);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(ct))
            {
                context.Logger.LogWarning($"Blob not found: {container}/{blobName}");
                return null;
            }

            var ms = new MemoryStream();
            await blobClient.DownloadToAsync(ms, cancellationToken: ct);
            ms.Position = 0;
            context.Logger.LogInformation($"Downloaded {container}/{blobName} Length={ms.Length}");
            return ms;
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"DownloadBlobToStreamAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Uses Azure Key Vault (client credentials) to retrieve encryption secret.
    /// Supports either "base64Key[:base64IV]" or JSON { "key":"...", "iv":"..." } formats.
    /// </summary>
    private async Task<(byte[]? key, byte[]? iv)> ResolveEncryptionKeyAndIvAsync(
        AzureKeyVaultConfig kvCfg,
        string secretName,
        ILambdaContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kvCfg.ClientId) ||
            string.IsNullOrWhiteSpace(kvCfg.ClientSecret) ||
            string.IsNullOrWhiteSpace(kvCfg.TenantID) ||
            string.IsNullOrWhiteSpace(kvCfg.KeyVaultURI))
        {
            ctx.Logger.LogError("ResolveEncryptionKeyAndIvAsync: Incomplete Key Vault configuration.");
            return (null, null);
        }

        try
        {
            var credential = new ClientSecretCredential(kvCfg.TenantID, Aes256CbcEncrypter.Decrypt(kvCfg.ClientId), Aes256CbcEncrypter.Decrypt(kvCfg.ClientSecret));
            var secretClient = new SecretClient(new Uri(kvCfg.KeyVaultURI.Replace("{0}", secretName, StringComparison.OrdinalIgnoreCase)), credential);

            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName, cancellationToken: ct);
            var raw = secret.Value?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                ctx.Logger.LogError("ResolveEncryptionKeyAndIvAsync: Secret value empty.");
                return (null, null);
            }

            // Try JSON first
            if (raw.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    var keyStr = root.TryGetProperty("key", out var k) ? k.GetString() : null;
                    var ivStr = root.TryGetProperty("iv", out var i) ? i.GetString() : null;
                    var keyBytes = string.IsNullOrWhiteSpace(keyStr) ? null : Convert.FromBase64String(keyStr);
                    var ivBytes = string.IsNullOrWhiteSpace(ivStr) ? new byte[16] : Convert.FromBase64String(ivStr);
                    return (keyBytes, ivBytes);
                }
                catch (Exception jx)
                {
                    ctx.Logger.LogWarning($"ResolveEncryptionKeyAndIvAsync: JSON parse fallback -> {jx.Message}");
                }
            }

            // Non-JSON: "base64Key[:base64IV]"
            var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            byte[] key = Convert.FromBase64String(parts[0]);
            byte[] iv = parts.Length > 1 ? Convert.FromBase64String(parts[1]) : new byte[16]; // zero IV fallback (adjust if policy forbids)
            return (key, iv);
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError($"ResolveEncryptionKeyAndIvAsync error: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted file into another file using provided key/iv.
    /// </summary>
    private async Task DecryptFileAesCbcAsync(
        string encryptedPath,
        string outputPath,
        byte[] key,
        byte[]? iv,
        ILambdaContext ctx,
        CancellationToken ct)
    {
        if (iv == null || iv.Length == 0)
            iv = new byte[16]; // default if not supplied (requires that encryption used a zero IV)

        await using var inFs = File.OpenRead(encryptedPath);
        await using var outFs = File.Create(outputPath);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var crypto = aes.CreateDecryptor();
        using var cryptoStream = new CryptoStream(inFs, crypto, CryptoStreamMode.Read);

        await cryptoStream.CopyToAsync(outFs, ct);
        await outFs.FlushAsync(ct);

        ctx.Logger.LogInformation($"DecryptFileAesCbcAsync: Decryption completed -> {outputPath}");
    }
}
