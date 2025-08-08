using Microsoft.AspNetCore.Mvc;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data.DTOs;

namespace CopyAzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthController(ITokenService tokenService, IConfiguration configuration)
    {
        _tokenService = tokenService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Simple authentication - in production, use proper user management
        var validUsername = _configuration["Auth:Username"] ?? "admin";
        var validPassword = _configuration["Auth:Password"] ?? "password";

        if (request.Username != validUsername || request.Password != validPassword)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var token = _tokenService.GenerateToken(request.Username);
        var expires = DateTime.UtcNow.AddHours(24);

        return Ok(new LoginResponse
        {
            Token = token,
            Expires = expires
        });
    }
}