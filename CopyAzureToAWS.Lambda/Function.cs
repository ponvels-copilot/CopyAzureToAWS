using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CopyAzureToAWS.Lambda;

public class Function
{
    private readonly IAmazonS3 _s3Client;

    public Function()
    {
        _s3Client = new AmazonS3Client();
    }

    /// <summary>
    /// Lambda function handler for processing SQS messages to copy files from Azure to AWS S3
    /// </summary>
    /// <param name="evnt">SQS event containing messages</param>
    /// <param name="context">Lambda context</param>
    /// <returns>Task</returns>
    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        //foreach (var message in evnt.Records)
        //{
        //    await ProcessMessage(message, context);
        //}
    }

    //private async Task ProcessMessage(SQSEvent.SQSMessage message, ILambdaContext context)
    //{
    //    try
    //    {
    //        context.Logger.LogInformation($"Processing message: {message.MessageId}");

    //        // Parse the SQS message
    //        var sqsMessage = JsonSerializer.Deserialize<SqsMessage>(message.Body);
    //        if (sqsMessage == null)
    //        {
    //            context.Logger.LogError("Failed to deserialize SQS message");
    //            return;
    //        }

    //        // Set up database context
    //        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
    //        if (string.IsNullOrEmpty(connectionString))
    //        {
    //            context.Logger.LogError("Database connection string not configured");
    //            return;
    //        }

    //        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
    //        optionsBuilder.UseSqlServer(connectionString);

    //        using var dbContext = new ApplicationDbContext(optionsBuilder.Options);

    //        // Update status to Processing
    //        await UpdateCallDetailStatus(dbContext, sqsMessage.CallDetailId, "Processing", null, context);

    //        // Copy file from Azure to S3
    //        await CopyAzureToS3(dbContext, sqsMessage, context);
    //    }
    //    catch (Exception ex)
    //    {
    //        context.Logger.LogError($"Error processing message {message.MessageId}: {ex.Message}");
    //    }
    //}

    //private async Task CopyAzureToS3(ApplicationDbContext dbContext, SqsMessage sqsMessage, ILambdaContext context)
    //{
    //    try
    //    {
    //        // Download from Azure Blob Storage
    //        var blobServiceClient = new BlobServiceClient(sqsMessage.AzureConnectionString);
    //        var blobClient = new BlobClient(new Uri(sqsMessage.AzureBlobUrl));

    //        context.Logger.LogInformation($"Downloading from Azure: {sqsMessage.AzureBlobUrl}");

    //        var azureStream = new MemoryStream();
    //        await blobClient.DownloadToAsync(azureStream);
    //        azureStream.Position = 0;

    //        // Calculate MD5 checksum of Azure content
    //        var azureMd5 = CalculateMD5(azureStream);
    //        azureStream.Position = 0;

    //        // Upload to S3
    //        var s3Key = $"{sqsMessage.CallDetailId}/{sqsMessage.AudioFileName}";
    //        context.Logger.LogInformation($"Uploading to S3: {sqsMessage.S3BucketName}/{s3Key}");

    //        var uploadRequest = new PutObjectRequest
    //        {
    //            BucketName = sqsMessage.S3BucketName,
    //            Key = s3Key,
    //            InputStream = azureStream,
    //            ContentType = "audio/wav"
    //        };

    //        var uploadResponse = await _s3Client.PutObjectAsync(uploadRequest);
            
    //        // Get S3 object MD5 checksum
    //        var getObjectRequest = new GetObjectRequest
    //        {
    //            BucketName = sqsMessage.S3BucketName,
    //            Key = s3Key
    //        };

    //        var s3Stream = new MemoryStream();
    //        using (var getObjectResponse = await _s3Client.GetObjectAsync(getObjectRequest))
    //        {
    //            await getObjectResponse.ResponseStream.CopyToAsync(s3Stream);
    //        }
    //        s3Stream.Position = 0;
            
    //        var s3Md5 = CalculateMD5(s3Stream);

    //        // Compare MD5 checksums
    //        if (azureMd5.Equals(s3Md5, StringComparison.OrdinalIgnoreCase))
    //        {
    //            context.Logger.LogInformation($"MD5 checksums match: {azureMd5}");

    //            // Update database with S3 details and MD5
    //            var callDetail = await dbContext.TableAzureToAWSRequest
    //                .FirstOrDefaultAsync(cd => cd.CallDetailId == sqsMessage.CallDetailId);
                
    //            if (callDetail != null)
    //            {
    //                callDetail.S3Key = s3Key;
    //                callDetail.Md5Checksum = azureMd5;
    //                callDetail.UpdatedAt = DateTime.UtcNow;
    //                await dbContext.SaveChangesAsync();
    //            }

    //            // Delete from Azure if checksums match
    //            context.Logger.LogInformation("Deleting file from Azure storage");
    //            await blobClient.DeleteAsync();

    //            // Update status to Completed
    //            await UpdateCallDetailStatus(dbContext, sqsMessage.CallDetailId, "Completed", null, context);
    //        }
    //        else
    //        {
    //            context.Logger.LogError($"MD5 checksums do not match. Azure: {azureMd5}, S3: {s3Md5}");
    //            await UpdateCallDetailStatus(dbContext, sqsMessage.CallDetailId, "Failed", 
    //                "MD5 checksums do not match between Azure and S3", context);
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        context.Logger.LogError($"Error copying file: {ex.Message}");
    //        await UpdateCallDetailStatus(dbContext, sqsMessage.CallDetailId, "Failed", ex.Message, context);
    //    }
    //}

    //private async Task UpdateCallDetailStatus(ApplicationDbContext dbContext, string callDetailId, 
    //    string status, string? errorMessage, ILambdaContext context)
    //{
    //    try
    //    {
    //        var callDetail = await dbContext.CallDetails
    //            .FirstOrDefaultAsync(cd => cd.CallDetailId == callDetailId);

    //        if (callDetail != null)
    //        {
    //            callDetail.Status = status;
    //            callDetail.UpdatedAt = DateTime.UtcNow;
                
    //            if (!string.IsNullOrEmpty(errorMessage))
    //            {
    //                callDetail.ErrorMessage = errorMessage;
    //            }

    //            await dbContext.SaveChangesAsync();
    //            context.Logger.LogInformation($"Updated call detail {callDetailId} status to {status}");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        context.Logger.LogError($"Error updating call detail status: {ex.Message}");
    //    }
    //}

    private static string CalculateMD5(Stream stream)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
