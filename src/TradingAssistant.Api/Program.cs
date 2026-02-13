using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret))
{
    Log.Fatal("Jwt:Secret is missing from configuration.");
    throw new InvalidOperationException("Jwt:Secret configuration is required");
}

var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var validIssuer = builder.Configuration["Jwt:Issuer"];
    var validAudience = builder.Configuration["Jwt:Audience"];

    if (!builder.Environment.IsDevelopment()) 
    {
        if (string.IsNullOrEmpty(validIssuer)) throw new InvalidOperationException("Jwt:Issuer is required in non-development environments");
        if (string.IsNullOrEmpty(validAudience)) throw new InvalidOperationException("Jwt:Audience is required in non-development environments");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = validIssuer ?? "TradingAssistant",
        ValidAudience = validAudience ?? "TradingAssistantUI",
        IssuerSigningKey = jwtKey
    };

    // Support token from query string for SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Data Protection — persist keys so tokens survive container restarts
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/app/keys";
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
    .SetApplicationName("TradingAssistant");

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global fixed window: 100 requests per minute per IP
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });

    // Stricter limit for auth endpoints: 10 attempts per minute
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.", cancellationToken);
    };
});

// cTrader Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICTraderAuthService, CTraderAuthService>();
builder.Services.AddSingleton<ICTraderConnectionManager, CTraderConnectionManager>();
builder.Services.AddSingleton<ICTraderSymbolResolver, CTraderSymbolResolver>();
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
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.UseCors("TradingUI");
app.UseRateLimiter();
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireRateLimiting("fixed");
app.MapHub<TradingHub>("/hub").RequireCors("TradingUI");
app.MapHealthChecks("/health");
app.MapMetrics();

// Auto-apply migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

Log.Information("Trading Assistant API started");
await app.RunAsync();
