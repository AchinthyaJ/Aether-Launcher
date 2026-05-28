using System.Text.Json;

namespace OfflineMinecraftLauncher;

internal sealed class LauncherProfileStore
{
    private readonly string _instancesRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public LauncherProfileStore(string launcherBasePath)
    {
        _instancesRoot = Path.Combine(launcherBasePath, "death-client", "instances");
        Directory.CreateDirectory(_instancesRoot);
    }

    public IReadOnlyList<LauncherProfile> LoadProfiles()
    {
        var profiles = new List<LauncherProfile>();
        foreach (var directory in Directory.EnumerateDirectories(_instancesRoot))
        {
            var manifestPath = Path.Combine(directory, LauncherProfile.ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var manifestJson = File.ReadAllText(manifestPath);
                var profile = JsonSerializer.Deserialize<LauncherProfile>(manifestJson, _jsonOptions);
                if (profile is null)
                    continue;

                profile.InstanceDirectory = directory;
                profiles.Add(profile);
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Failed to load launcher profile manifest '{manifestPath}'.", ex);
            }
        }

        return profiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LauncherProfile CreateProfile(string name, string gameVersion, string loader, string? loaderVersion = null, string? sourceProjectSlug = null, string? gameDirectoryOverride = null)
    {
        var slug = Slugify(name);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "profile";

        var directory = EnsureUniqueDirectory(slug);
        var normalizedLoader = string.IsNullOrWhiteSpace(loader) ? "vanilla" : loader.Trim().ToLowerInvariant();
        var versionId = BuildVersionId(gameVersion, normalizedLoader, loaderVersion);

        var profile = new LauncherProfile
        {
            Name = name.Trim(),
            GameVersion = gameVersion.Trim(),
            Loader = normalizedLoader,
            LoaderVersion = loaderVersion?.Trim(),
            VersionId = versionId,
            SourceProjectSlug = sourceProjectSlug,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            InstanceDirectory = directory,
            GameDirectoryOverride = gameDirectoryOverride ?? string.Empty
        };

        Save(profile);
        return profile;
    }

    public void Save(LauncherProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.InstanceDirectory))
            throw new InvalidOperationException("Profile instance directory is not set.");

        Directory.CreateDirectory(profile.InstanceDirectory);
        profile.UpdatedUtc = DateTime.UtcNow;
        var manifestPath = Path.Combine(profile.InstanceDirectory, LauncherProfile.ManifestFileName);
        LauncherLog.AtomicWriteAllText(manifestPath, JsonSerializer.Serialize(profile, _jsonOptions));
    }

    public void Delete(LauncherProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.InstanceDirectory) || Directory.Exists(profile.InstanceDirectory) == false)
            return;

        Directory.Delete(profile.InstanceDirectory, recursive: true);
    }

    public string GetInstancesRoot() => _instancesRoot;

    public static string BuildVersionId(string gameVersion, string loader, string? loaderVersion)
    {
        var normalizedLoader = string.IsNullOrWhiteSpace(loader) ? "vanilla" : loader.Trim().ToLowerInvariant();
        if (normalizedLoader == "vanilla" || string.IsNullOrWhiteSpace(loaderVersion))
            return gameVersion.Trim();

        return normalizedLoader switch
        {
            "fabric" => $"fabric-loader-{loaderVersion.Trim()}-{gameVersion.Trim()}",
            "quilt" => $"quilt-loader-{loaderVersion.Trim()}-{gameVersion.Trim()}",
            _ => gameVersion.Trim()
        };
    }

    private string EnsureUniqueDirectory(string slug)
    {
        var candidate = Path.Combine(_instancesRoot, slug);
        int counter = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(_instancesRoot, $"{slug}-{counter}");
            counter++;
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string Slugify(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int index = 0;
        bool previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
                previousDash = false;
            }
            else if (!previousDash)
            {
                buffer[index++] = '-';
                previousDash = true;
            }
        }

        return new string(buffer[..index]).Trim('-');
    }
}

internal sealed class LauncherProfile
{
    public const string ManifestFileName = "death-profile.json";

    public string Name { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public string VersionId { get; set; } = string.Empty;
    public string? SourceProjectSlug { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string InstanceDirectory { get; set; } = string.Empty;
    public string GameDirectoryOverride { get; set; } = string.Empty;
    public string JvmArguments { get; set; } = string.Empty;
    public HashSet<string> InstalledModIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsInstalled { get; set; } = false;
    public bool LastLaunchCrashed { get; set; } = false;
    public int LaunchCountSinceLastInstall { get; set; } = 0;

    public string ModsDirectory => Path.Combine(InstanceDirectory, "mods");

    public override string ToString() => $"{Name} [{LoaderDisplay}]";

    public string LoaderDisplay =>
        Loader switch
        {
            "vanilla" => GameVersion,
            _ when string.IsNullOrWhiteSpace(LoaderVersion) => $"{Loader} · {GameVersion}",
            _ => $"{Loader} {LoaderVersion} · {GameVersion}"
        };
}
