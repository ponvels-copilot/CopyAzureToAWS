using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace AzureToAWS.Api.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task CallRecordings_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/callrecordings", null);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CallRecordings_WithAuth_AcceptsValidRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "your-test-token");

        var content = new StringContent(
            """
            {
                "callDetailId": 123,
                "audioFile": "test.wav",
                "countryCode": "US"
            }
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/callrecordings", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
    }
}