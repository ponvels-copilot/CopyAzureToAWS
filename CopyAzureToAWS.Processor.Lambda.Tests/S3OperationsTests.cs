using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Moq;
using Xunit;
using System.Text;
using Amazon.SecretsManager;
using Amazon.DynamoDBv2;
using Amazon.SQS;

namespace AzureToAWS.Processor.Lambda.Tests;

public class S3OperationsTests : TestBase
{
    private readonly Mock<IAmazonS3> _s3ClientMock;
    private readonly Function _sut;

    public S3OperationsTests()
    {
        _s3ClientMock = Mock<IAmazonS3>();
        var secretsManagerMock = Mock<IAmazonSecretsManager>();
        var dynamoDBClientMock = Mock<AmazonDynamoDBClient>();
        var sqsClientMock = Mock<AmazonSQSClient>();

        // Ensure the mock S3 client has the expected region endpoint
        var s3Config = new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1 };
        _s3ClientMock.SetupGet(x => x.Config).Returns(s3Config);

        _sut = new Function(
            _s3ClientMock.Object,
            _s3ClientMock.Object,
            sqsClientMock.Object,
            secretsManagerMock.Object,
            dynamoDBClientMock.Object);
    }

    [Fact]
    public async Task DeleteS3ObjectAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        _s3ClientMock
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        // Act
        var (deleted, exception) = await _sut.DeleteS3ObjectAsync("test-bucket", "test-key", "us-east-1");

        // Assert
        deleted.Should().BeTrue();
        exception.Should().BeNull();
    }

    [Fact]
    public async Task DeleteS3ObjectAsync_WhenObjectNotFound_ReturnsTrue()
    {
        // Arrange
        _s3ClientMock
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        // Act
        var (deleted, exception) = await _sut.DeleteS3ObjectAsync("test-bucket", "test-key", "us-east-1");

        // Assert
        deleted.Should().BeTrue();
        exception.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("bucket", null)]
    [InlineData("", "key")]
    [InlineData("bucket", "")]
    public async Task DeleteS3ObjectAsync_WithInvalidInputs_ReturnsFalse(string bucket, string key)
    {
        // Act
        var (deleted, exception) = await _sut.DeleteS3ObjectAsync(bucket, key, "us-east-1");

        // Assert
        deleted.Should().BeFalse();
        exception.Should().NotBeNull();
        exception.Should().BeOfType<ArgumentException>();
    }
}