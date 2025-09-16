using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace AzureToAWS.Api.Services;

public interface ITokenService
{
    string GenerateToken(string subject);
}

public class TokenService : ITokenService
{
    private readonly IJwtKeyProvider _keyProvider;

    public TokenService(IJwtKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public string GenerateToken(string subject)
    {
        var key = _keyProvider.GetSigningKey();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, subject),
                new Claim(ClaimTypes.NameIdentifier, subject)
            }),
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}