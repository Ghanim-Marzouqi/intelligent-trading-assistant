using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Tests.AI;

public class OpenCodeZenServiceTests
{
    private readonly Mock<ICTraderPriceStream> _priceStream = new();
    private readonly Mock<ICTraderHistoricalData> _historicalData = new();
    private readonly Mock<IPositionSizer> _positionSizer = new();
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public OpenCodeZenServiceTests()
    {
        _db = TestDbContextFactory.Create();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProvider:ApiKey"] = "test-key",
                ["AiProvider:BaseUrl"] = "https://api.test.com/v1",
                ["AiProvider:Model"] = "test-model"
            })
            .Build();

        // Default: historical data returns empty candle list
        _historicalData.Setup(h => h.GetCandlesAsync(
                It.IsAny<string>(), It.IsAny<ProtoOATrendbarPeriod>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Candle>());
    }

    private OpenCodeZenService CreateService(HttpClient httpClient)
    {
        return new OpenCodeZenService(
            httpClient,
            _config,
            _db,
            NullLogger<OpenCodeZenService>.Instance,
            _priceStream.Object,
            _historicalData.Object,
            _positionSizer.Object);
    }

    private static HttpClient CreateMockHttpClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseJson, statusCode);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task AnalyzeMarketAsync_UnknownSymbol_ReturnsAnalysisWithZeroLevels()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("INVALID")).Returns(false);

        var aiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            pair = "INVALID",
                            bias = "neutral",
                            confidence = 0.1,
                            key_levels = new { support = 0, resistance = 0 },
                            risk_events = new[] { "Unknown symbol" },
                            recommendation = "wait",
                            reasoning = "Symbol not recognized"
                        })
                    }
                }
            }
        });

        var httpClient = CreateMockHttpClient(aiResponse);
        var service = CreateService(httpClient);

        var result = await service.AnalyzeMarketAsync("INVALID");

        // Support/resistance are overridden with computed values (0 for unknown symbols)
        Assert.Equal(0m, result.KeyLevels.Support);
        Assert.Equal(0m, result.KeyLevels.Resistance);
    }

    [Fact]
    public async Task AnalyzeMarketAsync_WithPriceData_OverridesKeyLevels()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD"))
            .Returns((1.18600m, 1.18610m));

        // Return enough history for Bollinger (20+)
        var history = Enumerable.Range(0, 25)
            .Select(i => 1.18500m + i * 0.0001m)
            .ToList();
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD"))
            .Returns(history);

        var aiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            pair = "EURUSD",
                            bias = "bullish",
                            confidence = 0.7,
                            key_levels = new { support = 999, resistance = 999 },
                            risk_events = new[] { "ECB meeting" },
                            recommendation = "buy",
                            reasoning = "Strong uptrend"
                        })
                    }
                }
            }
        });

        var httpClient = CreateMockHttpClient(aiResponse);
        var service = CreateService(httpClient);

        var result = await service.AnalyzeMarketAsync("EURUSD");

        Assert.Equal("EURUSD", result.Pair);
        Assert.Equal("bullish", result.Bias);
        Assert.Equal("buy", result.Recommendation);
        // Key levels should be overridden with computed values, NOT the LLM's 999
        Assert.NotEqual(999m, result.KeyLevels.Support);
        Assert.NotEqual(999m, result.KeyLevels.Resistance);
        Assert.True(result.KeyLevels.Support > 0);
        Assert.True(result.KeyLevels.Resistance > 0);
    }

    [Fact]
    public async Task AnalyzeMarketAsync_NoPriceAfterWait_ReturnsDefaultAnalysis()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD")).Returns((ValueTuple<decimal, decimal>?)null);
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD")).Returns(new List<decimal>());

        var aiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            pair = "EURUSD",
                            bias = "neutral",
                            confidence = 0.0,
                            key_levels = new { support = 0, resistance = 0 },
                            risk_events = Array.Empty<string>(),
                            recommendation = "wait",
                            reasoning = "No data"
                        })
                    }
                }
            }
        });

        var httpClient = CreateMockHttpClient(aiResponse);
        var service = CreateService(httpClient);

        // This will wait up to the configured timeout but we've mocked null prices
        // so it should return quickly with zero levels
        var result = await service.AnalyzeMarketAsync("EURUSD");

        Assert.Equal(0m, result.KeyLevels.Support);
        Assert.Equal(0m, result.KeyLevels.Resistance);
    }

    [Fact]
    public async Task AnalyzeMarketAsync_MalformedAiResponse_ReturnsDefaultWithReasoning()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD"))
            .Returns((1.18600m, 1.18610m));
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD"))
            .Returns(new List<decimal> { 1.18600m });

        // AI returns invalid JSON content
        var aiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = "This is not valid JSON at all" }
                }
            }
        });

        var httpClient = CreateMockHttpClient(aiResponse);
        var service = CreateService(httpClient);

        var result = await service.AnalyzeMarketAsync("EURUSD");

        Assert.Equal("EURUSD", result.Pair);
        Assert.Equal("This is not valid JSON at all", result.Reasoning);
        Assert.True(result.KeyLevels.Support > 0);
    }

    [Fact]
    public async Task AnalyzeMarketAsync_ApiError_Throws()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD"))
            .Returns((1.18600m, 1.18610m));
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD"))
            .Returns(new List<decimal> { 1.18600m });

        var httpClient = CreateMockHttpClient("Internal Server Error", HttpStatusCode.InternalServerError);
        var service = CreateService(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.AnalyzeMarketAsync("EURUSD"));
    }

    [Fact]
    public async Task EnrichAlertAsync_IncludesMarketData()
    {
        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD"))
            .Returns((1.18600m, 1.18610m));
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD"))
            .Returns(new List<decimal> { 1.18600m, 1.18590m, 1.18610m });

        string? capturedPrompt = null;
        var aiResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = "Price near support level with bullish divergence." }
                }
            }
        });

        var handler = new CaptureHttpMessageHandler(aiResponse, p => capturedPrompt = p);
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.EnrichAlertAsync("EURUSD", 1.18600m, "Price crossed below support");

        Assert.Equal("Price near support level with bullish divergence.", result);
        // Verify the prompt sent to AI includes live market data
        Assert.NotNull(capturedPrompt);
        Assert.Contains("LIVE MARKET DATA", capturedPrompt);
        Assert.Contains("1.18600", capturedPrompt);
    }

    [Fact]
    public async Task NotConfigured_ThrowsOnApiCall()
    {
        var noKeyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiProvider:BaseUrl"] = "https://api.test.com/v1"
                // No ApiKey
            })
            .Build();

        _priceStream.Setup(p => p.IsKnownSymbol("EURUSD")).Returns(true);
        _priceStream.Setup(p => p.GetCurrentPrice("EURUSD"))
            .Returns((1.18600m, 1.18610m));
        _priceStream.Setup(p => p.GetPriceHistory("EURUSD"))
            .Returns(new List<decimal> { 1.18600m });

        var httpClient = CreateMockHttpClient("{}");
        var service = new OpenCodeZenService(
            httpClient, noKeyConfig, _db,
            NullLogger<OpenCodeZenService>.Instance,
            _priceStream.Object,
            _historicalData.Object,
            _positionSizer.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnalyzeMarketAsync("EURUSD"));
    }
}

/// <summary>
/// Mock HTTP handler that returns a fixed response.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _response = response;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Mock HTTP handler that captures the request body and returns a fixed response.
/// </summary>
internal class CaptureHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly Action<string> _capturePrompt;

    public CaptureHttpMessageHandler(string response, Action<string> capturePrompt)
    {
        _response = response;
        _capturePrompt = capturePrompt;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            _capturePrompt(body);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        };
    }
}
