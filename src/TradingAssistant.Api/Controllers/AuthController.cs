using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TradingAssistant.Api.Services.CTrader;
using Microsoft.Extensions.Hosting;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly ICTraderAuthService _authService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;
    private readonly IHostEnvironment _env;

    public AuthController(
        ICTraderAuthService authService,
        IConfiguration config,
        ILogger<AuthController> logger,
        IHostEnvironment env)
    {
        _authService = authService;
        _config = config;
        _logger = logger;
        _env = env;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var configuredPassword = _config["Auth:Password"] ?? "";

        // If no password is configured, check environment
        if (string.IsNullOrEmpty(configuredPassword))
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning("No Auth:Password configured, allowing login without password (Development only)");
            }
            else
            {
                _logger.LogCritical("No Auth:Password configured. Denying access in non-development environment.");
                return Unauthorized(new { error = "Server misconfiguration" });
            }
        }
        else if (!CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(request.Password ?? ""),
                     Encoding.UTF8.GetBytes(configuredPassword)))
        {
            return Unauthorized(new { error = "Invalid password" });
        }

        var token = GenerateJwtToken();
        return Ok(new { token });
    }

    [HttpGet("ctrader")]
    public IActionResult RedirectToCTrader()
    {
        var clientId = _config["CTrader:ClientId"] ?? "";
        var redirectUri = _config["CTrader:RedirectUri"] ?? "http://localhost:5000/api/auth/ctrader/callback";

        var authUrl = $"https://openapi.ctrader.com/apps/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=trading";

        _logger.LogInformation("Redirecting to cTrader OAuth: {Url}", authUrl);
        return Redirect(authUrl);
    }

    [HttpGet("ctrader/callback")]
    public async Task<IActionResult> CTraderCallback([FromQuery] string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            return BadRequest("Missing authorization code");

        _logger.LogInformation("Received cTrader OAuth callback with code");

        try
        {
            await _authService.ExchangeCodeAsync(code, ct);
            return Ok(new { message = "cTrader authorization successful. Tokens stored. The trading service will connect automatically." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange cTrader authorization code");
            return StatusCode(500, new { error = "Failed to exchange authorization code", detail = ex.Message });
        }
    }

    [HttpGet("ctrader/status")]
    public IActionResult TokenStatus()
    {
        return Ok(new
        {
            isValid = _authService.IsTokenValid,
            authUrl = Url.Action(nameof(RedirectToCTrader))
        });
    }

    private string GenerateJwtToken()
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret configuration is required");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryHours = _config.GetValue<int>("Jwt:ExpiryHours", 24);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "trader"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "TradingAssistant",
            audience: _config["Jwt:Audience"] ?? "TradingAssistantUI",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string? Password);
