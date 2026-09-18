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
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{Global.AppName}/{Utils.GetVersion()}");
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

    private static (string? content, string? error) ExtractNodesContent(string body)
    {
        if (body.IsNullOrEmpty())
        {
            return (null, "empty response");
        }

        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return (body, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var okProp)
                && okProp.ValueKind is JsonValueKind.False or JsonValueKind.True
                && !okProp.GetBoolean())
            {
                return (null, ExtractServerError(body) ?? "request failed");
            }

            var content = ExtractSubscriptionFromJson(root);
            if (content.IsNotEmpty())
            {
                return (content, null);
            }
        }
        catch
        {
        }

        return (null, ExtractServerError(body) ?? "invalid nodes response");
    }

    private static string ExtractSubscriptionFromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;

            case JsonValueKind.Array:
                var lines = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    var line = ExtractSubscriptionFromJson(item);
                    if (line.IsNotEmpty())
                    {
                        lines.Add(line);
                    }
                }
                return string.Join(Environment.NewLine, lines);

            case JsonValueKind.Object:
                foreach (var key in new[] { "uri", "url", "link", "share_url", "shareUrl", "node", "content", "subscription" })
                {
                    if (element.TryGetProperty(key, out var value))
                    {
                        var line = ExtractSubscriptionFromJson(value);
                        if (line.IsNotEmpty())
                        {
                            return line;
                        }
                    }
                }

                foreach (var key in new[] { "nodes", "links", "uris", "data", "content", "subscription" })
                {
                    if (element.TryGetProperty(key, out var nested))
                    {
                        var content = ExtractSubscriptionFromJson(nested);
                        if (content.IsNotEmpty())
                        {
                            return content;
                        }
                    }
                }
                break;
        }

        return string.Empty;
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
            SaveAuthResult(config, response);
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
            SaveAuthResult(config, response);
            return true;
        }
        if (error.IsNotEmpty())
        {
            NoticeManager.Instance.SendMessageEx(error);
        }
        return false;
    }

    public bool IsMember(Config config)
    {
        return string.Equals(config.VpnItem?.UserType, "member", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTokenExpired(Config config)
    {
        return config.VpnItem == null
            || config.VpnItem.AccessToken.IsNullOrEmpty()
            || config.VpnItem.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task<bool> LogoutAsync(Config config)
    {
        if (!TryGetBase(config, out var baseUri, out var token) || token.IsNullOrEmpty())
        {
            ClearAuthData(config);
            await ConfigHandler.SaveConfig(config);
            return true;
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutUri = $"{baseUri.TrimEnd('/')}/v1/vpn/auth/logout";
        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(logoutUri, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var err = ExtractServerError(responseBody) ?? $"HTTP {(int)response.StatusCode}";
                NoticeManager.Instance.SendMessageEx(err);
            }
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            ClearAuthData(config);
            await ConfigHandler.SaveConfig(config);
        }

        return true;
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
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var err = ExtractServerError(responseBody) ?? $"HTTP {(int)response.StatusCode}";
                NoticeManager.Instance.SendMessageEx(err);
                return -1;
            }
            var (strData, parseError) = ExtractNodesContent(responseBody);
            if (strData.IsNullOrEmpty())
            {
                NoticeManager.Instance.SendMessageEx(parseError ?? ResUI.OperationFailed);
                return -1;
            }

            config.VpnItem ??= new VpnItem();
            var subid = config.VpnItem.VpnSubId ?? Guid.NewGuid().ToString("N");
            config.VpnItem.VpnSubId = subid;
            var count = await ConfigHandler.AddBatchServers(config, strData, subid, true);
            await ConfigHandler.SaveConfig(config);
            if (count < 1)
            {
                NoticeManager.Instance.SendMessageEx(ResUI.OperationFailed);
            }
            return count;
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static void SaveAuthResult(Config config, AuthResponse response)
    {
        config.VpnItem.AccessToken = response.access_token;
        config.VpnItem.ExpiresAt = ParseExpiresAt(response.expires_at);
        if (response.user != null)
        {
            config.VpnItem.UserEmail = response.user.email;
            config.VpnItem.UserNickname = response.user.nickname;
            config.VpnItem.UserType = response.user.user_type;
        }
    }

    private static void ClearAuthData(Config config)
    {
        config.VpnItem ??= new VpnItem();
        config.VpnItem.AccessToken = null;
        config.VpnItem.ExpiresAt = 0;
        config.VpnItem.UserEmail = null;
        config.VpnItem.UserNickname = null;
        config.VpnItem.UserType = null;
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
        public string? user_type { get; set; }
    }
}
