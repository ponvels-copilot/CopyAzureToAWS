using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;
using CopyAzureToAWS.Data.DTOs;

namespace CopyAzureToAWS.Api.Services;

public interface ISqsService
{
    Task<bool> SendMessageAsync(SqsMessage message);
}

public class SqsService : ISqsService
{
    private readonly IConfiguration _configuration;
    private readonly IAmazonSQS _sqsClient;

    public SqsService(IConfiguration configuration)
    {
        _configuration = configuration;
        _sqsClient = new AmazonSQSClient();
    }

    public async Task<bool> SendMessageAsync(SqsMessage message)
    {
        try
        {
            var queueUrl = _configuration["AWS:SQS:QueueUrl"];
            
            if (string.IsNullOrEmpty(queueUrl))
            {
                throw new InvalidOperationException("SQS Queue URL not configured");
            }

            var messageBody = JsonSerializer.Serialize(message);
            
            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = messageBody
            };

            var response = await _sqsClient.SendMessageAsync(sendMessageRequest);
            return !string.IsNullOrEmpty(response.MessageId);
        }
        catch (Exception)
        {
            return false;
        }
    }
}