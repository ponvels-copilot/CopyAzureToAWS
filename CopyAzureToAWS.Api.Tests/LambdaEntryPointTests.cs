using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using System.Text.Json;

namespace AzureToAWS.Api.Tests;

public class LambdaEntryPointTests : TestBase
{
    private readonly LambdaEntryPoint _sut;

    public LambdaEntryPointTests()
    {
        _sut = new LambdaEntryPoint();
    }

    [Fact]
    public async Task FunctionHandlerAsync_HealthCheck_ReturnsOk()
    {
        // Arrange
        var request = new APIGatewayProxyRequest
        {
            HttpMethod = "GET",
            Path = "/health",
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                RequestId = Guid.NewGuid().ToString()
            }
        };

        // Act
        var response = await _sut.FunctionHandlerAsync(request, Context);

        // Assert
        response.StatusCode.Should().Be(200);
    }

    //[Fact]
    //public async Task Init_ConfiguresWebHost_Successfully()
    //{
    //    // Act
    //    var webHost = _sut.Init(null);

    //    // Assert
    //    webHost.Should().NotBeNull();
    //}
}