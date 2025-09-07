using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using CopyAzureToAWS.Data.DTOs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CopyAzureToAWS.Common.Utilities
{
    public class FileVault
    {
        private string EndPoint { get; set; }
        string _AccountName = string.Empty;
        private string AccountName
        {
            get { return _AccountName; }
            set { _AccountName = Aes256CbcEncrypter.Decrypt(value); }
        }

        string _AccountKey = string.Empty;
        private string AccountKey
        {
            get { return _AccountKey; }
            set { _AccountKey = Aes256CbcEncrypter.Decrypt(value); }
        }

        private static string _ClientId = string.Empty;
        private static string ClientId
        {
            get { return _ClientId; }
            set { _ClientId = Aes256CbcEncrypter.Decrypt(value); }
        }

        private static string _ClientSecret = string.Empty;
        private static string ClientSecret
        {
            get { return _ClientSecret; }
            set { _ClientSecret = Aes256CbcEncrypter.Decrypt(value); }
        }

        private static string TenantID = string.Empty;

        private static string connectionstring = string.Empty;
        private static string ConnectionString
        {
            get { return connectionstring; }
            set { connectionstring = Aes256CbcEncrypter.Decrypt(value); }
        }

        public string KeyVaultURI { get; set; } = string.Empty;

        private readonly BlobServiceClient blobServiceClient;
        private readonly KeyClient? keyClient;
        private readonly SecretClient? secretClient;

        public FileVault(StorageAZURE storageAZURE)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            EndPoint = storageAZURE.MSAzureBlob.EndPoint;
            AccountName = storageAZURE.MSAzureBlob.AccountName;
            AccountKey = storageAZURE.MSAzureBlob.AccountKey;
            ClientId = storageAZURE.MSAzureKeyVault.ClientId;
            ClientSecret = storageAZURE.MSAzureKeyVault.ClientSecret;

            TenantID = storageAZURE.MSAzureKeyVault.TenantID;
            ConnectionString = storageAZURE.MSAzureBlob.ConnectionString;

            // Create BlobServiceClient using StorageSharedKeyCredential
            string blobServiceEndpoint = $"https://{AccountName}.blob.core.windows.net";
            var storageCredentials = new StorageSharedKeyCredential(AccountName, AccountKey);
            blobServiceClient = new BlobServiceClient(new Uri(blobServiceEndpoint), storageCredentials);
        }

        public async Task DownloadBlobAsync(string containerName, string blobName, string downloadFilePath)
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            Console.WriteLine($"Downloading blob to {downloadFilePath}");

            await blobClient.DownloadToAsync(downloadFilePath);
        }

        public static async Task<BlobContainerClient> GetContainerAsync(AzureConnection AzureConn, string ContainerName)
        {
            BlobServiceClient blobServiceClient = new(AzureConn.ConnectionString, AzureConn.option);

            // Retrieve a reference to a container.
            return await Task.Run(() => blobServiceClient.GetBlobContainerClient(ContainerName));
        }

        public async Task<byte[]> DownloadByteArrayAsync(string containerName, string blobName, int MaximumExecutionTimeSec)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(MaximumExecutionTimeSec);

            // Attempt to detect client-side encryption metadata.
            WrappedContentKey wrappedContentKey = await GetEncryptionKeyId(containerName, blobName);

            // If no encryption metadata present, perform a plain (non‑decrypting) download.
            if (wrappedContentKey == null)
            {
                AzureConnection plainConn = new()
                {
                    ConnectionString = ConnectionString
                };

                BlobClient plainBlob = (await GetContainerAsync(plainConn, containerName)).GetBlobClient(blobName);

                await using MemoryStream ms = new();
                using CancellationTokenSource cts = new(timeout);
                await plainBlob.DownloadToAsync(ms, cts.Token);
                return ms.ToArray();
            }

            // Encrypted path (existing logic retained; now guarded by null check).
            KeyVaultURI = wrappedContentKey.KeyId;

            ClientSecretCredential cred = new(TenantID, ClientId, ClientSecret);

            CryptographyClient cryptoClient = new(new Uri(KeyVaultURI), cred);
            KeyResolver keyResolver = new(cred);

            ClientSideEncryptionOptions encryptionOptions = new(ClientSideEncryptionVersion.V2_0)
            {
                KeyEncryptionKey = cryptoClient,
                KeyResolver = keyResolver,
                KeyWrapAlgorithm = "RSA-OAEP"
            };

            AzureConnection encryptedConn = new()
            {
                ConnectionString = ConnectionString,
                option = new SpecializedBlobClientOptions
                {
                    ClientSideEncryption = encryptionOptions
                }
            };

            BlobClient encBlob = (await GetContainerAsync(encryptedConn, containerName)).GetBlobClient(blobName);

            await using (MemoryStream encryptedStream = new())
            {
                using CancellationTokenSource cts = new(timeout);
                await encBlob.DownloadToAsync(encryptedStream, cts.Token);
                return encryptedStream.ToArray();
            }
        }

        public async Task<Stream> GetStreamAsync(string containerName, string blobName, int MaximumExecutionTimeSec)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(MaximumExecutionTimeSec);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            Console.WriteLine($"Downloading blob {blobName} from container {containerName}");

            MemoryStream? memoryStream = new();
            using CancellationTokenSource? cts = new(timeout);
            await blobClient.DownloadToAsync(memoryStream, cts.Token);
            memoryStream.Position = 0; // Reset the stream position to the beginning

            return memoryStream;
        }

        /// <summary>
        /// NEW: Downloads a blob and transparently decrypts it (client-side encryption v2) if metadata contains wrapped key info.
        /// Falls back to plain download when no encryption metadata is present.
        /// </summary>
        /// <param name="containerName">Azure blob container name.</param>
        /// <param name="blobName">Blob name (path inside container).</param>
        /// <param name="maximumExecutionTimeSec">Timeout seconds for the download operation.</param>
        /// <returns>MemoryStream positioned at start containing decrypted (or plain) content.</returns>
        public async Task<Stream> DownloadDecryptedStreamAsync(string containerName, string blobName, int maximumExecutionTimeSec)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentException("containerName cannot be empty.", nameof(containerName));
            if (string.IsNullOrWhiteSpace(blobName))
                throw new ArgumentException("blobName cannot be empty.", nameof(blobName));

            // 1. Probe metadata for encryption info
            WrappedContentKey wrappedKey = await GetEncryptionKeyId(containerName, blobName);

            // If no encryption metadata -> plain download
            if (wrappedKey == null)
            {
                return await GetStreamAsync(containerName, blobName, maximumExecutionTimeSec);
            }

            // 2. Build encrypted blob client (mirrors DownloadByteArrayAsync but returns Stream)
            TimeSpan timeout = TimeSpan.FromSeconds(maximumExecutionTimeSec);

            AzureConnection azureConn = new()
            {
                ConnectionString = ConnectionString
            };

            // KeyVaultURI is stored in metadata KeyId
            KeyVaultURI = wrappedKey.KeyId;

            ClientSecretCredential cred = new(TenantID, ClientId, ClientSecret);
            CryptographyClient cryptoClient = new(new Uri(KeyVaultURI), cred);
            KeyResolver resolver = new(cred);

            ClientSideEncryptionOptions encryptionOptions = new(ClientSideEncryptionVersion.V2_0)
            {
                KeyEncryptionKey = cryptoClient,
                KeyResolver = resolver,
                KeyWrapAlgorithm = "RSA-OAEP"
            };
            azureConn.option = new SpecializedBlobClientOptions { ClientSideEncryption = encryptionOptions };

            BlobClient encBlobClient = (await GetContainerAsync(azureConn, containerName)).GetBlobClient(blobName);

            MemoryStream result = new();
            using CancellationTokenSource cts = new(timeout);
            await encBlobClient.DownloadToAsync(result, cts.Token);
            result.Position = 0;

            return result;
        }

        private async Task<WrappedContentKey> GetEncryptionKeyId(string containerName, string blobName)
        {
            var blobServiceClient = new BlobServiceClient(new Uri($"https://{AccountName}.blob.core.windows.net"), new StorageSharedKeyCredential(AccountName, AccountKey));
            var blobClient = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);

            // Get metadata
            var properties = await blobClient.GetPropertiesAsync();

            MainClass mainClass1 = null;
            foreach (var item in properties.Value.Metadata)
            {
                Console.WriteLine($"Key: {item.Key}, Value: {item.Value}");
                mainClass1 = JsonConvert.DeserializeObject<MainClass>(item.Value);

                if (mainClass1 != null && !string.IsNullOrEmpty(mainClass1.WrappedContentKey.KeyId))
                {
                    byte[] arr = DecryptWrappedKey(mainClass1.WrappedContentKey.KeyId, mainClass1.WrappedContentKey.EncryptedKey);
                    return mainClass1.WrappedContentKey;
                }
            }

            return null;
        }

        public byte[] DecryptWrappedKey(string keyId, string encryptedKey)
        {
            var credential = new ClientSecretCredential(TenantID, ClientId, ClientSecret);
            var client = new CryptographyClient(new Uri(keyId), credential);
            byte[] encryptedKeyBytes = Convert.FromBase64String(encryptedKey);
            var decryptResult = client.Decrypt(EncryptionAlgorithm.RsaOaep, encryptedKeyBytes);
            return decryptResult.Plaintext;
        }
    }

    public class MainClass
    {
        public string EncryptionMode { get; set; } = string.Empty;

        public string ContentEncryptionIV { get; set; } = string.Empty;
        public WrappedContentKey WrappedContentKey { get; set; } = new WrappedContentKey();
        public EncryptionAgent EncryptionAgent { get; set; } = new EncryptionAgent();
        public KeyWrappingMetadata KeyWrappingMetadata { get; set; } = new KeyWrappingMetadata();

    }

    public class WrappedContentKey
    {
        public string KeyId { get; set; } = string.Empty;
        public string EncryptedKey { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
    }

    public class EncryptionAgent
    {
        public string Protocol { get; set; } = string.Empty;
        public string EncryptionAlgorithm { get; set; } = string.Empty;
    }

    public class KeyWrappingMetadata
    {
        public string EncryptionLibrary { get; set; } = string.Empty;
    }

    public class AzureConnection
    {
        public string ConnectionString { get; set; } = string.Empty;
        public BlobClientOptions option { get; set; } = new BlobClientOptions();
    }
}
