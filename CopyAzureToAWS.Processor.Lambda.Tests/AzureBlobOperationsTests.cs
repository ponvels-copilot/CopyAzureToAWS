using FluentAssertions;
using Moq;
using Xunit;
using AzureToAWS.Common.Utilities;
using AzureToAWS.Data.Models;
using AzureToAWS.Data.DTOs;

namespace AzureToAWS.Processor.Lambda.Tests;

public class AzureBlobOperationsTests : TestBase
{
    [Fact]
    public async Task GetAzureStreamAsync_WhenSuccessful_ReturnsStream()
    {
        // Arrange
        var callDetails = new CallDetailInfo
        {
            CallDetailID = 123,
            AudioFile = "test.wav",
            AudioFileLocation = "container/path"
        };

        var storageConfig = new StorageAZURE
        {
            MSAzureBlob = new()
            {
                EndPoint = "test-endpoint",
                AccountName = "test-account",
                AccountKey = "test-key",
                ConnectionString = "test-ConnectionString"
            }
        };

        // Act
        var (stream, exception) = await Function.GetAzureStreamAsync(callDetails, storageConfig);

        // Assert
        exception.Should().BeNull();
        stream.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, "test.wav")]
    [InlineData("location", null)]
    public async Task GetAzureStreamAsync_WithInvalidInputs_ReturnsError(string location, string fileName)
    {
        // Arrange
        var callDetails = new CallDetailInfo
        {
            CallDetailID = 123,
            AudioFile = fileName,
            AudioFileLocation = location
        };

        var storageConfig = new StorageAZURE
        {
            MSAzureBlob = new()
            {
                EndPoint = "test-endpoint",
                AccountName = "test-account",
                AccountKey = "test-key",
                ConnectionString = "test-ConnectionString"
            }
        };

        // Act
        var (stream, exception) = await Function.GetAzureStreamAsync(callDetails, storageConfig);

        // Assert
        exception.Should().NotBeNull();
        stream.Should().BeNull();
    }
}