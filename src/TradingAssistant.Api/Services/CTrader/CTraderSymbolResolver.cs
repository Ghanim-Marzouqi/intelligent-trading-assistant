using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenAPI.Net;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Models.Trading;

namespace TradingAssistant.Api.Services.CTrader;

public interface ICTraderSymbolResolver
{
    Task InitializeAsync(OpenClient client, long accountId, CancellationToken ct = default);
    long GetSymbolId(string symbolName);
    string GetSymbolName(long symbolId);
    int GetDigits(long symbolId);
    int GetDigits(string symbolName);
    decimal GetContractSize(string symbolName);
    decimal GetContractSize(long symbolId);
    bool TryGetSymbolId(string symbolName, out long symbolId);
    string GetAssetName(long assetId);
    bool IsInitialized { get; }
}

public class CTraderSymbolResolver : ICTraderSymbolResolver
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CTraderSymbolResolver> _logger;

    private readonly ConcurrentDictionary<string, long> _nameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<long, string> _idToName = new();
    private readonly ConcurrentDictionary<long, int> _idToDigits = new();
    private readonly ConcurrentDictionary<long, decimal> _idToContractSize = new();
    private readonly ConcurrentDictionary<string, decimal> _nameToContractSize = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<long, string> _assetIdToName = new();

    private bool _initialized;
    public bool IsInitialized => _initialized;

    public CTraderSymbolResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<CTraderSymbolResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(OpenClient client, long accountId, CancellationToken ct = default)
    {
        if (_initialized)
            return;

        _logger.LogInformation("Initializing symbol resolver...");

        // Step 0: Fetch asset list (for deposit currency resolution)
        var assetReq = new ProtoOAAssetListReq { CtidTraderAccountId = accountId };
        await client.SendMessage(assetReq, ProtoOAPayloadType.ProtoOaAssetListReq);

        var assetRes = await client.OfType<ProtoOAAssetListRes>()
            .FirstAsync()
            .ToTask(ct);

        foreach (var asset in assetRes.Asset)
        {
            _assetIdToName[(long)asset.AssetId] = asset.Name;
        }
        _logger.LogInformation("Loaded {Count} assets from cTrader", assetRes.Asset.Count);

        // Step 1: Get light symbol list (names + IDs)
        var listReq = new ProtoOASymbolsListReq
        {
            CtidTraderAccountId = accountId
        };
        await client.SendMessage(listReq, ProtoOAPayloadType.ProtoOaSymbolsListReq);

        var listRes = await client.OfType<ProtoOASymbolsListRes>()
            .FirstAsync()
            .ToTask(ct);

        var lightSymbols = listRes.Symbol.ToList();
        _logger.LogInformation("Received {Count} symbols from cTrader", lightSymbols.Count);

        // Build name↔ID mappings from light symbols
        foreach (var ls in lightSymbols)
        {
            if (!string.IsNullOrEmpty(ls.SymbolName))
            {
                _nameToId[ls.SymbolName] = ls.SymbolId;
                _idToName[ls.SymbolId] = ls.SymbolName;
            }
        }

        // Step 2: Get detailed symbol info in batches (digits, pip size, lot size, volumes)
        var symbolIds = lightSymbols.Select(s => s.SymbolId).ToList();
        const int batchSize = 50;

        var allDetails = new List<ProtoOASymbol>();
        for (var i = 0; i < symbolIds.Count; i += batchSize)
        {
            var batch = symbolIds.Skip(i).Take(batchSize).ToList();
            var detailReq = new ProtoOASymbolByIdReq
            {
                CtidTraderAccountId = accountId
            };
            detailReq.SymbolId.AddRange(batch);

            await client.SendMessage(detailReq, ProtoOAPayloadType.ProtoOaSymbolByIdReq);

            var detailRes = await client.OfType<ProtoOASymbolByIdRes>()
                .FirstAsync()
                .ToTask(ct);

            allDetails.AddRange(detailRes.Symbol);
        }

        // Step 3: Cache digits and contract sizes, then upsert to DB
        foreach (var detail in allDetails)
        {
            _idToDigits[detail.SymbolId] = detail.Digits;
            var contractSize = detail.HasLotSize ? detail.LotSize / 100m : 100_000m;
            _idToContractSize[detail.SymbolId] = contractSize;
            if (_idToName.TryGetValue(detail.SymbolId, out var name))
                _nameToContractSize[name] = contractSize;
        }

        await UpsertSymbolsToDbAsync(lightSymbols, allDetails, ct);

        _initialized = true;
        _logger.LogInformation("Symbol resolver initialized with {Count} symbols", _nameToId.Count);
    }

    public long GetSymbolId(string symbolName)
    {
        if (_nameToId.TryGetValue(symbolName, out var id))
            return id;
        throw new KeyNotFoundException($"Symbol '{symbolName}' not found in resolver");
    }

    public string GetSymbolName(long symbolId)
    {
        if (_idToName.TryGetValue(symbolId, out var name))
            return name;
        throw new KeyNotFoundException($"Symbol ID {symbolId} not found in resolver");
    }

    public int GetDigits(long symbolId)
    {
        if (_idToDigits.TryGetValue(symbolId, out var digits))
            return digits;
        return 5; // safe default for forex
    }

    public int GetDigits(string symbolName)
    {
        if (_nameToId.TryGetValue(symbolName, out var id))
            return GetDigits(id);
        return 5;
    }

    public decimal GetContractSize(long symbolId)
    {
        if (_idToContractSize.TryGetValue(symbolId, out var cs))
            return cs;
        return 100_000m; // safe default for forex
    }

    public decimal GetContractSize(string symbolName)
    {
        if (_nameToContractSize.TryGetValue(symbolName, out var cs))
            return cs;
        return 100_000m;
    }

    public bool TryGetSymbolId(string symbolName, out long symbolId)
        => _nameToId.TryGetValue(symbolName, out symbolId);

    public string GetAssetName(long assetId)
    {
        if (_assetIdToName.TryGetValue(assetId, out var name))
            return name;
        return "USD"; // safe default
    }

    private string ResolveAssetName(long assetId)
        => _assetIdToName.TryGetValue(assetId, out var name) ? name : "";

    private async Task UpsertSymbolsToDbAsync(
        List<ProtoOALightSymbol> lightSymbols,
        List<ProtoOASymbol> details,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var detailMap = details.GroupBy(d => d.SymbolId).ToDictionary(g => g.Key, g => g.First());
        var existingSymbols = await db.Symbols.ToListAsync(ct);
        var existingByCtraderId = existingSymbols
            .Where(s => s.CTraderSymbolId > 0)
            .ToDictionary(s => s.CTraderSymbolId);

        foreach (var ls in lightSymbols)
        {
            if (string.IsNullOrEmpty(ls.SymbolName))
                continue;

            detailMap.TryGetValue(ls.SymbolId, out var detail);

            var baseCurrency = ls.HasBaseAssetId ? ResolveAssetName(ls.BaseAssetId) : "";
            var quoteCurrency = ls.HasQuoteAssetId ? ResolveAssetName(ls.QuoteAssetId) : "";

            if (existingByCtraderId.TryGetValue(ls.SymbolId, out var existing))
            {
                existing.Name = ls.SymbolName;
                existing.Description = ls.Description ?? ls.SymbolName;
                existing.BaseCurrency = baseCurrency;
                existing.QuoteCurrency = quoteCurrency;
                existing.IsActive = ls.Enabled;
                existing.UpdatedAt = DateTime.UtcNow;

                if (detail is not null)
                {
                    existing.Digits = detail.Digits;
                    existing.PipSize = (decimal)Math.Pow(10, -detail.PipPosition);
                    existing.ContractSize = detail.HasLotSize ? detail.LotSize / 100m : 100000m;
                    existing.MinVolume = detail.HasMinVolume ? detail.MinVolume : 1000m;
                    existing.MaxVolume = detail.HasMaxVolume ? detail.MaxVolume : 10_000_000m;
                    existing.VolumeStep = detail.HasStepVolume ? detail.StepVolume : 1000m;
                }
            }
            else
            {
                var symbol = new Symbol
                {
                    CTraderSymbolId = ls.SymbolId,
                    Name = ls.SymbolName,
                    Description = ls.Description ?? ls.SymbolName,
                    BaseCurrency = baseCurrency,
                    QuoteCurrency = quoteCurrency,
                    IsActive = ls.Enabled,
                    CreatedAt = DateTime.UtcNow,
                    Digits = detail?.Digits ?? 5,
                    PipSize = detail is not null
                        ? (decimal)Math.Pow(10, -detail.PipPosition)
                        : 0.0001m,
                    ContractSize = detail is not null && detail.HasLotSize
                        ? detail.LotSize / 100m
                        : 100000m,
                    MinVolume = detail is not null && detail.HasMinVolume
                        ? detail.MinVolume
                        : 1000m,
                    MaxVolume = detail is not null && detail.HasMaxVolume
                        ? detail.MaxVolume
                        : 10_000_000m,
                    VolumeStep = detail is not null && detail.HasStepVolume
                        ? detail.StepVolume
                        : 1000m
                };

                db.Symbols.Add(symbol);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Upserted {Count} symbols to database", lightSymbols.Count);
    }
}
