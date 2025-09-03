using Microsoft.AspNetCore.Mvc;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data.DTOs;
using Microsoft.AspNetCore.Authorization;

namespace CopyAzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly IUserAccessService _userAccessService;
    private readonly IJwtKeyProvider _jwtKeyProvider;

    public AuthController(ITokenService tokenService, IUserAccessService userAccessService, IJwtKeyProvider jwtKeyProvider)
    {
        _tokenService = tokenService;
        _userAccessService = userAccessService;
        _jwtKeyProvider = jwtKeyProvider;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        try
        {
            return Ok(new { status = "ok", service = "auth", timeUtc = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "unhealthy", service = "auth", error = ex.Message, timeUtc = DateTime.UtcNow });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!await _userAccessService.ValidateCredentialsAsync(request.AccessKey, request.AccessSecret))
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var token = _tokenService.GenerateToken(request.AccessKey);
        var expires = DateTime.UtcNow.AddHours(24);

        return Ok(new LoginResponse
        {
            Token = token,
            Expires = expires
        });
    }
}