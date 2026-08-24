using System.Net.Http.Headers;
using System.Text.Json;

namespace Andline.Economy;

public sealed class ShopWalletClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<ShopWalletClient> _log;
    private readonly string _urlTemplate;
    private readonly string _apiKey;
    private readonly string _headerName;

    public ShopWalletClient(HttpClient http, ILogger<ShopWalletClient> log, IConfiguration config)
    {
        _http = http;
        _log = log;
        _urlTemplate = (config["SHOP_INTERNAL_URL"] ?? string.Empty).Trim();
        _apiKey = (config["SHOP_INTERNAL_KEY"] ?? string.Empty).Trim();
        _headerName = string.IsNullOrWhiteSpace(config["SHOP_INTERNAL_HEADER"])
            ? "X-Internal-Key"
            : config["SHOP_INTERNAL_HEADER"]!.Trim();
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_urlTemplate);

    public async Task<ShopWallet?> GetAsync(string uuid, string name, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return null;

        var url = BuildUrl(uuid, name);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.TryAddWithoutValidation(_headerName, _apiKey);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Shop wallet HTTP {Status} for {Uuid}", (int)response.StatusCode, uuid);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                return data.Deserialize<ShopWallet>(JsonOptions);
            }

            return root.Deserialize<ShopWallet>(JsonOptions);
        }
        catch (Exception exception)
        {
            _log.LogWarning(exception, "Shop wallet request failed for {Uuid}", uuid);
            return null;
        }
    }

    private string BuildUrl(string uuid, string name)
    {
        var encodedUuid = Uri.EscapeDataString(uuid);
        var encodedName = Uri.EscapeDataString(name);
        if (_urlTemplate.Contains("{uuid}", StringComparison.OrdinalIgnoreCase) ||
            _urlTemplate.Contains("{name}", StringComparison.OrdinalIgnoreCase))
        {
            return _urlTemplate
                .Replace("{uuid}", encodedUuid, StringComparison.OrdinalIgnoreCase)
                .Replace("{name}", encodedName, StringComparison.OrdinalIgnoreCase);
        }

        return $"{_urlTemplate.TrimEnd('/')}/{encodedUuid}";
    }
}
