using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using FluentAssertions;
using Moq;
using Xunit;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using AzureToAWS.Data.DTOs;
using Amazon.SecretsManager.Model;
using System.IO;
using System.Text;
using Amazon.S3.Model;

namespace AzureToAWS.Processor.Lambda.Tests;

public class FunctionTests : TestBase
{
    private readonly Mock<IAmazonS3> _s3ClientMock;
    private readonly Mock<IAmazonSecretsManager> _secretsManagerMock;
    private readonly Mock<AmazonDynamoDBClient> _dynamoDBClientMock;
    private readonly Mock<AmazonSQSClient> _sqsClientMock;
    private readonly Function _sut;

    public FunctionTests()
    {
        _s3ClientMock = Mock<IAmazonS3>();
        _secretsManagerMock = Mock<IAmazonSecretsManager>();
        _dynamoDBClientMock = Mock<AmazonDynamoDBClient>();
        _sqsClientMock = Mock<AmazonSQSClient>();

        _sut = new Function(
            _s3ClientMock.Object,
            _s3ClientMock.Object, // Canada client
            _sqsClientMock.Object,
            _secretsManagerMock.Object,
            _dynamoDBClientMock.Object);
    }

    [Fact]
    public async Task FunctionHandler_EmptyBatch_ReturnsEmptyFailures()
    {
        // Arrange
        var sqsEvent = new SQSEvent
        {
            Records = new List<SQSEvent.SQSMessage>()
        };

        // Act
        var result = await _sut.FunctionHandler(sqsEvent);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task FunctionHandler_InvalidMessage_AddsToFailures()
    {
        // Arrange
        var messageId = "test-message-id";
        var sqsEvent = new SQSEvent
        {
            Records = new List<SQSEvent.SQSMessage>
            {
                new()
                {
                    MessageId = messageId,
                    Body = "invalid-json"
                }
            }
        };

        // Act
        var result = await _sut.FunctionHandler(sqsEvent);

        // Assert
        result.BatchItemFailures.Should().ContainSingle();
        result.BatchItemFailures[0].ItemIdentifier.Should().Be(messageId);
    }

    [Fact]
    public async Task FunctionHandler_ValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var sqsMessage = new SqsMessage
        {
            CallDetailID = 123,
            AudioFile = "test.wav",
            CountryCode = "US",
            RequestId = "test-request"
        };

        var sqsEvent = new SQSEvent
        {
            Records = new List<SQSEvent.SQSMessage>
            {
                new()
                {
                    MessageId = "test-message-id",
                    Body = JsonSerializer.Serialize(sqsMessage)
                }
            }
        };

        SetupSecretManager();
        SetupDynamoDb(sqsMessage);
        SetupS3();

        // Act
        var result = await _sut.FunctionHandler(sqsEvent);

        // Assert
        result.BatchItemFailures.Should().BeEmpty();
    }

    private void SetupSecretManager()
    {
        _secretsManagerMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new
                {
                    ConnectionStrings_USReaderConnection = "test-connection",
                    ConnectionStrings_USWriterConnection = "test-connection",
                    ConnectionStrings_CAReaderConnection = "test-connection",
                    ConnectionStrings_CAWriterConnection = "test-connection"
                })
            });
    }

    private void SetupDynamoDb(SqsMessage message)
    {
        _dynamoDBClientMock
            .Setup(x => x.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        { "arn", new AttributeValue { S = "test-arn" } },
                        { "alias", new AttributeValue { S = "test-alias" } },
                        { "clientcode", new AttributeValue { S = "test-client" } },
                        { "systemname", new AttributeValue { S = "us-east-1" } }
                    }
                }
            });
    }

    private void SetupS3()
    {
        _s3ClientMock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });

        _s3ClientMock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("dummy")),
                ContentLength = 5
            });
    }
}