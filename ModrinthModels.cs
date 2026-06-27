using System.Text.Json.Serialization;

namespace OfflineMinecraftLauncher;

internal sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")]
    public List<ModrinthProject> Hits { get; set; } = [];
}

internal sealed class ModrinthProject
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("follows")]
    public int Follows { get; set; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("client_side")]
    public string? ClientSide { get; set; }

    [JsonPropertyName("server_side")]
    public string? ServerSide { get; set; }

    [JsonIgnore]
    public bool IsCurseForge { get; set; }

    public override string ToString() => Title;
}

internal sealed class ModrinthProjectVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthVersionFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<ModrinthVersionDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }
}

internal sealed class ModrinthVersionFile
{
    [JsonPropertyName("hashes")]
    public Dictionary<string, string> Hashes { get; set; } = [];

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("file_type")]
    public string? FileType { get; set; }
}

internal sealed class ModrinthVersionDependency
{
    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }
}

internal sealed class MrPackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public string Game { get; set; } = string.Empty;

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("files")]
    public List<MrPackFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = [];
}

internal sealed class MrPackFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("hashes")]
    public Dictionary<string, string> Hashes { get; set; } = [];

    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = [];

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("env")]
    public MrPackFileEnvironment? Env { get; set; }
}

internal sealed class MrPackFileEnvironment
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}
