using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace OfflineMinecraftLauncher;

public class SkinService
{
    private readonly string _skinsDir;
    private readonly string _capesDir;
    private readonly string _serverStorageDir;

    public SkinService()
    {
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".death-client");
        _skinsDir = Path.Combine(basePath, "skins");
        _capesDir = Path.Combine(basePath, "capes");
        _serverStorageDir = Path.Combine(basePath, "skin-server");
        Directory.CreateDirectory(_skinsDir);
        Directory.CreateDirectory(_capesDir);
        Directory.CreateDirectory(_serverStorageDir);
    }

    public async Task<string> ImportSkinAsync(string sourceFile)
    {
        return await ImportImageAsync(sourceFile, _skinsDir, true);
    }

    public async Task<string> ImportCapeAsync(string sourceFile)
    {
        return await ImportImageAsync(sourceFile, _capesDir, false);
    }

    private async Task<string> ImportImageAsync(string sourceFile, string targetDir, bool isSkin)
    {
        var fileInfo = new FileInfo(sourceFile);
        if (fileInfo.Length > 1024 * 1024)
            throw new Exception("File is too large (must be < 1MB).");

        using var stream = File.OpenRead(sourceFile);
        using var bitmap = new Bitmap(stream);
        
        if (isSkin)
        {
            if (bitmap.Size.Width != 64 || (bitmap.Size.Height != 64 && bitmap.Size.Height != 32 && bitmap.Size.Height != 128))
                throw new Exception("Invalid skin dimensions (must be 64x64, 64x32, or 64x128).");
        }

        var fileName = Path.GetFileName(sourceFile);
        var targetPath = Path.Combine(targetDir, fileName);
        
        await Task.Run(() => File.Copy(sourceFile, targetPath, true));
        return targetPath;
    }

    /// <summary>
    /// Deploy skin and cape PNGs to the instance's config/death-client/ directory
    /// where the Fabric mod's ConfigLoader.java expects them, and write the
    /// death-client.json config file.
    /// </summary>
    public void DeployToInstance(string instanceDir, string? skinPath, string? capePath)
    {
        var configDir = Path.Combine(instanceDir, "config", "death-client");
        var skinsTarget = Path.Combine(configDir, "skins");
        var capesTarget = Path.Combine(configDir, "capes");
        var configFile = Path.Combine(configDir, "death-client.json");

        Directory.CreateDirectory(skinsTarget);
        Directory.CreateDirectory(capesTarget);

        bool skinEnabled = false;
        bool capeEnabled = false;
        string skinFileName = "skin.png";
        string capeFileName = "cape.png";

        // Copy skin PNG to instance config directory
        if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
        {
            var destSkin = Path.Combine(skinsTarget, skinFileName);
            File.Copy(skinPath, destSkin, true);
            skinEnabled = true;
        }

        // Copy cape PNG to instance config directory
        if (!string.IsNullOrWhiteSpace(capePath) && File.Exists(capePath))
        {
            var destCape = Path.Combine(capesTarget, capeFileName);
            File.Copy(capePath, destCape, true);
            
            var sourceMcmeta = capePath + ".mcmeta";
            var destMcmeta = Path.Combine(capesTarget, capeFileName + ".mcmeta");
            if (File.Exists(sourceMcmeta))
            {
                File.Copy(sourceMcmeta, destMcmeta, true);
            }
            else if (File.Exists(destMcmeta))
            {
                File.Delete(destMcmeta);
            }

            capeEnabled = true;
        }

        // Write death-client.json config that the Fabric mod reads
        var config = new
        {
            skinFile = skinFileName,
            capeFile = capeFileName,
            skinEnabled = skinEnabled,
            capeEnabled = capeEnabled
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configFile, json);
    }

    public void PublishToNodeServerStorage(string? skinPath, string? capePath)
    {
        PublishAsset(skinPath, Path.Combine(_serverStorageDir, "current-skin.png"));
        PublishAssetWithMcmeta(capePath, Path.Combine(_serverStorageDir, "current-cape.png"));
    }

    private static void PublishAsset(string? sourcePath, string destinationPath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, true);
            return;
        }

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);
    }

    private static void PublishAssetWithMcmeta(string? sourcePath, string destinationPath)
    {
        PublishAsset(sourcePath, destinationPath);

        var destinationMcmeta = destinationPath + ".mcmeta";
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath) && File.Exists(sourcePath + ".mcmeta"))
        {
            File.Copy(sourcePath + ".mcmeta", destinationMcmeta, true);
        }
        else if (File.Exists(destinationMcmeta))
        {
            File.Delete(destinationMcmeta);
        }
    }
}
