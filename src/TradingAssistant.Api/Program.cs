using Microsoft.EntityFrameworkCore;
using Serilog;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Services.Alerts;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Journal;
using TradingAssistant.Api.Services.Notifications;
using TradingAssistant.Api.Services.Orders;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Database - PostgreSQL for all environments
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Cache - Redis for all environments
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "TradingAssistant:";
});

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddRedis(redisConnectionString);

// SignalR
builder.Services.AddSignalR();

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("TradingUI", policy =>
    {
        policy
            .WithOrigins(builder.Configuration["Cors:AllowedOrigins"]?.Split(',') ?? ["http://localhost:4200"])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// cTrader Services
builder.Services.AddSingleton<ICTraderAuthService, CTraderAuthService>();
builder.Services.AddHostedService<CTraderApiAdapter>();
builder.Services.AddSingleton<ICTraderPriceStream, CTraderPriceStream>();
builder.Services.AddSingleton<ICTraderAccountStream, CTraderAccountStream>();
builder.Services.AddScoped<ICTraderOrderExecutor, CTraderOrderExecutor>();

// Alert Services
builder.Services.AddSingleton<AlertEngine>();
builder.Services.AddHostedService<AlertEngine>(sp => sp.GetRequiredService<AlertEngine>());
builder.Services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();

// Journal Services
builder.Services.AddScoped<ITradeJournalService, TradeJournalService>();
builder.Services.AddScoped<ITradeEnricher, TradeEnricher>();
builder.Services.AddScoped<IAnalyticsAggregator, AnalyticsAggregator>();

// Order Services
builder.Services.AddScoped<IOrderManager, OrderManager>();
builder.Services.AddScoped<IPositionSizer, PositionSizer>();
builder.Services.AddScoped<IRiskGuard, RiskGuard>();

// AI Services
builder.Services.AddHttpClient<IAiAnalysisService, OpenCodeZenService>();

// Notification Services
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.UseCors("TradingUI");

app.MapControllers();
app.MapHub<TradingHub>("/hub");
app.MapHealthChecks("/health");
app.MapMetrics();

// Auto-create database in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

Log.Information("Trading Assistant API started");
await app.RunAsync();
