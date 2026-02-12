using Microsoft.AspNetCore.Mvc;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ICTraderAuthService _authService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ICTraderAuthService authService,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _config = config;
        _logger = logger;
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
}
