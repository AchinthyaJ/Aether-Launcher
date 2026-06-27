using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace OfflineMinecraftLauncher;

public class LaunchConfig
{
    public string username { get; set; } = string.Empty;
    public string skin { get; set; } = string.Empty;
    public string cape { get; set; } = string.Empty;
}

public class LaunchService
{
    /// <summary>
    /// Writes the death-client Fabric mod config with the correct skin URL
    /// fetched from the Aether Worker skin service via SkinClient.
    /// Should be called before launching the Minecraft process.
    /// </summary>
    public async Task<string> PrepareInstanceProfileAsync(
        string instanceDirectory,
        string username,
        string? customSkinPath = null,
        string? resolvedSkinUrl = null,
        string? resolvedSkinModel = null,
        string? customCapePath = null,
        string? resolvedCapeUrl = null)
    {
        // Write to config/death-client/ which is where the Fabric mod's ConfigLoader reads
        var targetDir = Path.Combine(instanceDirectory, "config", "death-client");
        Directory.CreateDirectory(targetDir);

        var configPath = Path.Combine(targetDir, "death-client.json");

        // Copy local skin PNG if provided and exists (acts as local/offline fallback for the Fabric mod)
        bool hasSkinFile = false;
        const string skinFileName = "skin.png";
        if (!string.IsNullOrWhiteSpace(customSkinPath) && File.Exists(customSkinPath))
        {
            try
            {
                var skinsTarget = Path.Combine(targetDir, "skins");
                Directory.CreateDirectory(skinsTarget);
                File.Copy(customSkinPath, Path.Combine(skinsTarget, skinFileName), true);
                hasSkinFile = true;
                LauncherLog.Info($"[LaunchService] Copied local skin to instance fallback: {customSkinPath}");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[LaunchService] Failed to copy local skin fallback: {ex.Message}");
            }
        }

        // Copy local cape PNG if provided and exists (acts as local/offline fallback for the Fabric mod)
        bool hasCapeFile = false;
        const string capeFileName = "cape.png";
        var capesTarget = Path.Combine(targetDir, "capes");
        if (!string.IsNullOrWhiteSpace(customCapePath) && File.Exists(customCapePath))
        {
            try
            {
                Directory.CreateDirectory(capesTarget);
                File.Copy(customCapePath, Path.Combine(capesTarget, capeFileName), true);

                var sourceMcmeta = customCapePath + ".mcmeta";
                var destMcmeta = Path.Combine(capesTarget, capeFileName + ".mcmeta");
                if (File.Exists(sourceMcmeta))
                {
                    File.Copy(sourceMcmeta, destMcmeta, true);
                    LauncherLog.Info($"[LaunchService] Copied local cape .mcmeta to instance fallback: {sourceMcmeta}");
                }
                else if (File.Exists(destMcmeta))
                {
                    File.Delete(destMcmeta);
                }

                hasCapeFile = true;
                LauncherLog.Info($"[LaunchService] Copied local cape to instance fallback: {customCapePath}");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[LaunchService] Failed to copy local cape fallback: {ex.Message}");
            }
        }
        else
        {
            try
            {
                var staleCapePath = Path.Combine(capesTarget, capeFileName);
                var staleMcmetaPath = staleCapePath + ".mcmeta";
                if (File.Exists(staleCapePath))
                {
                    File.Delete(staleCapePath);
                    LauncherLog.Info($"[LaunchService] Removed stale local cape fallback: {staleCapePath}");
                }
                if (File.Exists(staleMcmetaPath))
                {
                    File.Delete(staleMcmetaPath);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[LaunchService] Failed to remove stale local cape fallback: {ex.Message}");
            }
        }

        // Fetch the player's skin profile from the Aether Worker skin service
        string skinUrl = resolvedSkinUrl ?? string.Empty;
        string skinModel = resolvedSkinModel ?? "classic";
        
        if (string.IsNullOrEmpty(skinUrl))
        {
            try
            {
                var profile = await SkinClient.GetProfileAsync(username);
                if (profile != null)
                {
                    skinUrl = profile.TextureUrl ?? string.Empty;
                    skinModel = profile.Model;
                    LauncherLog.Info($"[LaunchService] Resolved skin for '{username}': url={skinUrl}, model={skinModel}");
                }
                else
                {
                    LauncherLog.Info($"[LaunchService] No skin profile found for '{username}' — using default.");
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[LaunchService] Failed to fetch skin profile for '{username}': {ex.Message}");
            }
        }
        else
        {
            LauncherLog.Info($"[LaunchService] Using pre-resolved skin: url={skinUrl}, model={skinModel}");
        }

        // Write both the new schema and the old schema fields to ensure compatibility
        // with any version of the Fabric mod
        var config = new
        {
            username,
            skinFile = skinFileName,
            capeFile = capeFileName,
            skinEnabled = !string.IsNullOrEmpty(skinUrl) || hasSkinFile,
            capeEnabled = !string.IsNullOrEmpty(resolvedCapeUrl) || hasCapeFile,
            skinUrl,
            capeUrl = resolvedCapeUrl ?? string.Empty,
            skinModel,
            uuid = SkinClient.GenerateOfflineUUIDString(username),
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);

        return configPath;
    }
}

