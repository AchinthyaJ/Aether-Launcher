using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OfflineMinecraftLauncher;

internal sealed class LauncherAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "offline";
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string Xuid { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string MinecraftAccessToken { get; set; } = string.Empty;
    public DateTime MinecraftAccessTokenExpiresUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool IsExpired => Provider == "microsoft" && (DateTime.UtcNow.AddMinutes(5) >= MinecraftAccessTokenExpiresUtc);
}

internal sealed class DeviceCodeSession
{
    public string DeviceCode { get; init; } = string.Empty;
    public string UserCode { get; init; } = string.Empty;
    public string VerificationUri { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; } = 5;
    public int ExpiresInSeconds { get; init; } = 900;
}

internal sealed class MinecraftAuthenticationService
{
    private const string DeviceCodeEndpoint = "https://login.live.com/oauth20_connect.srf";
    private const string TokenEndpoint = "https://login.live.com/oauth20_token.srf";
    private const string XboxAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftEntitlementsEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";
    private const string Scope = "XboxLive.signin offline_access";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MinecraftAuthenticationService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "FugoLauncher/1.0.0");
    }

    public async Task<DeviceCodeSession> BeginDeviceLoginAsync(string clientId, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
            ["response_type"] = "device_code"
        };

        using var response = await _httpClient.PostAsync(DeviceCodeEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            LauncherLog.Info($"[Microsoft Auth] BeginDeviceLogin failed ({(int)response.StatusCode}): {payload}");
            throw CreateMicrosoftAuthException(payload);
        }

        var result = JsonSerializer.Deserialize<DeviceCodeResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Microsoft device login response was empty.");

        return new DeviceCodeSession
        {
            DeviceCode = result.device_code,
            UserCode = result.user_code,
            VerificationUri = result.verification_uri,
            Message = result.message,
            IntervalSeconds = result.interval,
            ExpiresInSeconds = result.expires_in
        };
    }

    public async Task<LauncherAccount> CompleteDeviceLoginAsync(string clientId, DeviceCodeSession deviceSession, CancellationToken cancellationToken)
    {
        var token = await PollForMicrosoftTokenAsync(clientId, deviceSession, cancellationToken);
        return await CreateMinecraftAccountAsync(token, cancellationToken);
    }

    public async Task<LauncherAccount> RefreshMinecraftAccountAsync(string clientId, LauncherAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
            throw new InvalidOperationException("This account does not have a refresh token. Sign in again.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = account.RefreshToken,
            ["scope"] = Scope
        };

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateMicrosoftAuthException(payload);

        var token = JsonSerializer.Deserialize<TokenResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Microsoft refresh response was empty.");

        return await CreateMinecraftAccountAsync(token, cancellationToken, account.Id);
    }

    private async Task<TokenResponse> PollForMicrosoftTokenAsync(string clientId, DeviceCodeSession deviceSession, CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(deviceSession.ExpiresInSeconds);
        var interval = Math.Max(5, deviceSession.IntervalSeconds);

        while (DateTime.UtcNow < expiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = clientId,
                ["device_code"] = deviceSession.DeviceCode
            };

            using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<TokenResponse>(payload, JsonOptions)
                    ?? throw new InvalidOperationException("Microsoft token response was empty.");
            }

            var error = JsonSerializer.Deserialize<TokenErrorResponse>(payload, JsonOptions);
            if (string.Equals(error?.error, "authorization_pending", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
                continue;
            }

            if (string.Equals(error?.error, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                interval += 5;
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
                continue;
            }

            LauncherLog.Info($"[Microsoft Auth] Token polling failed ({(int)response.StatusCode}): {payload}");
            throw CreateMicrosoftAuthException(payload);
        }

        throw new TimeoutException("The Microsoft device login expired before sign-in completed.");
    }

    private async Task<LauncherAccount> CreateMinecraftAccountAsync(TokenResponse token, CancellationToken cancellationToken, string? existingId = null)
    {
        var xboxAuth = await PostJsonAsync<XboxAuthResponse>(XboxAuthEndpoint, new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={token.access_token}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, cancellationToken);

        var xstsAuth = await PostJsonAsync<XstsAuthResponse>(XstsAuthEndpoint, new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxAuth.Token }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, cancellationToken);

        var userHash = xstsAuth.DisplayClaims.xui.FirstOrDefault()?.uhs;
        var xuid = xstsAuth.DisplayClaims.xui.FirstOrDefault()?.xid ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userHash))
            throw new InvalidOperationException("Xbox authentication did not return a valid user hash.");

        var minecraftAuth = await PostJsonAsync<MinecraftLoginResponse>(MinecraftLoginEndpoint, new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsAuth.Token}"
        }, cancellationToken);

        using var entitlementsRequest = new HttpRequestMessage(HttpMethod.Get, $"{MinecraftEntitlementsEndpoint}?requestId={Guid.NewGuid():D}");
        entitlementsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAuth.access_token);
        using var entitlementsResponse = await _httpClient.SendAsync(entitlementsRequest, cancellationToken);
        var entitlementsPayload = await entitlementsResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!entitlementsResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft entitlements request failed: {entitlementsPayload}");

        var entitlements = JsonSerializer.Deserialize<MinecraftEntitlementsResponse>(entitlementsPayload, JsonOptions);
        var ownsMinecraft = entitlements?.items?.Any(item =>
            string.Equals(item.name, "product_minecraft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.name, "game_minecraft", StringComparison.OrdinalIgnoreCase)) == true;
        if (!ownsMinecraft)
            throw new InvalidOperationException("This Microsoft account does not appear to own Minecraft Java Edition.");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAuth.access_token);
        using var profileResponse = await _httpClient.SendAsync(profileRequest, cancellationToken);
        var profilePayload = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft profile request failed: {profilePayload}");

        var profile = JsonSerializer.Deserialize<MinecraftProfileResponse>(profilePayload, JsonOptions)
            ?? throw new InvalidOperationException("Minecraft profile response was empty.");

        return new LauncherAccount
        {
            Id = existingId ?? Guid.NewGuid().ToString("N"),
            Provider = "microsoft",
            DisplayName = profile.name,
            Username = profile.name,
            Uuid = profile.id,
            Xuid = xuid,
            RefreshToken = token.refresh_token,
            MinecraftAccessToken = minecraftAuth.access_token,
            MinecraftAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(minecraftAuth.expires_in),
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private async Task<T> PostJsonAsync<T>(string endpoint, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(payload);

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new InvalidOperationException($"Empty response from {endpoint}.");
    }

    private static Exception CreateMicrosoftAuthException(string payload)
    {
        var error = JsonSerializer.Deserialize<TokenErrorResponse>(payload, JsonOptions);
        var message = error?.error_description ?? error?.error ?? payload;
        return new InvalidOperationException(message);
    }

    private sealed class DeviceCodeResponse
    {
        public string device_code { get; set; } = string.Empty;
        public string user_code { get; set; } = string.Empty;
        public string verification_uri { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public int expires_in { get; set; }
        public int interval { get; set; }
    }

    private sealed class TokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string refresh_token { get; set; } = string.Empty;
    }

    private sealed class TokenErrorResponse
    {
        public string error { get; set; } = string.Empty;
        public string error_description { get; set; } = string.Empty;
    }

    private sealed class XboxAuthResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    private sealed class XstsAuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public XstsDisplayClaims DisplayClaims { get; set; } = new();
    }

    private sealed class XstsDisplayClaims
    {
        public List<XstsUserClaim> xui { get; set; } = [];
    }

    private sealed class XstsUserClaim
    {
        public string uhs { get; set; } = string.Empty;
        public string xid { get; set; } = string.Empty;
    }

    private sealed class MinecraftLoginResponse
    {
        public string access_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
    }

    private sealed class MinecraftEntitlementsResponse
    {
        public List<MinecraftEntitlementItem> items { get; set; } = [];
    }

    private sealed class MinecraftEntitlementItem
    {
        public string name { get; set; } = string.Empty;
    }

    private sealed class MinecraftProfileResponse
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
    }

    // Username/Password Authentication Method
    public async Task<LauncherAccount> AuthenticateWithUsernamePasswordAsync(string clientId, string username, string password, CancellationToken cancellationToken)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty.", nameof(username));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        // Use OAuth 2.0 Resource Owner Password Credentials (ROPC) grant
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = Scope
        };

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw CreateMicrosoftAuthException(payload);

        var token = JsonSerializer.Deserialize<TokenResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Microsoft token response was empty.");

        // Create Minecraft account using the obtained token
        return await CreateMinecraftAccountAsync(token, cancellationToken);
    }
}
