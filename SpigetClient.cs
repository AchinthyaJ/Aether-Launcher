using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineMinecraftLauncher;

internal sealed class SpigetClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SpigetClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.spiget.org/v2/")
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher/1.0");
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchResourcesAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"search/resources/{Uri.EscapeDataString(query.Trim())}?field=name&size=15", cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<List<SpigetResource>>(stream, _jsonOptions, cancellationToken);
            if (payload == null) return [];

            return payload.Select(r => new ModrinthProject
            {
                ProjectId = $"spiget_{r.Id}",
                Title = r.Name,
                Description = r.Tag ?? "No description available.",
                ServerSide = "required",
                ClientSide = "unsupported"
            }).ToList();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("[SpigetClient] SpigotMC/Spiget search failed due to error.", ex);
            return [];
        }
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

internal sealed class SpigetResource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Tag { get; set; }
}
