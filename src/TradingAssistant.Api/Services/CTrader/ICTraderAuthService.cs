using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderAuthService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default);
    Task ExchangeCodeAsync(string authCode, CancellationToken cancellationToken = default);
    bool IsTokenValid { get; }
}

public class CTraderAuthService : ICTraderAuthService
{
    private const string TokenEndpoint = "https://openapi.ctrader.com/apps/token";

    private readonly IConfiguration _config;
    private readonly ILogger<CTraderAuthService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry;

    public CTraderAuthService(
        IConfiguration config,
        ILogger<CTraderAuthService> logger,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _scopeFactory = scopeFactory;
    }

    public bool IsTokenValid => !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsTokenValid)
            return _accessToken!;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsTokenValid)
                return _accessToken!;

            // Try loading from DB if not in memory
            if (string.IsNullOrEmpty(_refreshToken))
            {
                await LoadFromDbAsync(cancellationToken);
            }

            // If we have a refresh token, use it
            if (!string.IsNullOrEmpty(_refreshToken))
            {
                return await RefreshTokenInternalAsync(cancellationToken);
            }

            // No tokens at all — log the auth URL
            var clientId = _config["CTrader:ClientId"] ?? "";
            var redirectUri = _config["CTrader:RedirectUri"] ?? "http://localhost:5000/api/auth/ctrader/callback";
            _logger.LogWarning(
                "No cTrader OAuth tokens found. Authorize at: http://localhost:5000/api/auth/ctrader " +
                "(ClientId={ClientId}, RedirectUri={RedirectUri})",
                clientId, redirectUri);

            throw new InvalidOperationException(
                "No cTrader OAuth tokens available. Visit /api/auth/ctrader to authorize.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await RefreshTokenInternalAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ExchangeCodeAsync(string authCode, CancellationToken cancellationToken = default)
    {
        var clientId = _config["CTrader:ClientId"] ?? "";
        var clientSecret = _config["CTrader:ClientSecret"] ?? "";
        var redirectUri = _config["CTrader:RedirectUri"] ?? "http://localhost:5000/api/auth/ctrader/callback";

        _logger.LogInformation("Exchanging authorization code for tokens");

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri
        };

        var response = await _httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(parameters),
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token exchange failed: {StatusCode} {Body}", response.StatusCode, json);
            throw new InvalidOperationException($"Token exchange failed: {response.StatusCode} - {json}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Invalid token response from cTrader");

        _accessToken = tokenResponse.AccessToken;
        _refreshToken = tokenResponse.RefreshToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 2592000);

        await SaveToDbAsync(cancellationToken);

        _logger.LogInformation("cTrader tokens stored. Expires at {Expiry}", _tokenExpiry);
    }

    private async Task<string> RefreshTokenInternalAsync(CancellationToken cancellationToken)
    {
        var clientId = _config["CTrader:ClientId"] ?? "";
        var clientSecret = _config["CTrader:ClientSecret"] ?? "";

        _logger.LogInformation("Refreshing cTrader OAuth2 token");

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken!,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        var response = await _httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(parameters),
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token refresh failed: {StatusCode} {Body}", response.StatusCode, json);
            throw new InvalidOperationException($"Token refresh failed: {response.StatusCode} - {json}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Invalid refresh token response from cTrader");

        _accessToken = tokenResponse.AccessToken;
        if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            _refreshToken = tokenResponse.RefreshToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 2592000);

        await SaveToDbAsync(cancellationToken);

        _logger.LogInformation("cTrader token refreshed. Expires at {Expiry}", _tokenExpiry);
        return _accessToken;
    }

    private async Task LoadFromDbAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var token = await db.CTraderTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);

            if (token is not null)
            {
                _accessToken = token.AccessToken;
                _refreshToken = token.RefreshToken;
                _tokenExpiry = token.ExpiresAt;
                _logger.LogInformation("Loaded cTrader tokens from database. Expires at {Expiry}", _tokenExpiry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tokens from database (may not be migrated yet)");
        }
    }

    private async Task SaveToDbAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await db.CTraderTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.AccessToken = _accessToken!;
                existing.RefreshToken = _refreshToken!;
                existing.ExpiresAt = _tokenExpiry;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.CTraderTokens.Add(new CTraderToken
                {
                    AccessToken = _accessToken!,
                    RefreshToken = _refreshToken!,
                    ExpiresAt = _tokenExpiry,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save tokens to database");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private class TokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public string? TokenType { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorDescription { get; set; }
    }
}
