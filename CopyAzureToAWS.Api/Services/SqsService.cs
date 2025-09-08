using Amazon.SQS;
using Amazon.SQS.Model;
using CopyAzureToAWS.Api.Configuration;
using CopyAzureToAWS.Api.Infrastructure.Logging; // for WriteLog
using CopyAzureToAWS.Data.DTOs;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CopyAzureToAWS.Api.Services;

public interface ISqsService
{
    Task<(bool, Exception?)> SendMessageAsync(SqsMessage message);
}

public class SqsService : ISqsService
{
    private readonly IAmazonSQS _sqs;
    private readonly ILogger<SqsService> _logger;
    private readonly string _queueUrl;
    private readonly bool _isFifo;

    public SqsService(
        IAmazonSQS sqs,
        IOptions<SqsOptions> options,
        ILogger<SqsService> logger)
    {
        _sqs = sqs;
        _logger = logger;
        _queueUrl = options.Value.QueueUrl?.Trim() ?? throw new InvalidOperationException("SQS queue url is not configured.");
        _isFifo = _queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase);

        _logger.WriteLog(_logger.GetType().Name, $"SQS Service initialized. QueueUrl={_queueUrl}, IsFifo={_isFifo}", "Init");
    }

    public async Task<(bool , Exception?)> SendMessageAsync(SqsMessage message)
    {
        var requestId = string.IsNullOrWhiteSpace(message.RequestId)
            ? Guid.NewGuid().ToString()
            : message.RequestId;

        _logger.WriteLog(
            "Sqs.Send.Attempt",
            $"Sending message to queue (FIFO={_isFifo}) CallDetailID={message.CallDetailID}",
            requestId);

        try
        {
            var body = JsonSerializer.Serialize(message);
            var request = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = body
            };

            if (_isFifo)
            {
                request.MessageGroupId = message.CallDetailID.ToString();
                request.MessageDeduplicationId = $"{message.CallDetailID}-{requestId}";
            }

            var response = await _sqs.SendMessageAsync(request);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                _logger.WriteLog(
                    "Sqs.Send.Success",
                    $"Message sent CallDetailID={message.CallDetailID} MessageId={response.MessageId}",
                    requestId);
                return (true, null);
            }

            _logger.WriteLog(
                "Sqs.Send.NonOk",
                $"Non-OK status {(int)response.HttpStatusCode} CallDetailID={message.CallDetailID}",
                requestId,
                success: false);
            return (false, new Exception($"Non-OK status {(int)response.HttpStatusCode} CallDetailID={message.CallDetailID}"));
        }
        catch (Exception ex)
        {
            _logger.WriteLog(
                "Sqs.Send.Error",
                $"Exception sending message CallDetailID={message.CallDetailID}",
                requestId,
                success: false,
                exception: ex);
            return (false, ex);
        }
    }
}