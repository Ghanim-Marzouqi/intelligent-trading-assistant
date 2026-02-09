using System.Net.Http.Json;

namespace TradingAssistant.Api.Services.Notifications;

public interface IWhatsAppService
{
    Task SendMessageAsync(string message);
    Task SendTemplateMessageAsync(string templateName, Dictionary<string, string> parameters);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<WhatsAppService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var baseUrl = _config["WhatsApp:BaseUrl"] ?? "https://graph.facebook.com/v18.0";
        var phoneNumberId = _config["WhatsApp:PhoneNumberId"];

        _httpClient.BaseAddress = new Uri($"{baseUrl}/{phoneNumberId}/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config["WhatsApp:AccessToken"]}");
    }

    public async Task SendMessageAsync(string message)
    {
        var recipientPhone = _config["WhatsApp:RecipientPhone"];

        if (string.IsNullOrEmpty(recipientPhone))
        {
            _logger.LogWarning("WhatsApp recipient phone not configured");
            return;
        }

        _logger.LogDebug("Sending WhatsApp message to {Phone}", recipientPhone);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipientPhone,
            type = "text",
            text = new { body = message }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("messages", payload);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("WhatsApp message sent successfully");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message");
        }
    }

    public async Task SendTemplateMessageAsync(string templateName, Dictionary<string, string> parameters)
    {
        var recipientPhone = _config["WhatsApp:RecipientPhone"];

        if (string.IsNullOrEmpty(recipientPhone))
        {
            _logger.LogWarning("WhatsApp recipient phone not configured");
            return;
        }

        var components = new List<object>();

        if (parameters.Any())
        {
            components.Add(new
            {
                type = "body",
                parameters = parameters.Select(p => new
                {
                    type = "text",
                    text = p.Value
                }).ToArray()
            });
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipientPhone,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = "en" },
                components
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("messages", payload);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("WhatsApp template message sent: {Template}", templateName);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp template message");
        }
    }
}
