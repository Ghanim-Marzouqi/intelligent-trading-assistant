using System.Threading.Channels;

namespace TradingAssistant.Api.Services;

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem, string description);
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<QueuedWorkItem> _channel = Channel.CreateUnbounded<QueuedWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    public async ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem, string description)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _channel.Writer.WriteAsync(new QueuedWorkItem(workItem, description));
    }

    internal ChannelReader<QueuedWorkItem> Reader => _channel.Reader;
}

internal record QueuedWorkItem(Func<IServiceProvider, CancellationToken, Task> WorkItem, string Description);

public class BackgroundTaskProcessor : BackgroundService
{
    private readonly BackgroundTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundTaskProcessor> _logger;

    public BackgroundTaskProcessor(
        BackgroundTaskQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundTaskProcessor> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task processor started");

        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogDebug("Executing background task: {Description}", item.Description);

                using var scope = _scopeFactory.CreateScope();
                await item.WorkItem(scope.ServiceProvider, stoppingToken);

                _logger.LogDebug("Background task completed: {Description}", item.Description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task failed: {Description}", item.Description);
            }
        }
    }
}
