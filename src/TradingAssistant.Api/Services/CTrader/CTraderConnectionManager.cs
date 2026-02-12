using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using OpenAPI.Net;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderConnectionManager
{
    Task<OpenClient> GetClientAsync(CancellationToken ct = default);
    long AccountId { get; }
    Task ReconnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}

public class CTraderConnectionManager : ICTraderConnectionManager, IDisposable
{
    private readonly IConfiguration _config;
    private readonly ICTraderAuthService _authService;
    private readonly ILogger<CTraderConnectionManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private OpenClient? _client;
    private IDisposable? _errorSubscription;
    private bool _isConnected;
    private long _accountId;

    public long AccountId => _accountId;

    public CTraderConnectionManager(
        IConfiguration config,
        ICTraderAuthService authService,
        ILogger<CTraderConnectionManager> logger)
    {
        _config = config;
        _authService = authService;
        _logger = logger;
    }

    public async Task<OpenClient> GetClientAsync(CancellationToken ct = default)
    {
        if (_isConnected && _client is not null)
            return _client;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_isConnected && _client is not null)
                return _client;

            await ConnectInternalAsync(ct);
            return _client!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            await DisconnectInternalAsync();
            await ConnectInternalAsync(ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        var host = _config["CTrader:ApiHost"] ?? "demo.ctraderapi.com";
        var port = int.Parse(_config["CTrader:ApiPort"] ?? "5035");
        var clientId = _config["CTrader:ClientId"] ?? "";
        var clientSecret = _config["CTrader:ClientSecret"] ?? "";
        var configAccountId = long.Parse(_config["CTrader:AccountId"] ?? "0");

        // Step 0: Verify we have OAuth tokens BEFORE opening the WebSocket
        // This avoids a wasteful connect → app-auth → fail → disconnect cycle
        var accessToken = await _authService.GetAccessTokenAsync(ct);

        _logger.LogInformation("Connecting to cTrader at {Host}:{Port}", host, port);

        _client = new OpenClient(host, port, TimeSpan.FromSeconds(10), useWebSocket: true);

        _errorSubscription = _client.OfType<ProtoOAErrorRes>().Subscribe(error =>
        {
            _logger.LogError("cTrader API error: {ErrorCode} - {Description}",
                error.ErrorCode, error.Description);
        });

        await _client.Connect();
        _logger.LogInformation("WebSocket connected to cTrader");

        // Step 1: Application auth
        var appAuthReq = new ProtoOAApplicationAuthReq
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        await _client.SendMessage(appAuthReq, ProtoOAPayloadType.ProtoOaApplicationAuthReq);

        var appAuthRes = await _client.OfType<ProtoOAApplicationAuthRes>()
            .FirstAsync()
            .ToTask(ct);
        _logger.LogInformation("Application authenticated with cTrader");

        // Step 2: Discover accounts via API
        // The configured AccountId may be a login number, not the ctidTraderAccountId.
        // Always fetch the account list and match accordingly.
        var accountListReq = new ProtoOAGetAccountListByAccessTokenReq
        {
            AccessToken = accessToken
        };
        await _client.SendMessage(accountListReq,
            ProtoOAPayloadType.ProtoOaGetAccountsByAccessTokenReq);

        var accountListRes = await _client.OfType<ProtoOAGetAccountListByAccessTokenRes>()
            .FirstAsync()
            .ToTask(ct);

        if (accountListRes.CtidTraderAccount.Count == 0)
            throw new InvalidOperationException("No trading accounts found for this access token");

        foreach (var acct in accountListRes.CtidTraderAccount)
        {
            _logger.LogInformation(
                "Found cTrader account: ctidTraderAccountId={CtidId}, traderLogin={Login}, isLive={IsLive}",
                acct.CtidTraderAccountId,
                acct.HasTraderLogin ? acct.TraderLogin : 0,
                acct.IsLive);
        }

        if (configAccountId > 0)
        {
            // Try matching as ctidTraderAccountId first, then as traderLogin
            var matched = accountListRes.CtidTraderAccount.FirstOrDefault(
                a => (long)a.CtidTraderAccountId == configAccountId);
            matched ??= accountListRes.CtidTraderAccount.FirstOrDefault(
                a => a.HasTraderLogin && a.TraderLogin == configAccountId);

            if (matched is not null)
            {
                _accountId = (long)matched.CtidTraderAccountId;
            }
            else
            {
                _logger.LogWarning(
                    "Configured AccountId={ConfigId} not found in account list. Using first available account.",
                    configAccountId);
                _accountId = (long)accountListRes.CtidTraderAccount[0].CtidTraderAccountId;
            }
        }
        else
        {
            _accountId = (long)accountListRes.CtidTraderAccount[0].CtidTraderAccountId;
        }

        // Step 3: Account auth
        var accountAuthReq = new ProtoOAAccountAuthReq
        {
            CtidTraderAccountId = _accountId,
            AccessToken = accessToken
        };
        await _client.SendMessage(accountAuthReq, ProtoOAPayloadType.ProtoOaAccountAuthReq);

        var accountAuthRes = await _client.OfType<ProtoOAAccountAuthRes>()
            .FirstAsync()
            .ToTask(ct);

        _isConnected = true;
        _logger.LogInformation("Connected to cTrader. AccountId={AccountId}", _accountId);
    }

    private Task DisconnectInternalAsync()
    {
        _isConnected = false;
        _errorSubscription?.Dispose();
        _errorSubscription = null;

        if (_client is not null)
        {
            _client.Dispose();
            _client = null;
        }

        _logger.LogInformation("Disconnected from cTrader");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _errorSubscription?.Dispose();
        _client?.Dispose();
        _semaphore.Dispose();
    }
}
