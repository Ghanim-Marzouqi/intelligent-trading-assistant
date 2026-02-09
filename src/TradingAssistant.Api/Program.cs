using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using TradingAssistant.Api.Data;
using TradingAssistant.Api.Hubs;
using TradingAssistant.Api.Services.Alerts;
using TradingAssistant.Api.Services.AI;
using TradingAssistant.Api.Services.CTrader;
using TradingAssistant.Api.Services.Journal;
using TradingAssistant.Api.Services.Notifications;
using TradingAssistant.Api.Services.Orders;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TradingAssistant")
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:Url"] ?? "http://localhost:5341")
    .CreateLogger();

builder.Host.UseSerilog();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
    options.InstanceName = "TradingAssistant:";
});

// OpenTelemetry Metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("TradingAssistant.Api"))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();
    });

// SignalR
builder.Services.AddSignalR();

// Controllers
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

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "redis:6379");

// Application Services
builder.Services.AddSingleton<ICTraderAuthService, CTraderAuthService>();
builder.Services.AddHostedService<CTraderApiAdapter>();
builder.Services.AddSingleton<ICTraderPriceStream, CTraderPriceStream>();
builder.Services.AddSingleton<ICTraderAccountStream, CTraderAccountStream>();
builder.Services.AddScoped<ICTraderOrderExecutor, CTraderOrderExecutor>();

builder.Services.AddHostedService<AlertEngine>();
builder.Services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();

builder.Services.AddScoped<ITradeJournalService, TradeJournalService>();
builder.Services.AddScoped<ITradeEnricher, TradeEnricher>();
builder.Services.AddScoped<IAnalyticsAggregator, AnalyticsAggregator>();

builder.Services.AddScoped<IOrderManager, OrderManager>();
builder.Services.AddScoped<IPositionSizer, PositionSizer>();
builder.Services.AddScoped<IRiskGuard, RiskGuard>();

builder.Services.AddScoped<IAiAnalysisService, OpenCodeZenService>();

builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();

// HTTP Clients with Polly resilience
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>()
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<IAiAnalysisService, OpenCodeZenService>()
    .AddStandardResilienceHandler();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("TradingUI");

app.MapControllers();
app.MapHub<TradingHub>("/hub");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

Log.Information("Trading Assistant API started");
await app.RunAsync();
