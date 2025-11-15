using AzureToAWS.Api.Infrastructure.Logging;
using AzureToAWS.Api.Services;
using AzureToAWS.Data.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly IUserAccessService _userAccessService;
    private readonly IJwtKeyProvider _jwtKeyProvider;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ITokenService tokenService,
        IUserAccessService userAccessService,
        IJwtKeyProvider jwtKeyProvider,
        ILogger<AuthController> logger)
    {
        _tokenService = tokenService;
        _userAccessService = userAccessService;
        _jwtKeyProvider = jwtKeyProvider;
        _logger = logger;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        var requestId = HttpContext.ResolveRequestId();
        try
        {
            _logger.WriteLog("Auth.Health", "Health check OK", requestId);
            return Ok(new { status = "ok", service = "auth", timeUtc = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.WriteLog("Auth.Health.Error", "Health check failed", requestId, success: false, exception: ex);
            return StatusCode(503, new { status = "unhealthy", service = "auth", error = ex.Message, timeUtc = DateTime.UtcNow });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var requestId = HttpContext.ResolveRequestId();
        _logger.WriteLog("Auth.Login.Attempt", $"Login attempt for key '{Obfuscate(request.AccessKey)}'", requestId);

        try
        {
            if (!await _userAccessService.ValidateCredentialsAsync(request.AccessKey, request.AccessSecret))
            {
                _logger.WriteLog("Auth.Login.Invalid", $"Invalid credentials '{Obfuscate(request.AccessKey)}'", requestId, success: false);
                return Unauthorized(new { message = "Invalid credentials", requestId });
            }

            var token = _tokenService.GenerateToken(request.AccessKey);
            var expires = DateTime.UtcNow.AddHours(24);

            _logger.WriteLog("Auth.Login.Success", $"Token issued (exp {expires:o}) '{Obfuscate(request.AccessKey)}'", requestId);
            return Ok(new LoginResponse { Token = token, Expires = expires });
        }
        catch (Exception ex)
        {
            _logger.WriteLog("Auth.Login.Error", $"Unexpected error '{Obfuscate(request.AccessKey)}'", requestId, success: false, exception: ex);
            return StatusCode(500, new { message = "Internal server error", requestId });
        }
    }

    private static string Obfuscate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length < 4
            ? "****"
            : $"{new string('*', value.Length - 4)}{value[^4..]}";
}