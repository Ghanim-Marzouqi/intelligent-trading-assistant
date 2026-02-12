namespace TradingAssistant.Api.Models.Trading;

public class Symbol
{
    public long Id { get; set; }
    public long CTraderSymbolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public int Digits { get; set; }
    public decimal PipSize { get; set; }
    public decimal ContractSize { get; set; }
    public decimal MinVolume { get; set; }
    public decimal MaxVolume { get; set; }
    public decimal VolumeStep { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
