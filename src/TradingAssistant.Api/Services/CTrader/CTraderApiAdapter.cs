namespace TradingAssistant.Api.Services.CTrader;

public class CTraderApiAdapter : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ICTraderAuthService _authService;
    private readonly ICTraderPriceStream _priceStream;
    private readonly ICTraderAccountStream _accountStream;
    private readonly ILogger<CTraderApiAdapter> _logger;
    private readonly ReconnectionPolicy _reconnectionPolicy;

    public CTraderApiAdapter(
        IConfiguration config,
        ICTraderAuthService authService,
        ICTraderPriceStream priceStream,
        ICTraderAccountStream accountStream,
        ILogger<CTraderApiAdapter> logger)
    {
        _config = config;
        _authService = authService;
        _priceStream = priceStream;
        _accountStream = accountStream;
        _logger = logger;
        _reconnectionPolicy = new ReconnectionPolicy();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("cTrader API Adapter starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var token = await _authService.GetAccessTokenAsync(stoppingToken);

                _logger.LogInformation("Connecting to cTrader API...");

                // TODO: Establish gRPC connection to cTrader Open API
                // Host: demo.ctraderapi.com or live.ctraderapi.com
                // Port: 5035

                await _priceStream.StartAsync(stoppingToken);
                await _accountStream.StartAsync(stoppingToken);

                _reconnectionPolicy.Reset();

                // Keep alive until cancelled or disconnected
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "cTrader connection error");

                var delay = _reconnectionPolicy.GetNextDelay();
                _logger.LogWarning("Reconnecting in {Delay}...", delay);

                await Task.Delay(delay, stoppingToken);
            }
        }

        _logger.LogInformation("cTrader API Adapter stopped");
    }
}

public class ReconnectionPolicy
{
    private int _attemptCount;
    private readonly int _maxDelaySeconds = 60;

    public TimeSpan GetNextDelay()
    {
        var delay = Math.Min(Math.Pow(2, _attemptCount), _maxDelaySeconds);
        _attemptCount++;
        return TimeSpan.FromSeconds(delay);
    }

    public void Reset() => _attemptCount = 0;
}
