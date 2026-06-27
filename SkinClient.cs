using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OfflineMinecraftLauncher;

/// <summary>
/// Handles skin upload, profile lookup, UUID generation, and authlib-injector management
/// for the Aether edge skin service (Cloudflare Worker).
/// </summary>
public static class SkinClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // ─── Stable offline UUID ──────────────────────────────────────────────────

    /// <summary>
    /// Replicates Minecraft's offline UUID algorithm:
    /// UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))
    /// This is EXACTLY what the Minecraft server computes for offline players.
    /// </summary>
    public static Guid GenerateOfflineUUID(string username)
    {
        var bytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);

        // Set UUID version 3 and variant 2 bits (RFC 4122)
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        // Reorder bytes to match Java's UUID byte order (big-endian)
        return new Guid(new byte[]
        {
            hash[3], hash[2], hash[1], hash[0],
            hash[5], hash[4],
            hash[7], hash[6],
            hash[8], hash[9], hash[10], hash[11],
            hash[12], hash[13], hash[14], hash[15]
        });
    }

    /// <summary>Returns UUID formatted as a no-dashes hex string (Mojang API format).</summary>
    public static string GenerateOfflineUUIDString(string username)
        => GenerateOfflineUUID(username).ToString("N");

    // ─── Skin Upload ──────────────────────────────────────────────────────────

    public record SkinUploadResult(bool Success, string? TextureHash, string? TextureUrl, string? Error);

    /// <summary>
    /// Uploads a PNG skin to the Aether edge skin service.
    /// Validates dimensions before uploading.
    /// </summary>
    public static async Task<SkinUploadResult> UploadSkinAsync(
        string username, string pngFilePath, string model = "classic")
    {
        try
        {
            // Validate file exists and size
            var fileInfo = new FileInfo(pngFilePath);
            if (!fileInfo.Exists)
                return new SkinUploadResult(false, null, null, "Skin file not found.");
            if (fileInfo.Length > 256 * 1024)
                return new SkinUploadResult(false, null, null, "Skin file too large (max 256KB).");

            // Validate PNG dimensions using raw file parsing to avoid Avalonia thread/graphics initialization issues
            var dimensions = GetPngDimensions(pngFilePath);
            if (dimensions == null)
                return new SkinUploadResult(false, null, null, "File is not a valid PNG image.");

            var w = dimensions.Value.width;
            var h = dimensions.Value.height;
            if (w != 64 || (h != 64 && h != 32))
                return new SkinUploadResult(false, null, null, $"Invalid skin dimensions {w}×{h}. Must be 64×64 or 64×32.");

            // Build multipart form upload
            var baseUrl = DiscoveryClient.BaseUrl.TrimEnd('/');
            using var form = new MultipartFormDataContent();
            var pngBytes = await File.ReadAllBytesAsync(pngFilePath);
            var pngContent = new ByteArrayContent(pngBytes);
            pngContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            pngContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"skin\"",
                FileName = "\"skin.png\""
            };
            form.Add(pngContent);

            var modelContent = new StringContent(model);
            modelContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"model\""
            };
            form.Add(modelContent);

            var url = $"{baseUrl}/api/skins/{Uri.EscapeDataString(username)}";
            var response = await _http.PutAsync(url, form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new SkinUploadResult(false, null, null, $"Upload failed ({(int)response.StatusCode}): {body}");

            var result = JsonSerializer.Deserialize<SkinUploadResponse>(body, _json);
            return new SkinUploadResult(true, result?.Hash, result?.Url, null);
        }
        catch (Exception ex)
        {
            return new SkinUploadResult(false, null, null, ex.Message);
        }
    }

    // ─── Cape Upload ──────────────────────────────────────────────────────────

    public record CapeUploadResult(bool Success, string? TextureHash, string? TextureUrl, string? Error);

    /// <summary>
    /// Uploads a PNG cape to the Aether edge skin service.
    /// Validates dimensions before uploading.
    /// </summary>
    public static async Task<CapeUploadResult> UploadCapeAsync(
        string username, string pngFilePath)
    {
        try
        {
            var fileInfo = new FileInfo(pngFilePath);
            if (!fileInfo.Exists)
                return new CapeUploadResult(false, null, null, "Cape file not found.");
            if (fileInfo.Length > 256 * 1024)
                return new CapeUploadResult(false, null, null, "Cape file too large (max 256KB).");

            bool isGif = false;
            int w = 0, h = 0;
            using (var fs = new FileStream(pngFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length >= 10)
                {
                    byte[] header = new byte[10];
                    if (fs.Read(header, 0, 10) == 10)
                    {
                        isGif = header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46; // GIF
                        if (isGif)
                        {
                            w = header[6] | (header[7] << 8);
                            h = header[8] | (header[9] << 8);
                        }
                    }
                }
            }

            if (!isGif)
            {
                var dimensions = GetPngDimensions(pngFilePath);
                if (dimensions == null)
                    return new CapeUploadResult(false, null, null, "File is not a valid PNG or GIF image.");

                w = dimensions.Value.width;
                h = dimensions.Value.height;
                // Accept standard 64×32, HD 128×64, or animated spritesheets (height is multiple of frame height)
                bool validStandard = w == 64 && h >= 32 && h % 32 == 0;
                bool validHD = w == 128 && h >= 64 && h % 64 == 0;
                if (!validStandard && !validHD)
                    return new CapeUploadResult(false, null, null, $"Invalid cape dimensions {w}×{h}. Width must be 64 or 128, height must be a multiple of {(w >= 128 ? 64 : 32)}.");
            }

            var baseUrl = DiscoveryClient.BaseUrl.TrimEnd('/');
            using var form = new MultipartFormDataContent();
            var pngBytes = await File.ReadAllBytesAsync(pngFilePath);
            var pngContent = new ByteArrayContent(pngBytes);
            pngContent.Headers.ContentType = new MediaTypeHeaderValue(isGif ? "image/gif" : "image/png");
            pngContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"cape\"",
                FileName = isGif ? "\"cape.gif\"" : "\"cape.png\""
            };
            form.Add(pngContent);

            var url = $"{baseUrl}/api/capes/{Uri.EscapeDataString(username)}";
            var response = await _http.PutAsync(url, form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new CapeUploadResult(false, null, null, $"Upload failed ({(int)response.StatusCode}): {body}");

            var result = JsonSerializer.Deserialize<SkinUploadResponse>(body, _json);
            return new CapeUploadResult(true, result?.Hash, result?.Url, null);
        }
        catch (Exception ex)
        {
            return new CapeUploadResult(false, null, null, ex.Message);
        }
    }

    // ─── Profile Lookup ───────────────────────────────────────────────────────

    public record SkinProfile(string Username, string Uuid, string? TextureUrl, string Model);

    public static async Task<SkinProfile?> GetProfileAsync(string username)
    {
        try
        {
            var baseUrl = DiscoveryClient.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/api/profiles/minecraft/{Uri.EscapeDataString(username)}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            var profile = JsonSerializer.Deserialize<ProfileResponse>(body, _json);
            if (profile == null) return null;

            // Bug 4: Extract TextureUrl and Model from base64-encoded textures property
            string? textureUrl = null;
            string model = "classic";

            if (profile.Properties is { Count: > 0 })
            {
                var texturesProp = profile.Properties.Find(p =>
                    string.Equals(p.Name, "textures", StringComparison.OrdinalIgnoreCase));

                if (texturesProp != null && !string.IsNullOrEmpty(texturesProp.Value))
                {
                    try
                    {
                        var decodedBytes = Convert.FromBase64String(texturesProp.Value);
                        var decodedJson = Encoding.UTF8.GetString(decodedBytes);
                        using var doc = JsonDocument.Parse(decodedJson);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("textures", out var textures) &&
                            textures.TryGetProperty("SKIN", out var skin))
                        {
                            if (skin.TryGetProperty("url", out var urlProp))
                                textureUrl = urlProp.GetString();

                            if (skin.TryGetProperty("metadata", out var metadata) &&
                                metadata.TryGetProperty("model", out var modelProp))
                                model = modelProp.GetString() ?? "classic";
                        }
                    }
                    catch
                    {
                        // Failed to decode textures — fall through with defaults
                    }
                }
            }

            return new SkinProfile(profile.Name, profile.Id, textureUrl, model);
        }
        catch
        {
            return null;
        }
    }

    // ─── authlib-injector management ──────────────────────────────────────────

    private const string AutolibInjectorVersion = "1.2.5";
    private const string AutolibInjectorUrl =
        "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar";

    public static string AutolibInjectorPath =>
        Path.Combine(AppRuntime.DataDirectory, "authlib-injector.jar");

    /// <summary>
    /// Downloads authlib-injector.jar if not already present.
    /// Returns the local path on success, null on failure.
    /// </summary>
    public static async Task<string?> EnsureAuthlibInjectorAsync(Action<string>? statusCallback = null)
    {
        var jarPath = AutolibInjectorPath;
        if (File.Exists(jarPath))
            return jarPath;

        try
        {
            statusCallback?.Invoke("Downloading authlib-injector...");
            var dir = Path.GetDirectoryName(jarPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var bytes = await _http.GetByteArrayAsync(AutolibInjectorUrl);
            await File.WriteAllBytesAsync(jarPath, bytes);
            statusCallback?.Invoke("authlib-injector ready.");
            return jarPath;
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke($"[Warning] Could not download authlib-injector: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Builds the -javaagent JVM argument string to inject into Minecraft / server launches.
    /// Returns null if authlib-injector.jar is not available.
    /// </summary>
    public static string? BuildAuthlibInjectorArg()
    {
        var jarPath = AutolibInjectorPath;
        if (!File.Exists(jarPath)) return null;

        var apiRoot = "http://127.0.0.1:47135/";
        return $"-javaagent:{jarPath}={apiRoot}";
    }

    // ─── Response models ──────────────────────────────────────────────────────

    private class SkinUploadResponse
    {
        public string Hash { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    private class ProfileResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<ProfileProperty>? Properties { get; set; }
    }

    private class ProfileProperty
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Signature { get; set; }
    }

    /// <summary>Public accessor for PNG dimension parsing (used by animated cape detection).</summary>
    public static (int width, int height)? GetPngDimensionsPublic(string filePath)
        => GetPngDimensions(filePath);

    public static bool IsAnimatedCape(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        // 1. Check if GIF
        bool isGif = false;
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length >= 6)
                {
                    byte[] header = new byte[6];
                    if (fs.Read(header, 0, 6) == 6)
                    {
                        isGif = header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46; // GIF
                    }
                }
            }
        }
        catch { }

        if (isGif) return true;

        // 2. Check if PNG vertical spritesheet
        try
        {
            var dims = GetPngDimensionsPublic(filePath);
            if (dims != null)
            {
                var (w, h) = dims.Value;
                int frameHeight = (w >= 128) ? 64 : 32;
                if (h >= 2 * frameHeight && h % frameHeight == 0)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static (int width, int height)? GetPngDimensions(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 24)
            {
                LauncherLog.Warn($"[PngParser] File {filePath} is too short: {fs.Length} bytes.");
                return null;
            }

            var header = new byte[24];
            if (fs.Read(header, 0, 24) != 24)
            {
                LauncherLog.Warn($"[PngParser] Failed to read 24 bytes from {filePath}.");
                return null;
            }

            // Check signature: 89 50 4E 47 0D 0A 1A 0A
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47 ||
                header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A)
            {
                LauncherLog.Warn($"[PngParser] Invalid PNG signature for {filePath}: {BitConverter.ToString(header)}");
                return null;
            }

            // Check IHDR chunk type: 49 48 44 52
            if (header[12] != 0x49 || header[13] != 0x48 || header[14] != 0x44 || header[15] != 0x52)
            {
                LauncherLog.Warn($"[PngParser] Invalid PNG IHDR chunk type for {filePath}: {BitConverter.ToString(header)}");
                return null;
            }

            // Width is at bytes 16-19 (big-endian)
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            // Height is at bytes 20-23 (big-endian)
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            return (width, height);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"[PngParser] Exception parsing PNG {filePath}: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// Converts a GIF file to a vertical PNG spritesheet, or copies a PNG file directly, and writes a corresponding .mcmeta file if it is animated.
    /// </summary>
    public static bool ConvertGifToPngSpritesheet(string gifPath, string pngPath, out int frameCount, out string? error)
    {
        frameCount = 1;
        error = null;
        try
        {
            // Check if it's a GIF
            bool isGif = false;
            using (var fs = new FileStream(gifPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length >= 6)
                {
                    byte[] header = new byte[6];
                    if (fs.Read(header, 0, 6) == 6)
                    {
                        isGif = header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && // GIF
                                header[3] == 0x38 && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61; // 87a or 89a
                    }
                }
            }

            if (isGif)
            {
                using (var image = Image.Load<Rgba32>(gifPath))
                {
                    frameCount = image.Frames.Count;
                    if (frameCount <= 0)
                    {
                        error = "GIF has no frames.";
                        return false;
                    }

                    int w = image.Width;
                    int h = image.Height;

                    // Standard cape frame: 64x32. HD cape frame: 128x64.
                    // Enforce 2:1 aspect ratio (width:height) per frame
                    int targetW = (w >= 128) ? 128 : 64;
                    int targetH = (w >= 128) ? 64 : 32;

                    using (var spritesheet = new Image<Rgba32>(targetW, targetH * frameCount))
                    {
                        for (int i = 0; i < frameCount; i++)
                        {
                            using (var frameImage = image.Frames.CloneFrame(i))
                            {
                                if (frameImage.Width != targetW || frameImage.Height != targetH)
                                {
                                    frameImage.Mutate(x => x.Resize(targetW, targetH));
                                }
                                
                                var destPoint = new Point(0, i * targetH);
                                spritesheet.Mutate(x => x.DrawImage(frameImage, destPoint, 1f));
                            }
                        }
                        spritesheet.SaveAsPng(pngPath);
                    }
                }
            }
            else
            {
                // Just copy the PNG file directly
                File.Copy(gifPath, pngPath, true);
            }

            bool isAnimatedPng = false;
            if (!isGif)
            {
                var dims = GetPngDimensions(pngPath);
                if (dims != null)
                {
                    var (w, h) = dims.Value;
                    int frameHeight = (w >= 128) ? 64 : 32;
                    if (h > 2 * frameHeight && h % frameHeight == 0)
                    {
                        isAnimatedPng = true;
                        frameCount = h / frameHeight;
                    }
                }
            }

            if (isGif || isAnimatedPng)
            {
                var mcmetaPath = pngPath + ".mcmeta";
                var mcmeta = "{\"animation\":{\"interpolate\":false,\"frametime\":2}}";
                File.WriteAllText(mcmetaPath, mcmeta);
                LauncherLog.Info($"[Cape] Generated .mcmeta for animated cape: {mcmetaPath}");
            }
            else
            {
                // Delete existing .mcmeta if the new cape is static
                var mcmetaPath = pngPath + ".mcmeta";
                if (File.Exists(mcmetaPath))
                {
                    try { File.Delete(mcmetaPath); } catch {}
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
