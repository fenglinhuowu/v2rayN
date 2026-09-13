using System.Net.Http.Headers;
using System.Text.Json;
using ServiceLib.Common;
using ServiceLib.Handler;
using ServiceLib.Models.Configs;

namespace ServiceLib.Services;

public class VpnApiService
{
    private static readonly Lazy<VpnApiService> _instance = new(() => new VpnApiService());
    public static VpnApiService Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    private VpnApiService()
    {
        var handler = new HttpClientHandler { UseCookies = false };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"v2rayN/{Utils.GetVersion()}");
    }

    private static bool TryGetBase(Config config, out string baseUri, out string token)
    {
        baseUri = config.VpnItem?.ApiBaseUrl ?? string.Empty;
        token = config.VpnItem?.AccessToken ?? string.Empty;
        return baseUri.IsNotEmpty();
    }

    private static string? ExtractServerError(string? body)
    {
        if (body.IsNullOrEmpty())
        {
            return null;
        }
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                {
                    return err.GetString();
                }
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    return msg.GetString();
                }
            }
        }
        catch
        {
        }
        return body;
    }

    private async Task<(AuthResponse? response, string? error)> PostAuthAsync(string baseUri, string path, string email, string password)
    {
        var uri = $"{baseUri.TrimEnd('/')}/{path.TrimStart('/')}";
        var payload = new AuthRequest { email = email, password = password };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(uri, content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return (null, ExtractServerError(body) ?? $"HTTP {(int)response.StatusCode}");
        }
        return (JsonUtils.Deserialize<AuthResponse>(body), null);
    }

    public async Task<bool> RegisterAsync(Config config, string email, string password)
    {
        if (!TryGetBase(config, out var baseUri, out _))
        {
            return false;
        }
        var (response, error) = await PostAuthAsync(baseUri, "v1/vpn/auth/register", email, password);
        if (response?.ok == true && response.access_token.IsNotEmpty())
        {
            config.VpnItem.AccessToken = response.access_token;
            config.VpnItem.ExpiresAt = ParseExpiresAt(response.expires_at);
            return true;
        }
        if (error.IsNotEmpty())
        {
            NoticeManager.Instance.SendMessageEx(error);
        }
        return false;
    }

    public async Task<bool> LoginAsync(Config config, string email, string password)
    {
        if (!TryGetBase(config, out var baseUri, out _))
        {
            return false;
        }
        var (response, error) = await PostAuthAsync(baseUri, "v1/vpn/auth/login", email, password);
        if (response?.ok == true && response.access_token.IsNotEmpty())
        {
            config.VpnItem.AccessToken = response.access_token;
            config.VpnItem.ExpiresAt = ParseExpiresAt(response.expires_at);
            return true;
        }
        if (error.IsNotEmpty())
        {
            NoticeManager.Instance.SendMessageEx(error);
        }
        return false;
    }

    public bool IsTokenExpired(Config config)
    {
        return config.VpnItem == null
            || config.VpnItem.AccessToken.IsNullOrEmpty()
            || config.VpnItem.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task<int> GetNodesAsync(Config config)
    {
        if (!TryGetBase(config, out var baseUri, out var token))
        {
            return -1;
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            return -1;
        }
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var nodeUri = $"{baseUri.TrimEnd('/')}/v1/vpn/nodes";
        try
        {
            using var response = await _httpClient.GetAsync(nodeUri);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var err = ExtractServerError(body) ?? $"HTTP {(int)response.StatusCode}";
                NoticeManager.Instance.SendMessageEx(err);
                return -1;
            }
            var strData = await response.Content.ReadAsStringAsync();
            var subid = config.VpnItem.VpnSubId ?? Guid.NewGuid().ToString("N");
            config.VpnItem.VpnSubId = subid;
            return await ConfigHandler.AddBatchServers(config, strData, subid, true);
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static long ParseExpiresAt(string? value)
    {
        if (value.IsNullOrEmpty())
        {
            return 0;
        }
        if (long.TryParse(value, out var unix))
        {
            return unix;
        }
        if (DateTimeOffset.TryParse(value, out var dto))
        {
            return dto.ToUnixTimeSeconds();
        }
        return 0;
    }

    private class AuthRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
    }

    private class AuthResponse
    {
        public bool ok { get; set; }
        public string? access_token { get; set; }
        public string? expires_at { get; set; }
        public UserInfo? user { get; set; }
    }

    private class UserInfo
    {
        public string? email { get; set; }
        public string? nickname { get; set; }
    }
}
