using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OfflineMinecraftLauncher
{
    public class DiscoveryClient
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // Edge discovery API — repo is private so safe to hardcode
        public static string BaseUrl { get; set; } = "https://aether.aetherservers.workers.dev/";

        static DiscoveryClient()
        {
            try
            {
                var envPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
                if (!System.IO.File.Exists(envPath))
                {
                    envPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".env");
                }

                if (System.IO.File.Exists(envPath))
                {
                    var lines = System.IO.File.ReadAllLines(envPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                        var idx = trimmed.IndexOf('=');
                        if (idx > 0)
                        {
                            var key = trimmed.Substring(0, idx).Trim();
                            var val = trimmed.Substring(idx + 1).Trim();
                            if (key == "DISCOVERY_API_URL")
                            {
                                BaseUrl = val;
                                break;
                            }
                        }
                    }
                }
            }
            catch {}
        }

        public class ServerPresence
        {
            [JsonPropertyName("inviteCode")]
            public string InviteCode { get; set; } = string.Empty;

            [JsonPropertyName("hostUserId")]
            public string HostUserId { get; set; } = string.Empty;

            [JsonPropertyName("serverName")]
            public string ServerName { get; set; } = string.Empty;

            [JsonPropertyName("endpoint")]
            public string Endpoint { get; set; } = string.Empty;

            [JsonPropertyName("players")]
            public List<string> Players { get; set; } = new();

            [JsonPropertyName("autoInvite")]
            public bool AutoInvite { get; set; } = true;
        }

        public class DiscoveryResponse
        {
            [JsonPropertyName("inviteCode")]
            public string InviteCode { get; set; } = string.Empty;

            [JsonPropertyName("serverName")]
            public string ServerName { get; set; } = string.Empty;

            [JsonPropertyName("endpoint")]
            public string Endpoint { get; set; } = string.Empty;

            [JsonPropertyName("online")]
            public bool Online { get; set; }
        }

        public static async Task<bool> AnnounceServerAsync(ServerPresence presence)
        {
            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/announce";
                var json = JsonSerializer.Serialize(presence);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Announce failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> SendHeartbeatAsync(string inviteCode)
        {
            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/heartbeat";
                var json = JsonSerializer.Serialize(new { inviteCode });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Heartbeat failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<List<DiscoveryResponse>> FetchActiveServersAsync(string userId)
        {
            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/servers?userId={Uri.EscapeDataString(userId)}";
                var response = await _httpClient.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new List<DiscoveryResponse>();
                }
                response.EnsureSuccessStatusCode();
                var responseStr = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<DiscoveryResponse>>(responseStr) ?? new List<DiscoveryResponse>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Fetch active servers failed: {ex.Message}");
                return new List<DiscoveryResponse>();
            }
        }

        public static async Task<DiscoveryResponse?> ResolveInviteAsync(string inviteCode)
        {
            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/resolve/{Uri.EscapeDataString(inviteCode)}";
                var response = await _httpClient.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new DiscoveryResponse { InviteCode = inviteCode, Online = false };
                }
                response.EnsureSuccessStatusCode();
                var responseStr = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<DiscoveryResponse>(responseStr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Resolve invite failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> RemoveServerAsync(string inviteCode)
        {
            try
            {
                var url = $"{BaseUrl.TrimEnd('/')}/shutdown";
                var json = JsonSerializer.Serialize(new { inviteCode });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Remove server failed: {ex.Message}");
                return false;
            }
        }
    }
}
