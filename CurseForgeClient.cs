using System.Net.Http.Headers;
using System.Text.Json;

namespace OfflineMinecraftLauncher;

internal sealed class CurseForgeClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    // Standard community CurseForge API key (Overwolf Eternal API)
    private const string ApiKey = "$2a$10$bL9ixNo9n.hnSAb688.f7uLT94611vE/.vAnpU69N5lB7Y.U83m@m";

    public CurseForgeClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.curseforge.com/v1/")
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AetherLauncher", "1.0"));
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(string query, string? gameVersion, string? loader, CancellationToken cancellationToken)
    {
        var gameId = "432"; // Minecraft
        var classId = "6"; // Mods by default
        
        var isPlugin = false;
        if (!string.IsNullOrWhiteSpace(loader))
        {
            var l = loader.ToLowerInvariant();
            if (l == "paper" || l == "spigot" || l == "purpur" || l == "bukkit")
            {
                classId = "5"; // Bukkit Plugins
                isPlugin = true;
            }
        }
        
        var queryParams = new List<string>
        {
            $"gameId={gameId}",
            $"classId={classId}",
            $"searchFilter={Uri.EscapeDataString(query.Trim())}",
            "pageSize=12"
        };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            queryParams.Add($"gameVersion={Uri.EscapeDataString(gameVersion.Trim())}");

        // Map loaders to CurseForge modLoaderType (only for mods, not plugins)
        // 1 = Forge, 4 = Fabric, 5 = Quilt, 6 = NeoForge
        if (!isPlugin && !string.IsNullOrWhiteSpace(loader))
        {
            var l = loader.ToLowerInvariant();
            if (l.Contains("forge") && !l.Contains("neo")) queryParams.Add("modLoaderType=1");
            else if (l.Contains("fabric")) queryParams.Add("modLoaderType=4");
            else if (l.Contains("quilt")) queryParams.Add("modLoaderType=5");
            else if (l.Contains("neoforge")) queryParams.Add("modLoaderType=6");
        }

        var response = await _httpClient.GetAsync($"mods/search?{string.Join("&", queryParams)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<CurseForgeSearchResponse>(stream, _jsonOptions, cancellationToken);
        return payload?.Data.Select(m => m.ToModrinthProject()).ToList() ?? [];
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchPacksAsync(string query, string? gameVersion, CancellationToken cancellationToken)
    {
        var gameId = "432";
        var classId = "4471"; // Modpacks
        
        var queryParams = new List<string>
        {
            $"gameId={gameId}",
            $"classId={classId}",
            $"searchFilter={Uri.EscapeDataString(query.Trim())}",
            "pageSize=12"
        };

        if (!string.IsNullOrWhiteSpace(gameVersion))
            queryParams.Add($"gameVersion={Uri.EscapeDataString(gameVersion.Trim())}");

        var response = await _httpClient.GetAsync($"mods/search?{string.Join("&", queryParams)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<CurseForgeSearchResponse>(stream, _jsonOptions, cancellationToken);
        return payload?.Data.Select(m => m.ToModrinthProject()).ToList() ?? [];
    }

    public async Task<IReadOnlyList<CurseForgeFile>> GetProjectVersionsAsync(string projectId, string? gameVersion, string? loader, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"mods/{projectId}/files", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<CurseForgeFilesResponse>(stream, _jsonOptions, cancellationToken);
        if (payload == null || payload.Data == null) return [];

        var files = payload.Data;

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            var gv = gameVersion.Trim();
            files = files.Where(f => f.GameVersions.Contains(gv, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            var l = loader.Trim().ToLowerInvariant();
            string curseForgeLoaderTag = "";
            if (l.Contains("forge") && !l.Contains("neo")) curseForgeLoaderTag = "Forge";
            else if (l.Contains("fabric")) curseForgeLoaderTag = "Fabric";
            else if (l.Contains("quilt")) curseForgeLoaderTag = "Quilt";
            else if (l.Contains("neoforge")) curseForgeLoaderTag = "NeoForge";

            if (!string.IsNullOrEmpty(curseForgeLoaderTag))
            {
                files = files.Where(f => f.GameVersions.Contains(curseForgeLoaderTag, StringComparer.OrdinalIgnoreCase)).ToList();
            }
        }

        return files;
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
