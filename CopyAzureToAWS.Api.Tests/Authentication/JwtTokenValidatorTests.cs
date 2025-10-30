using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace AzureToAWS.Api.Tests.Authentication;

public class JwtTokenValidatorTests : TestBase
{
    [Fact]
    public void ValidateToken_ValidToken_ReturnsTrue()
    {
        // Arrange
        var tokenKey = "your-test-secret-key-with-sufficient-length";
        var token = GenerateTestToken(tokenKey);

        // Act
        var (isValid, principal) = ValidateToken(token, tokenKey);

        // Assert
        isValid.Should().BeTrue();
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.Name)?.Value.Should().Be("test-user");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public void ValidateToken_InvalidToken_ReturnsFalse(string token)
    {
        // Arrange
        var tokenKey = "your-test-secret-key-with-sufficient-length";

        // Act
        var (isValid, principal) = ValidateToken(token, tokenKey);

        // Assert
        isValid.Should().BeFalse();
        principal.Should().BeNull();
    }

    private string GenerateTestToken(string key)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var keyBytes = Encoding.ASCII.GetBytes(key);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "test-user"),
                new Claim(ClaimTypes.Role, "test-role")
            }),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(keyBytes),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private (bool isValid, ClaimsPrincipal? principal) ValidateToken(string token, string key)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyBytes = Encoding.ASCII.GetBytes(key);
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return (true, principal);
        }
        catch
        {
            return (false, null);
        }
    }
}