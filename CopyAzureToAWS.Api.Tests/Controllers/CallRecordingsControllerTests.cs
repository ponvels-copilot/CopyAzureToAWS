using Amazon.SQS;
using Amazon.SQS.Model;
using AzureToAWS.Api.Controllers;
using AzureToAWS.Data;
using AzureToAWS.Data.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AzureToAWS.Api.Tests.Controllers;

public class CallRecordingsControllerTests : TestBase
{
    private readonly Mock<ILogger<CallRecordingsController>> _loggerMock;
    private readonly Mock<IAmazonSQS> _sqsMock;
    private readonly Mock<ApplicationDbContext> _dbContextMock;
    private readonly CallRecordingsController _sut;

    public CallRecordingsControllerTests()
    {
        _loggerMock = new Mock<ILogger<CallRecordingsController>>();
        _sqsMock = new Mock<IAmazonSQS>();
        _dbContextMock = new Mock<ApplicationDbContext>();

        _sut = new CallRecordingsController(
            _loggerMock.Object,
            _sqsMock.Object,
            _dbContextMock.Object);
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsAccepted()
    {
        // Arrange
        var request = new AzureToAWSRequest
        {
            CallDetailID = 123,
            AudioFile = "test.wav",
            CountryCode = "US"
        };

        _sqsMock
            .Setup(x => x.SendMessageAsync(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse());

        // Act
        var result = await _sut.Post(request);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        _sqsMock.Verify(
            x => x.SendMessageAsync(
                It.Is<SendMessageRequest>(r => r.QueueUrl.Contains("azure-to-aws")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null, "test.wav", "US")]
    [InlineData(123, null, "US")]
    [InlineData(123, "test.wav", null)]
    [InlineData(123, "", "US")]
    [InlineData(123, "test.wav", "")]
    public async Task Post_InvalidRequest_ReturnsBadRequest(long? callDetailId, string audioFile, string countryCode)
    {
        // Arrange
        var request = new AzureToAWSRequest
        {
            CallDetailID = (long)callDetailId,
            AudioFile = audioFile,
            CountryCode = countryCode
        };

        // Act
        var result = await _sut.Post(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Post_WhenSqsFails_ReturnsInternalServerError()
    {
        // Arrange
        var request = new AzureToAWSRequest
        {
            CallDetailID = 123,
            AudioFile = "test.wav",
            CountryCode = "US"
        };

        _sqsMock
            .Setup(x => x.SendMessageAsync(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Test error"));

        // Act
        var result = await _sut.Post(request);

        // Assert
        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(500);
    }
}