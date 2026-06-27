using System.Net.Http.Headers;
using System.Text.Json;

namespace OfflineMinecraftLauncher;

internal sealed class ModrinthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ModrinthClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.modrinth.com/v2/")
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AetherLauncher", "1.0"));
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(string query, string projectType, string? gameVersion, string? loader, CancellationToken cancellationToken)
    {
        var facets = new List<List<string>>
        {
            new List<string> { $"project_type:{projectType}" }
        };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add(new List<string> { $"versions:{gameVersion.Trim()}" });

        if (!string.IsNullOrWhiteSpace(loader) && !string.Equals(loader, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            var l = loader.Trim().ToLowerInvariant();
            if (l == "paper" || l == "spigot" || l == "purpur" || l == "bukkit")
            {
                // Support searching across all common plugin categories so we don't miss plugins tagged only as spigot or bukkit
                facets.Add(new List<string> { "categories:spigot", "categories:paper", "categories:purpur", "categories:bukkit" });
            }
            else
            {
                facets.Add(new List<string> { $"categories:{l}" });
            }
        }

        var encodedFacets = Uri.EscapeDataString(JsonSerializer.Serialize(facets));
        var encodedQuery = Uri.EscapeDataString(query.Trim());
        try
        {
            var response = await _httpClient.GetAsync($"search?query={encodedQuery}&limit=24&index=downloads&facets={encodedFacets}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                LauncherLog.Error($"[ModrinthClient] Search failed with {response.StatusCode}: {error}");
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<ModrinthSearchResponse>(stream, _jsonOptions, cancellationToken);
            return payload?.Hits ?? [];
        }
        catch (Exception ex)
        {
            LauncherLog.Error("[ModrinthClient] Search failed due to network error.", ex);
            return [];
        }
    }

    public async Task<IReadOnlyList<ModrinthProjectVersion>> GetProjectVersionsAsync(string projectIdOrSlug, string? gameVersion, string? loader, CancellationToken cancellationToken)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion.Trim() }))}");

        if (!string.IsNullOrWhiteSpace(loader) && !string.Equals(loader, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            var l = loader.Trim().ToLowerInvariant();
            if (l == "paper" || l == "spigot" || l == "purpur" || l == "bukkit")
            {
                query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { "spigot", "paper", "purpur", "bukkit" }))}");
            }
            else
            {
                query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { l }))}");
            }
        }

        var suffix = query.Count == 0 ? string.Empty : $"?{string.Join("&", query)}";
        var response = await _httpClient.GetAsync($"project/{Uri.EscapeDataString(projectIdOrSlug)}/version{suffix}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<List<ModrinthProjectVersion>>(stream, _jsonOptions, cancellationToken);
        if (payload == null) return [];

        var list = payload;

        // Perform strict client-side filtering as a bulletproof safeguard against API parameter variations
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            var gv = gameVersion.Trim();
            list = list.Where(v => v.GameVersions.Contains(gv, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(loader) && !string.Equals(loader, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            var l = loader.Trim().ToLowerInvariant();
            if (l == "paper" || l == "spigot" || l == "purpur" || l == "bukkit")
            {
                var pluginLoaders = new[] { "spigot", "paper", "purpur", "bukkit" };
                list = list.Where(v => v.Loaders.Any(vl => pluginLoaders.Contains(vl.ToLowerInvariant()))).ToList();
            }
            else
            {
                list = list.Where(v => v.Loaders.Any(vl => string.Equals(vl, l, StringComparison.OrdinalIgnoreCase))).ToList();
            }
        }

        return list;
    }

    public async Task DownloadFileAsync(string url, string destinationPath, IProgress<(long BytesRead, long? TotalBytes)>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            progress?.Report((totalRead, totalBytes));
        }
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
