using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Journal;
using TradingAssistant.Api.Services.CTrader;

namespace TradingAssistant.Api.Services.Journal;

public interface ITradeJournalService
{
    Task RecordTradeAsync(PositionEventArgs positionEvent);
    Task<TradeEntry?> GetTradeAsync(long id);
    Task AddTagAsync(long tradeId, string tag);
    Task AddNoteAsync(long tradeId, string note);
}

public class TradeJournalService : ITradeJournalService
{
    private readonly AppDbContext _db;
    private readonly ITradeEnricher _enricher;
    private readonly IAnalyticsAggregator _analytics;
    private readonly ILogger<TradeJournalService> _logger;

    public TradeJournalService(
        AppDbContext db,
        ITradeEnricher enricher,
        IAnalyticsAggregator analytics,
        ILogger<TradeJournalService> logger)
    {
        _db = db;
        _enricher = enricher;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task RecordTradeAsync(PositionEventArgs positionEvent)
    {
        _logger.LogInformation("Recording trade: {Symbol} {Direction}",
            positionEvent.Symbol, positionEvent.Direction);

        var entry = new TradeEntry
        {
            PositionId = positionEvent.PositionId,
            Symbol = positionEvent.Symbol,
            Direction = positionEvent.Direction,
            Volume = positionEvent.Volume,
            EntryPrice = positionEvent.EntryPrice,
            ExitPrice = positionEvent.CurrentPrice,
            StopLoss = positionEvent.StopLoss,
            TakeProfit = positionEvent.TakeProfit,
            PnL = positionEvent.PnL,
            CloseTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        // Enrich with calculated metrics
        await _enricher.EnrichAsync(entry);

        _db.TradeEntries.Add(entry);
        await _db.SaveChangesAsync();

        // Update aggregated analytics
        await _analytics.UpdateDailyStatsAsync(entry);

        _logger.LogInformation("Trade recorded: {TradeId} with PnL {PnL}",
            entry.Id, entry.NetPnL);
    }

    public async Task<TradeEntry?> GetTradeAsync(long id)
    {
        return await _db.TradeEntries.FindAsync(id);
    }

    public async Task AddTagAsync(long tradeId, string tag)
    {
        var tradeTag = new TradeTag
        {
            TradeEntryId = tradeId,
            Name = tag,
            CreatedAt = DateTime.UtcNow
        };

        _db.TradeTags.Add(tradeTag);
        await _db.SaveChangesAsync();
    }

    public async Task AddNoteAsync(long tradeId, string note)
    {
        var tradeNote = new TradeNote
        {
            TradeEntryId = tradeId,
            Content = note,
            CreatedAt = DateTime.UtcNow
        };

        _db.TradeNotes.Add(tradeNote);
        await _db.SaveChangesAsync();
    }
}
