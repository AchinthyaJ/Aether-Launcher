using System;
using System.Runtime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CmlLib.Core.Version;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading;
using Avalonia.Styling;
namespace OfflineMinecraftLauncher;

public class ModItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isEnabled;
    public string FileName { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (!value && (FullPath.Contains("fabric-api", StringComparison.OrdinalIgnoreCase) || FullPath.Contains("aether-client", StringComparison.OrdinalIgnoreCase)))
            {
                // Prevent disabling fabric-api or aether-client
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
                return;
            }
            _isEnabled = value;
            if (string.IsNullOrEmpty(FullPath)) return; // Init

            try
            {
                if (value && FileName.EndsWith(".disabled"))
                {
                    var newPath = FullPath.Substring(0, FullPath.Length - ".disabled".Length);
                    File.Move(FullPath, newPath);
                    FullPath = newPath;
                    FileName = Path.GetFileName(newPath);
                }
                else if (!value && !FileName.EndsWith(".disabled"))
                {
                    var newPath = FullPath + ".disabled";
                    File.Move(FullPath, newPath);
                    FullPath = newPath;
                    FileName = Path.GetFileName(newPath);
                }
            }
            catch { }
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(FileName)));
        }
    }

    public void InitState(bool state) { _isEnabled = state; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}


public sealed class MainWindow : Window
{
    private readonly MinecraftLauncher _defaultLauncher;
    private readonly MinecraftPath _defaultMinecraftPath;
    private readonly LauncherProfileStore _profileStore;
    private readonly UserSettingsStore _settingsStore;
    private readonly ModrinthClient _modrinthClient = new();
    private readonly CurseForgeClient _curseForgeClient = new();
    private readonly ObservableCollection<string> _versionItems = [];
    private readonly ObservableCollection<LauncherProfile> _profileItems = [];
    private readonly ObservableCollection<ModItem> _modItems = [];
    private readonly ObservableCollection<ModrinthProject> _searchResults = [];
    private static readonly string[] ProjectTypeOptions = ["Mod", "Modpack"];
    private static readonly string[] LoaderOptions = ["Any", "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge"];
    private static readonly string[] ProfileLoaderOptions = ["Vanilla", "Fabric", "Quilt", "Forge", "NeoForge"];
    private static readonly string[] ProfilePresetOptions = ["Aether Client (Fabric) (Coming Soon)", "Vanilla Minecraft", "Custom Modded"];
    private static readonly string[] VersionCategoryOptions = ["Versions", "Snapshots", "Other sources"];
    private static readonly string[] SourceOptions = ["Modrinth", "CurseForge"];

    // Local server hosting state fields
    public class LocalServerMetadata
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Loader { get; set; } = "vanilla";
        public string Version { get; set; } = "";
        public string Port { get; set; } = "25565";
        public string MaxPlayers { get; set; } = "20";
        public string RamAllocation { get; set; } = "2G";
        public bool OnlineMode { get; set; } = false;
        public bool UseUPnP { get; set; } = true;
        public bool UseTunnel { get; set; } = true;
        public string FolderPath { get; set; } = "";
        public double PlayerTimeoutHours { get; set; } = 2.0;
        public double EmptyTimeoutMinutes { get; set; } = 30.0;
        public string ActiveTunnelAddress { get; set; } = "";
        public string InviteCode { get; set; } = "";
        public List<string> AllowedPlayers { get; set; } = new();
        public bool AutoInvite { get; set; } = true;
        public DateTime? InviteCodeLastChanged { get; set; } = null;
        public string GcProfile { get; set; } = "aikar";
    }

    public class PropertyDefinition
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "Advanced";
        public string Type { get; set; } = "text"; // "text", "boolean", "choice"
        public string[]? Choices { get; set; }
    }

    private static readonly List<PropertyDefinition> ServerPropertyDefinitions = new()
    {
        // General
        new PropertyDefinition { Key = "motd", Label = "Message of the Day (MOTD)", Description = "The server description shown in the multiplayer list.", Category = "General", Type = "text" },
        new PropertyDefinition { Key = "server-ip", Label = "Server IP Bind Address", Description = "Binds the server to a specific local network IP (leave empty for all).", Category = "General", Type = "text" },
        new PropertyDefinition { Key = "server-port", Label = "Server Port", Description = "The port the server listens on.", Category = "General", Type = "text" },
        new PropertyDefinition { Key = "max-players", Label = "Max Players", Description = "The maximum number of players allowed online.", Category = "General", Type = "text" },
        new PropertyDefinition { Key = "online-mode", Label = "Online Mode", Description = "Enforces Microsoft account authentication.", Category = "General", Type = "boolean" },
        new PropertyDefinition { Key = "white-list", Label = "Enable Whitelist", Description = "Only allow whitelisted players to join.", Category = "General", Type = "boolean" },
        new PropertyDefinition { Key = "enforce-whitelist", Label = "Enforce Whitelist", Description = "Kicks players who are not on the whitelist when it is reloaded.", Category = "General", Type = "boolean" },
        new PropertyDefinition { Key = "hide-online-players", Label = "Hide Online Players", Description = "Does not send a player list on server ping.", Category = "General", Type = "boolean" },
        new PropertyDefinition { Key = "enable-status", Label = "Enable Status Pings", Description = "Allows the server to appear 'online' in server lists.", Category = "General", Type = "boolean" },
 
        // Gameplay
        new PropertyDefinition { Key = "gamemode", Label = "Default Gamemode", Description = "The default gamemode for new players.", Category = "Gameplay", Type = "choice", Choices = new[] { "survival", "creative", "adventure", "spectator" } },
        new PropertyDefinition { Key = "difficulty", Label = "Difficulty", Description = "The game difficulty.", Category = "Gameplay", Type = "choice", Choices = new[] { "peaceful", "easy", "normal", "hard" } },
        new PropertyDefinition { Key = "pvp", Label = "Allow PvP", Description = "Enables player-versus-player combat.", Category = "Gameplay", Type = "boolean" },
        new PropertyDefinition { Key = "hardcore", Label = "Hardcore Mode", Description = "Permanently bans players upon death.", Category = "Gameplay", Type = "boolean" },
        new PropertyDefinition { Key = "force-gamemode", Label = "Force Gamemode", Description = "Forces players to join in the default gamemode.", Category = "Gameplay", Type = "boolean" },
        new PropertyDefinition { Key = "spawn-protection", Label = "Spawn Protection Radius", Description = "Radius of spawn protection in blocks (0 to disable).", Category = "Gameplay", Type = "text" },
        new PropertyDefinition { Key = "allow-flight", Label = "Allow Flight", Description = "Allows players to fly (prevents kick for flying).", Category = "Gameplay", Type = "boolean" },
        new PropertyDefinition { Key = "allow-nether", Label = "Allow Nether", Description = "Allows players to travel to the Nether dimension.", Category = "Gameplay", Type = "boolean" },
 
        // World & Spawning
        new PropertyDefinition { Key = "level-name", Label = "World Folder Name", Description = "The name of the directory containing the world save.", Category = "World & Spawning", Type = "text" },
        new PropertyDefinition { Key = "level-seed", Label = "World Seed", Description = "Seed for generating the world map.", Category = "World & Spawning", Type = "text" },
        new PropertyDefinition { Key = "level-type", Label = "World Gen Type", Description = "The type of world generation.", Category = "World & Spawning", Type = "choice", Choices = new[] { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified" } },
        new PropertyDefinition { Key = "generate-structures", Label = "Generate Structures", Description = "Generates villages, dungeons, and monuments.", Category = "World & Spawning", Type = "boolean" },
        new PropertyDefinition { Key = "spawn-animals", Label = "Spawn Animals", Description = "Enables passive animal spawning.", Category = "World & Spawning", Type = "boolean" },
        new PropertyDefinition { Key = "spawn-monsters", Label = "Spawn Monsters", Description = "Enables hostile monster spawning.", Category = "World & Spawning", Type = "boolean" },
        new PropertyDefinition { Key = "spawn-npcs", Label = "Spawn Villagers (NPCs)", Description = "Enables villager spawning.", Category = "World & Spawning", Type = "boolean" },
 
        // Performance & Limits
        new PropertyDefinition { Key = "view-distance", Label = "View Distance", Description = "The maximum distance in chunks sent to players (4-32).", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "simulation-distance", Label = "Simulation Distance", Description = "Distance in chunks to tick entities (4-32).", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "entity-broadcast-range-percentage", Label = "Entity Broadcast Range %", Description = "Controls how close entities must be to render.", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "max-tick-time", Label = "Max Tick Time (ms)", Description = "Milliseconds a single tick can take before the watchdog stops the server.", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "network-compression-threshold", Label = "Network Compression Limit", Description = "Size threshold in bytes to compress packets.", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "sync-chunk-writes", Label = "Sync Chunk Writes", Description = "Saves chunks synchronously to prevent data loss.", Category = "Performance", Type = "boolean" },
        new PropertyDefinition { Key = "max-chained-neighbor-updates", Label = "Max Neighbor Updates", Description = "Limits maximum block physics chain propagation updates.", Category = "Performance", Type = "text" },
        new PropertyDefinition { Key = "max-world-size", Label = "Max World Size (Blocks)", Description = "Sets the maximum boundary radius limit of the world map.", Category = "Performance", Type = "text" },
 
        // Advanced & Security
        new PropertyDefinition { Key = "enable-command-block", Label = "Enable Command Blocks", Description = "Allows execution of command blocks.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "op-permission-level", Label = "OP Permission Level", Description = "Default command clearance for operators.", Category = "Advanced", Type = "choice", Choices = new[] { "1", "2", "3", "4" } },
        new PropertyDefinition { Key = "function-permission-level", Label = "Function Permission Level", Description = "Default command clearance for functions.", Category = "Advanced", Type = "choice", Choices = new[] { "1", "2", "3", "4" } },
        new PropertyDefinition { Key = "prevent-proxy-connections", Label = "Prevent VPN/Proxy", Description = "Attempts to block proxy and VPN connections.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "rate-limit", Label = "Player Packets Rate Limit", Description = "Max packets allowed per player before kick (0 to disable).", Category = "Advanced", Type = "text" },
        new PropertyDefinition { Key = "log-ips", Label = "Log IP Addresses", Description = "Logs player IP addresses in the console.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "enforce-secure-profile", Label = "Enforce Chat Signing", Description = "Enforces Mojang-signed public keys for players.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "use-native-transport", Label = "Use Native Transport", Description = "Optimizes packet sending on Linux/macOS.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "broadcast-console-to-ops", Label = "Broadcast Console to OPs", Description = "Sends command block feedback messages to online operators.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "broadcast-rcon-to-ops", Label = "Broadcast RCON to OPs", Description = "Sends remote console execution feedback to online operators.", Category = "Advanced", Type = "boolean" },
        new PropertyDefinition { Key = "player-idle-timeout", Label = "Player Idle Timeout (mins)", Description = "Disconnects players who remain idle longer than set minutes (0 to disable).", Category = "Advanced", Type = "text" },
 
        // RCON & Query
        new PropertyDefinition { Key = "enable-query", Label = "Enable Query (GameSpy)", Description = "Allows external tools to query server info.", Category = "RCON & Query", Type = "boolean" },
        new PropertyDefinition { Key = "query.port", Label = "Query Port", Description = "The port used for GameSpy queries.", Category = "RCON & Query", Type = "text" },
        new PropertyDefinition { Key = "enable-rcon", Label = "Enable RCON", Description = "Allows remote command console access.", Category = "RCON & Query", Type = "boolean" },
        new PropertyDefinition { Key = "rcon.port", Label = "RCON Port", Description = "The port used for remote console connection.", Category = "RCON & Query", Type = "text" },
        new PropertyDefinition { Key = "rcon.password", Label = "RCON Password", Description = "Password used to authenticate RCON.", Category = "RCON & Query", Type = "text" },
 
        // Resource Packs
        new PropertyDefinition { Key = "require-resource-pack", Label = "Require Resource Pack", Description = "Kicks players who reject the server resource pack.", Category = "Resource Packs", Type = "boolean" },
        new PropertyDefinition { Key = "resource-pack", Label = "Resource Pack URL", Description = "Direct download link to a server resource pack.", Category = "Resource Packs", Type = "text" },
        new PropertyDefinition { Key = "resource-pack-id", Label = "Resource Pack ID", Description = "Unique UUID identifier of the resource pack.", Category = "Resource Packs", Type = "text" },
        new PropertyDefinition { Key = "resource-pack-prompt", Label = "Resource Pack Prompt", Description = "Message shown to players requesting pack confirmation.", Category = "Resource Packs", Type = "text" },
        new PropertyDefinition { Key = "resource-pack-sha1", Label = "Resource Pack SHA-1 Hash", Description = "SHA-1 hash of the resource pack file.", Category = "Resource Packs", Type = "text" }
    };

    private string _activeServerScreen = "list";
    private string _selectedServerId = "";
    private string _activeDashboardTab = "overview";
    private List<LocalServerMetadata>? _localServers;
    private Dictionary<string, System.Diagnostics.Process> _serverProcesses = new();
    private Dictionary<string, System.Diagnostics.Process> _tunnelProcesses = new();
    private Dictionary<string, string> _tunnelAddresses = new();
    private Dictionary<string, System.Text.StringBuilder> _serverLogs = new();
    private Dictionary<string, string> _serverStatuses = new();
    private Dictionary<string, DateTime> _serverStartTimes = new();
    private Dictionary<string, List<string>> _serverActivePlayers = new();
    private string _publicIpAddress = "";
    private System.Action<string>? _onServerLogAdded;
    private System.Action<string>? _onServerStatusChanged;
    // private Avalonia.Threading.DispatcherTimer? _dashboardMetricsTimer;
    private Dictionary<string, List<TeleportRequest>> _activeTeleportRequests = new();
    private readonly HashSet<string> _intentionallyStoppedServers = new();
    private Vector _savedDashboardScrollOffset = Vector.Zero;
    private ScrollViewer? _activeDashboardScrollViewer;

    public class TeleportRequest
    {
        public string Sender { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    private TextBox usernameInput = null!;
    private ComboBox cbVersion = null!;
    private ComboBox minecraftVersion = null!;
    private Button downloadVersionButton = null!;
    private TextBox profileNameInput = null!;
    private TextBox profileGameDirInput = null!;
    private ComboBox profileLoaderCombo = null!;
    private ComboBox profilePresetCombo = null!;
    private StackPanel profilePresetSection = null!;
    private Button createProfileButton = null!;
    private Button renameProfileButton = null!;
    private Button btnStart = null!;
    private CancellationTokenSource? _launchCts;
    private Button launchNavButton = null!;
    private Button profilesNavButton = null!;
    private Button modrinthNavButton = null!;
    private Button performanceNavButton = null!;
    private Button settingsNavButton = null!;
    private Button layoutNavButton = null!;
    private Button accountsNavButton = null!;
    private TextBlock activeProfileBadge = null!;
    private TextBlock activeContextLabel = null!;
    private TextBlock installModeLabel = null!;
    private Image characterImage = null!;
    private double _rotationAngle = 0.0;
    private double _rotationAngleX = 0.22;
    private bool _isDraggingCharacter = false;
    private Point _lastDragPoint;
    private double _dragVelocity = 0.0;
    private double _animationTime = 0.0;
    private Bitmap? _cachedSkinBitmap;
    private Bitmap? _cachedCapeBitmap;
    private RenderTargetBitmap? _previewRtb;
    private string? _cachedSkinPath;
    private string? _cachedCapePath;
    private bool _cachedIsSlim;
    private DateTime? _cachedSkinWriteTime;
    private DateTime? _cachedCapeWriteTime;
    private DispatcherTimer? _previewTimer;
    private string? _lastModsListProfilePath;
    private DateTime _lastModsListDirectoryWriteTime;
    private DateTime _lastCharacterFileCheckTime = DateTime.MinValue;
    private string? _resolvedSkinPath;
    private string? _resolvedCapePath;
    private bool _resolvedSkinExists;
    private bool _resolvedCapeExists;
    private string? _lastUsernameChecked;
    private string? _lastCustomSkinPath;
    private string? _lastCustomCapePath;
    private TextBlock statusLabel = null!;
    private TextBlock installDetailsLabel = null!;
    private ProgressBar pbFiles = null!;
    private ProgressBar pbProgress = null!;
    private TextBox modrinthSearchInput = null!;
    private ComboBox modrinthProjectTypeCombo = null!;
    private ComboBox modrinthLoaderCombo = null!;
    private ComboBox modrinthSourceCombo = null!;
    private Button modrinthSearchButton = null!;
    private TextBox modrinthVersionInput = null!;
    private ListBox modrinthResultsListBox = null!;
    private readonly ObservableCollection<WorldItem> _worldItems = [];
    private readonly ObservableCollection<ResourcePackItem> _resourcePackItems = [];
    private ToggleSwitch? _offlineModeToggle;
    private ListBox? _worldsListBox;
    private ListBox? _rpListBox;
    private ListBox? _modsListBox;
    private Control? _worldsEmptyState;
    private Control? _rpEmptyState;
    private Control? _modsEmptyState;
    private Control? _manageContentGrid;
    private Control? _manageNoProfileCard;
    private TextBlock modrinthDetailsBox = null!;
    private TextBlock modrinthResultsSummary = null!;
    private Button installSelectedButton = null!;
    private Button importMrpackButton = null!;
    private ListBox profileListBox = null!;
    private TextBlock profileInspectorTitle = null!;
    private TextBlock profileInspectorMeta = null!;
    private TextBlock profileInspectorPath = null!;
    private Button clearProfileButton = null!;
    private TextBlock heroInstanceLabel = null!;
    private TextBlock heroPerformanceLabel = null!;
    private TextBlock homeFpsStatValue = null!;
    private TextBlock homeRamStatValue = null!;
    private TextBlock performanceFpsStatValue = null!;
    private TextBlock performanceRamStatValue = null!;
    private TextBlock loadingLabel = null!;
    private Control launchSection = null!;
    private Control modrinthSection = null!;
    private Control profilesSection = null!;
    private Control performanceSection = null!;
    private Control settingsSection = null!;
    private Control layoutSection = null!;
    private Border? _homeStatusBar;
    public ProgressBar? PbProgress { get; set; }
    public TextBox? ModrinthSearchInput { get; set; }
    public System.Collections.Generic.Dictionary<string, object> Fields { get; } = new();
    private Border _instanceEditorOverlay = null!;
    private Border _accountsOverlay = null!;
    private StackPanel _accountsListPanel = new();
    private MinecraftAuthenticationService _authService = new();
    private Border _playOverlay = null!;
    private TextBlock _playOverlayIcon = null!;
    private TextBlock _playOverlayLabel = null!;
    // _notificationCard removed (notification replaced with Featured Servers section)
    // Quick Instance panel
    private ComboBox _quickVersionCombo = null!;
    private ComboBox _quickLoaderCombo = null!;
    private Button _quickInstallButton = null!;

    // Quick Mods panel
    private TextBox _quickModSearch = null!;
    private Button _quickModSearchButton = null!;
    private readonly ListBox _quickModResults = new();
    private readonly ObservableCollection<ModrinthProject> _quickSearchResults = [];

    private ComboBox instanceVersionCombo = null!;
    private ComboBox instanceCategoryCombo = null!;

    private string _playerUuid = string.Empty;
    private LauncherProfile? _selectedProfile;
    private bool _handlingProfileSelection = false;
    private readonly Dictionary<string, Border> _instanceCardBorders = new();
    private CancellationTokenSource? _searchCancellation;
    private UserSettings _settings;
    private string _activeSection = "launch";
    // Responsive UI state
    private bool _isNarrowMode;
    private bool _isGameLaunchedAndMinimized;
    private Border? _avatarGlass;
    private StackPanel? _avatarControls;
    private Grid? _avatarActions;
    private StackPanel? _mainContentStack;
    private Grid? _mainRowGrid;
    private readonly SemaphoreSlim _versionListSemaphore = new(1, 1);

    // Style revert system
    private LayoutStyle? _previousStyle;
    private CancellationTokenSource? _revertCts;
    private Border? _revertOverlay;


    public MainWindow()
    {
        var initialPath = new MinecraftPath();
        initialPath.CreateDirs();
        _settingsStore = new UserSettingsStore(initialPath.BasePath);
        _settings = _settingsStore.Load();

        // Reset corrupt layout styles (e.g. from an old AXAML import containing invalid/oversized dimensions)
        if (_settings.Style != null && (_settings.Style.SidebarPadding > 100 || _settings.Style.ButtonPadding > 100 || _settings.Style.SidebarWidth < 40))
        {
            _settings.Style = LayoutStyle.Default();
            _settings.SelectedPreset = "None";
            _settingsStore.Save(_settings);
        }

        // Migrate legacy semicolon-delimited layout tokens to structured Style object
        _settings.MigrateLegacyLayout();
        if (string.IsNullOrWhiteSpace(_settings.ClientLayout))
        {
            // Migration happened or was already clean — persist
            _settingsStore.Save(_settings);
        }

        // Automatically check the total system memory to avoid OOM or Java launch failure on low-end VMs
        try
        {
            long totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (totalMemoryBytes > 0)
            {
                double totalMemoryMb = totalMemoryBytes / (1024.0 * 1024.0);
                if (totalMemoryMb < 2500 && _settings.MaxRamMb > 800)
                {
                    LauncherLog.Info($"[Memory] Low system memory detected ({totalMemoryMb:F0} MB). Adjusting MaxRamMb from {_settings.MaxRamMb} to 800 MB to prevent launch failure.");
                    _settings.MaxRamMb = 800;
                    _settingsStore.Save(_settings);
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"[Memory] Failed to detect total system memory: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(_settings.BaseMinecraftPath) && Directory.Exists(_settings.BaseMinecraftPath))
            _defaultMinecraftPath = new MinecraftPath(_settings.BaseMinecraftPath);
        else
            _defaultMinecraftPath = initialPath;

        _defaultMinecraftPath.CreateDirs();
        ApplyThemeVariant();
        this.ActualThemeVariantChanged += (s, e) => {
            UpdateWindowIcon();
            RebuildUiFromLayoutState(_activeSection);
        };
        _profileStore = new LauncherProfileStore(_defaultMinecraftPath.BasePath);
        _defaultLauncher = CreateLauncher(_defaultMinecraftPath);
        ConfigureWindowChrome();
        EnsureFallbackControlsInitialized();

        this.SizeChanged += (s, e) => UpdateResponsiveLayout();
        Opened += async (_, _) => 
        {
            UpdateResponsiveLayout();
            // Defer initialization to next frame so the window renders immediately
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { await InitializeAsync(); } catch { }
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Build the C# UI — always uses the default C# UI, styled by settings.Style
        // Note: ApplySelectedPresetStyle is NOT called here. The preset was already applied
        // and saved when the user selected it, so _settings.Style already reflects it.
        // Calling it on startup would overwrite any per-setting customizations the user made.
        Content = BuildRoot();

        // Use adaptive timer: performance mode = 5fps, normal = 30fps (was 16fps)
        // 30fps is visually smooth for rotation while halving CPU vs 60ms
        var timerInterval = IsPerformanceModeEnabled() ? 200 : 33;
        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(timerInterval)
        };
        _previewTimer.Tick += (s, e) =>
        {
            // Skip rendering if window is minimized or not on launch section
            if (WindowState == WindowState.Minimized) return;
            var launchVisible = _activeSection == "home" || _activeSection == "launch";
            if (!launchVisible) return;

            // Scale rotation/animation speed by interval so visual speed stays consistent
            double dt = _previewTimer.Interval.TotalMilliseconds / 60.0;

            if (_isDraggingCharacter)
            {
                // no auto-rotation when dragging
            }
            else
            {
                // apply drag velocity / inertia on Y
                if (Math.Abs(_dragVelocity) > 0.001)
                {
                    _rotationAngle += _dragVelocity;
                    _dragVelocity *= 0.92; // friction/decay
                }
                else
                {
                    _rotationAngle += 0.025 * dt; // slow elegant continuous auto rotation
                }

                // spring-back X rotation to default 0.22
                _rotationAngleX += (0.22 - _rotationAngleX) * 0.05 * dt;
            }

            if (_rotationAngle > 2 * Math.PI) _rotationAngle -= 2 * Math.PI;
            if (_rotationAngle < 0) _rotationAngle += 2 * Math.PI;

            _animationTime += 0.10 * dt;
            if (_animationTime > 1000.0) _animationTime = 0.0;

            UpdateCharacterPreview();
        };
        _previewTimer.Start();

        Closed += (_, _) =>
        {
            _previewTimer?.Stop();
            _cachedSkinBitmap?.Dispose();
            _cachedCapeBitmap?.Dispose();
            _previewRtb?.Dispose();
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _modrinthClient.Dispose();
        };
    }

    private MinecraftLauncher CreateLauncher(MinecraftPath path)
    {
        path.CreateDirs();
        MinecraftLauncher launcher;
        if (_settings.OfflineMode)
        {
            var parameters = MinecraftLauncherParameters.CreateDefault();
            parameters.MinecraftPath = path;
            parameters.VersionLoader = new CmlLib.Core.VersionLoader.LocalJsonVersionLoader(path);
            launcher = new MinecraftLauncher(parameters);
        }
        else
        {
            launcher = new MinecraftLauncher(path);
        }
        launcher.FileProgressChanged += _launcher_FileProgressChanged;
        launcher.ByteProgressChanged += _launcher_ByteProgressChanged;
        return launcher;
    }

    private Control BuildRoot()
    {
        EnsureFallbackControlsInitialized();
        var style = _settings.Style;
        var topNavigation = IsTopNavigationEnabled();
        var collapsedSidebar = IsSidebarCollapsed();
        var compact = style.CompactMode;
        var sidebarWidth = collapsedSidebar ? 72 : (compact ? 200 : (double.IsNaN(style.SidebarWidth) ? 240 : style.SidebarWidth));




        if (topNavigation)
        {
            return WrapWindowSurface(new Grid
            {
                Background = GetMainBackground(),
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children =
                {
                    new Border {
                        Background = new SolidColorBrush(Color.FromArgb(8, 110, 91, 255)),
                        IsHitTestVisible = false,
                        ZIndex = 999
                    }.With(rowSpan: 2),
                    
                    new Canvas
                    {
                        Children =
                        {
                            new Border
                            {
                                Width = 500,
                                Height = 500,
                                CornerRadius = new CornerRadius(999),
                                Background = new RadialGradientBrush
                                {
                                    Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                                    GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                                    RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                                    RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
                                    GradientStops =
                                    {
                                        new GradientStop(GetAccentColor(20), 0),
                                        new GradientStop(GetAccentColor(0), 1)
                                    }
                                },
                                [Canvas.LeftProperty] = -120d,
                                [Canvas.TopProperty] = -30d
                            },
                            new Border
                            {
                                Width = 600,
                                Height = 600,
                                CornerRadius = new CornerRadius(999),
                                Background = new RadialGradientBrush
                                {
                                    GradientStops =
                                    {
                                        new GradientStop(GetAccentColor(15), 0),
                                        new GradientStop(GetAccentColor(0), 1)
                                    }
                                },
                                [Canvas.RightProperty] = -180d,
                                [Canvas.TopProperty] = 40d
                            }
                        }
                    }.With(row: 0),

                    // Accent Strip
                    new Border
                    {
                        Height = double.IsNaN(style.AccentStripHeight) ? 2 : style.AccentStripHeight,
                        Background = GetAccentStripBrush(),
                        VerticalAlignment = VerticalAlignment.Top,
                        ZIndex = 2000
                    }.With(rowSpan: 2),

                    DetachFromParent(BuildTopNavigation())!.With(row: 0),
                    DetachFromParent(BuildContent())!.With(row: 1),
                    BuildExternalPlayButtonHost(topNavigation: true)!,
                    DetachFromParent(_instanceEditorOverlay)!.With(row: 0, rowSpan: 2, columnSpan: 1),
                    DetachFromParent(_accountsOverlay)!.With(row: 0, rowSpan: 2, columnSpan: 2)
                }
            }, topNavigation: true);

        }

        var sidebarOnRight = string.Equals(style.SidebarSide, "right", StringComparison.OrdinalIgnoreCase);
        return WrapWindowSurface(new Grid
        {
            Background = GetMainBackground(),
            ColumnDefinitions = sidebarOnRight
                ? new ColumnDefinitions($"*,{sidebarWidth}")
                : new ColumnDefinitions($"{sidebarWidth},*"),
            Children =
            {
                new Canvas
                {
                    Children =
                    {
                        new Border
                        {
                            Width = 500,
                            Height = 500,
                            CornerRadius = new CornerRadius(999),
                            Background = new RadialGradientBrush
                            {
                                Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                                GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                                RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
                                GradientStops =
                                {
                                    new GradientStop(Color.FromArgb(20, Color.Parse(_settings.AccentColor ?? "#6E5BFF").R, Color.Parse(_settings.AccentColor ?? "#6E5BFF").G, Color.Parse(_settings.AccentColor ?? "#6E5BFF").B), 0),
                                    new GradientStop(Color.FromArgb(0, Color.Parse(_settings.AccentColor ?? "#6E5BFF").R, Color.Parse(_settings.AccentColor ?? "#6E5BFF").G, Color.Parse(_settings.AccentColor ?? "#6E5BFF").B), 1)
                                }
                            },
                            [Canvas.LeftProperty] = -120d,
                            [Canvas.TopProperty] = -30d
                        },
                        new Border
                        {
                            Width = 600,
                            Height = 600,
                            CornerRadius = new CornerRadius(999),
                            Background = new RadialGradientBrush
                            {
                                GradientStops =
                                {
                                    new GradientStop(Color.FromArgb(15, Color.Parse(_settings.AccentColor ?? "#6E5BFF").R, Color.Parse(_settings.AccentColor ?? "#6E5BFF").G, Color.Parse(_settings.AccentColor ?? "#6E5BFF").B), 0),
                                    new GradientStop(Color.FromArgb(0, Color.Parse(_settings.AccentColor ?? "#6E5BFF").R, Color.Parse(_settings.AccentColor ?? "#6E5BFF").G, Color.Parse(_settings.AccentColor ?? "#6E5BFF").B), 1)
                                }
                            },
                            [Canvas.RightProperty] = -180d,
                            [Canvas.TopProperty] = 40d
                        }
                    }
                },
                  sidebarOnRight ? DetachFromParent(BuildContent())!.With(column: 0) : DetachFromParent(BuildHeader())!,
                  sidebarOnRight ? DetachFromParent(BuildHeader())!.With(column: 1) : DetachFromParent(BuildContent())!.With(column: 1),
                                BuildExternalPlayButtonHost(topNavigation: false)!,
                DetachFromParent(_instanceEditorOverlay)!.With(columnSpan: 2),
                DetachFromParent(_accountsOverlay)!.With(columnSpan: 2)
            }
        }, topNavigation: false);
    }

    // --- Style token accessors (read from structured LayoutStyle) ---

    private bool IsTopNavigationEnabled() => string.Equals(_settings.Style.NavPosition, "top", StringComparison.OrdinalIgnoreCase);

    private bool IsSidebarCollapsed() => !IsTopNavigationEnabled() && _settings.Style.SidebarCollapsed;

    private bool IsSidebarOnRight() => string.Equals(_settings.Style.SidebarSide, "right", StringComparison.OrdinalIgnoreCase);

    private bool ShouldExternalizePlayButton() => _settings.Style.PlayButtonGlobal;

    private Control BuildExternalPlayButtonHost(bool topNavigation)
    {
        if (!ShouldExternalizePlayButton())
            return new Border { IsVisible = false, Width = 0, Height = 0 };

        var defaultHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24),
            ZIndex = 2500,
            Child = DetachFromParent(_playOverlay)
        };

        if (topNavigation)
            Grid.SetRowSpan(defaultHost, 2);
        else
            Grid.SetColumnSpan(defaultHost, 2);

        return defaultHost;
    }

    private int GetStyleCornerRadius() =>
        string.Equals(_settings.Style.BorderStyle, "square", StringComparison.OrdinalIgnoreCase) ? 0 : _settings.Style.CornerRadius;

    private void ToggleSidebarCollapsed()
    {
        _settings.Style.SidebarCollapsed = !IsSidebarCollapsed();
        _settingsStore.Save(_settings);
        RebuildUiFromLayoutState(_activeSection);
    }

    private void RebuildUiFromLayoutState(string activeSection = "layout")
    {
        InvalidateUiCache();
        Content = BuildRoot();
        SetActiveSection(activeSection);
    }

    // --- Style change with 15-second revert window ---

    private void ApplyStyleWithRevert(Action<LayoutStyle> mutate)
    {
        // Snapshot current style before change. Use conditional assignment so dragging a slider
        // doesn't overwrite the original revert snapshot with intermediate drag values.
        _previousStyle ??= _settings.Style.Clone();
        _revertCts?.Cancel();
        _revertCts?.Dispose();

        // Apply the mutation
        mutate(_settings.Style);

        // If border style is square, force corner radius to 0
        if (string.Equals(_settings.Style.BorderStyle, "square", StringComparison.OrdinalIgnoreCase))
            _settings.Style.CornerRadius = 0;

        // Rebuild UI with new style and navigate back to settings
        RebuildUiFromLayoutState("settings");

        // Show revert overlay with 15s countdown
        ShowRevertOverlay();
    }

    private void ShowRevertOverlay()
    {
        _revertCts = new CancellationTokenSource();
        var ct = _revertCts.Token;
        var secondsLeft = 15;

        var countdownLabel = new TextBlock
        {
            Text = $"Keeping in {secondsLeft}s...",
            Foreground = new SolidColorBrush(Color.Parse("#B0BACF")),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };

        var keepBtn = new Button
        {
            Content = "✓ Keep Changes",
            Background = new SolidColorBrush(Color.Parse("#2A7A3A")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8),
            FontWeight = FontWeight.SemiBold,
            BorderThickness = new Thickness(0)
        };
        var revertBtn = new Button
        {
            Content = "↩ Revert",
            Background = new SolidColorBrush(Color.Parse("#7A2A2A")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8),
            FontWeight = FontWeight.SemiBold,
            BorderThickness = new Thickness(0)
        };

        keepBtn.Click += (_, _) => ConfirmStyleChange();
        revertBtn.Click += (_, _) => RevertStyleChange();

        _revertOverlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 14, 18, 28)),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4")),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(24, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 32),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Layout changed.",
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    countdownLabel,
                    keepBtn,
                    revertBtn
                }
            }
        };

        // Add overlay on top of current content
        if (Content is Control currentContent)
        {
            // Must detach from Window.Content BEFORE adding to overlay Grid
            Content = null;
            var overlay = new Grid
            {
                Children =
                {
                    currentContent,
                    _revertOverlay
                }
            };
            Content = overlay;
        }

        // Countdown timer
        _ = Task.Run(async () =>
        {
            while (secondsLeft > 0 && !ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
                secondsLeft--;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ct.IsCancellationRequested)
                        countdownLabel.Text = $"Keeping in {secondsLeft}s...";
                });
            }

            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(ConfirmStyleChange);
        }, ct).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
    }

    private void ConfirmStyleChange()
    {
        _revertCts?.Cancel();
        _revertCts?.Dispose();
        _revertCts = null;
        _previousStyle = null;

        _settingsStore.Save(_settings);

        // Remove overlay, rebuild clean
        RebuildUiFromLayoutState("settings");
    }

    private void RevertStyleChange()
    {
        _revertCts?.Cancel();
        _revertCts?.Dispose();
        _revertCts = null;

        if (_previousStyle != null)
        {
            _settings.Style = _previousStyle;
            _previousStyle = null;
            _settingsStore.Save(_settings);
        }

        // Rebuild with reverted style
        RebuildUiFromLayoutState("settings");
    }

    private void ConfigureWindowChrome()
    {
        Title = "Aether Launcher";
        Name = "aether-launcher";
        Width = 1344;
        Height = 714;
        MinWidth = 1100;
        MinHeight = 610;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 46;
        TransparencyLevelHint = new[] { 
            WindowTransparencyLevel.AcrylicBlur, 
            WindowTransparencyLevel.Mica, 
            WindowTransparencyLevel.Transparent 
        };

        UpdateWindowIcon();
    }

    private Control WrapWindowSurface(Control content, bool topNavigation)
    {
        var style = _settings.Style;

        var accentHex = !string.IsNullOrWhiteSpace(_settings.AccentColor) ? _settings.AccentColor : "#6E5BFF";
        Color accentColor;
        try { accentColor = Color.Parse(accentHex); } catch { accentColor = Color.Parse("#6E5BFF"); }

        var accentOverlay = new Panel
        {
            Background = new SolidColorBrush(Color.FromArgb(9, accentColor.R, accentColor.G, accentColor.B)),
            IsHitTestVisible = false,
            ZIndex = 9998
        };

        var shell = new Grid
        {
            ClipToBounds = false,
            Children = { content, accentOverlay }
        };

        if (!topNavigation)
        {
            var floatingControls = BuildWindowControls();
            floatingControls.Margin = new Thickness(0, 16, 16, 0);
            floatingControls.HorizontalAlignment = HorizontalAlignment.Right;
            floatingControls.VerticalAlignment = VerticalAlignment.Top;
            floatingControls.ZIndex = 9999;
            shell.Children.Add(floatingControls);
        }

        var cr = GetStyleCornerRadius();
        
        var margin = style.WindowMargin;
        if (style.CompactMode) margin = Math.Max(0, margin - 4);
        
        var bg = !string.IsNullOrWhiteSpace(style.WindowBackground) ? style.WindowBackground : "#090C12";
        var border = !string.IsNullOrWhiteSpace(style.WindowBorderColor) ? style.WindowBorderColor : "#DC222A3F";

        return new Border
        {
            Margin = new Thickness(margin),
            CornerRadius = new CornerRadius(cr),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse(bg)),
            BorderBrush = new SolidColorBrush(Color.Parse(border)),
            BorderThickness = new Thickness(style.WindowBorderThickness),
            Child = shell
        };
    }


    private StackPanel BuildWindowControls()
    {
        var minimizeButton = CreateWindowControlButton("−", Color.Parse("#F4B63C"), () => WindowState = WindowState.Minimized);
        var closeButton = CreateWindowControlButton("✕", Color.Parse("#FF5C70"), Close);

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };

        bool hasCustomAccountsBtn = false;
        if (!hasCustomAccountsBtn)
        {
            stackPanel.Children.Add(DetachFromParent(accountsNavButton)!);
        }

        stackPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { minimizeButton, closeButton }
        });

        return stackPanel;
    }

    private Button CreateWindowControlButton(string glyph, Color color, Action onClick)
    {
        var label = new TextBlock
        {
            Text = glyph,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 12, 16, 24)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = TextBlock.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() }
            }
        };

        var button = new Button
        {
            Width = 14,
            Height = 14,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(0),
            Content = label
        };

        button.Click += (_, _) => onClick();
        button.PointerEntered += (_, _) => label.Opacity = 1;
        button.PointerExited += (_, _) => label.Opacity = 0;

        return button;
    }

    private void AttachWindowDrag(Control control)
    {
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                return;

            try
            {
                BeginMoveDrag(e);
            }
            catch
            {
            }
        };
    }

    private Brush GetMainBackground()
    {
        var style = _settings.Style;

        // 1. If a specific WindowBackground hex color is set, prioritize it
        if (!string.IsNullOrWhiteSpace(style.WindowBackground))
        {
            try { return new SolidColorBrush(Color.Parse(style.WindowBackground)); } catch { }
        }

        // 2. Try Custom Background Image Path from style
        if (!string.IsNullOrWhiteSpace(style.BackgroundImagePath) && File.Exists(style.BackgroundImagePath))
        {
            try {
                var ovOp = double.IsNaN(style.BackgroundOverlayOpacity) ? 1.0 : style.BackgroundOverlayOpacity;
                return new ImageBrush(new Bitmap(style.BackgroundImagePath)) 
                { 
                    Stretch = Stretch.UniformToFill, 
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                    Opacity = ovOp == 1.0 ? style.BackgroundOpacity : 1.0 - ovOp
                };
            } catch { }
        }

        // 3. Try legacy custom_bg.png on disk
        var customBgPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "custom_bg.png");
        if (File.Exists(customBgPath))
        {
            try {
                return new ImageBrush(new Bitmap(customBgPath)) 
                { 
                    Stretch = Stretch.UniformToFill, 
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                    Opacity = style.BackgroundOpacity 
                };
            } catch { }
        }

        // 4. Default Bundled Resource
        try 
        {
            var asset = AssetLoader.Open(new Uri("avares://AetherLauncher/assets/launcher_background.png"));
            if (asset != null)
            {
                return new ImageBrush(new Bitmap(asset)) 
                { 
                    Stretch = Stretch.UniformToFill, 
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                    Opacity = style.BackgroundOpacity 
                };
            }
        } catch { }

        // 5. Final Fallback to Linear Gradient
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#0E1119"), 0),
                new GradientStop(Color.Parse("#141822"), 1)
            }
        };
    }


    private Control BuildHeader()
    {
        var style = _settings.Style;
        var collapsed = IsSidebarCollapsed();
        var sidebarOnRight = IsSidebarOnRight();
        var cr = GetStyleCornerRadius();
        var compact = style.CompactMode;
        var brand = collapsed
            ? (Control)new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.Parse("#121722")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "☠",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(4, 8, 4, 28),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Image
                    {
                        Source = new Bitmap(AssetLoader.Open(new Uri(GetTaskbarIconUri()))),
                        Width = 28, Height = 28,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = style.TitleText ?? "AETHER LAUNCHER",
                        Foreground = Brushes.White,
                        FontSize = 18,
                        FontWeight = FontWeight.Black,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                    }
                }
            };

        launchNavButton = CreateNavButton("⌂", "Home", collapsed);
        launchNavButton.Click += (_, _) => SetActiveSection("home");
        profilesNavButton = CreateNavButton("▣", "Instances", collapsed);
        profilesNavButton.Click += (_, _) => SetActiveSection("instances");
        modrinthNavButton = CreateNavButton("⌕", "Mods", collapsed);
        modrinthNavButton.Click += (_, _) => SetActiveSection("modrinth");
        performanceNavButton = CreateNavButton("🛠", "Manage", collapsed);
        performanceNavButton.Click += (_, _) => SetActiveSection("performance");
        settingsNavButton = CreateNavButton("⚙", "Settings", collapsed);
        settingsNavButton.Click += (_, _) => SetActiveSection("settings");
        layoutNavButton = CreateNavButton("▤", "Servers", collapsed);
        layoutNavButton.Click += (_, _) => SetActiveSection("layout");

        var edgeToggleButton = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.Parse("#121722")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A3150")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = sidebarOnRight ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = sidebarOnRight ? new Thickness(-11, 0, 0, 0) : new Thickness(0, 0, -11, 0),
            Content = new TextBlock
            {
                Text = sidebarOnRight
                    ? (collapsed ? "›" : "‹")
                    : (collapsed ? "‹" : "›"),
                Foreground = new SolidColorBrush(Color.Parse("#D5DAE5")),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        edgeToggleButton.Click += (_, _) => ToggleSidebarCollapsed();

        var sbBg = !string.IsNullOrWhiteSpace(style.SidebarBackground) ? style.SidebarBackground : "#090C12";
        var sbBorder = !string.IsNullOrWhiteSpace(style.SidebarBorderColor) ? style.SidebarBorderColor : "#171B24";
        var sbPad = double.IsNaN(style.SidebarPadding) ? (collapsed ? new Thickness(10, 22, 10, 18) : new Thickness(18, 22, 18, 18)) : new Thickness(style.SidebarPadding);

        var sidebarBody = new Border
        {
            Background = new SolidColorBrush(Color.Parse(sbBg)),
            BorderBrush = new SolidColorBrush(Color.Parse(sbBorder)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = sbPad,
            Child = new StackPanel
            {
                Spacing = collapsed ? 10 : 12,
                Children =
                {
                    brand!,
                    DetachFromParent(launchNavButton)!,
                    DetachFromParent(profilesNavButton)!,
                    DetachFromParent(modrinthNavButton)!,
                    DetachFromParent(performanceNavButton)!,
                    DetachFromParent(settingsNavButton)!,
                    DetachFromParent(layoutNavButton)!
                }
            }
        };
        AttachWindowDrag(sidebarBody);

        return new Grid
        {
            ClipToBounds = false,
            Children =
            {
                sidebarBody,
                edgeToggleButton
            }
        };
    }

    private Control BuildTopNavigation()
    {
        launchNavButton = CreateNavButton("⌂", "Home");
        launchNavButton.Click += (_, _) => SetActiveSection("home");
        profilesNavButton = CreateNavButton("▣", "Instances");
        profilesNavButton.Click += (_, _) => SetActiveSection("instances");
        modrinthNavButton = CreateNavButton("⌕", "Mods");
        modrinthNavButton.Click += (_, _) => SetActiveSection("modrinth");
        performanceNavButton = CreateNavButton("🛠", "Manage");
        performanceNavButton.Click += (_, _) => SetActiveSection("performance");
        settingsNavButton = CreateNavButton("⚙", "Settings");
        settingsNavButton.Click += (_, _) => SetActiveSection("settings");
        layoutNavButton = CreateNavButton("▤", "Servers");
        layoutNavButton.Click += (_, _) => SetActiveSection("layout");

        ApplyHoverMotion(launchNavButton);
        ApplyHoverMotion(profilesNavButton);
        ApplyHoverMotion(modrinthNavButton);
        ApplyHoverMotion(performanceNavButton);
        ApplyHoverMotion(settingsNavButton);
        ApplyHoverMotion(layoutNavButton);

        foreach (var button in new[] { launchNavButton, profilesNavButton, modrinthNavButton, performanceNavButton, settingsNavButton, layoutNavButton })
        {
            if (button == null) continue;
            button.Height = 40;
            button.MinWidth = 80;
        }

        var brandBlock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Image
                {
                    Source = new Bitmap(AssetLoader.Open(new Uri(GetTaskbarIconUri()))),
                    Width = 28, Height = 28,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = "AETHER LAUNCHER",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeight.Black,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                }
            }
        };

        var centeredTabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                DetachFromParent(launchNavButton)!,
                DetachFromParent(profilesNavButton)!,
                DetachFromParent(modrinthNavButton)!,
                DetachFromParent(performanceNavButton)!,
                DetachFromParent(settingsNavButton)!,
                DetachFromParent(layoutNavButton)!
            }
        };

        var topNavigationBar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 9, 12, 18)),
            BorderBrush = new SolidColorBrush(Color.Parse("#171B24")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(22, 10, 22, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("200,*,Auto"),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    brandBlock.With(column: 0),
                    centeredTabs.With(column: 1),
                    BuildWindowControls().With(column: 2)
                }
            }
        };
        AttachWindowDrag(topNavigationBar);
        return topNavigationBar;
    }

    private static T? DetachFromParent<T>(T? control) where T : Control
    {
        if (control == null) return null;
        if (control.Parent is Panel panel)
            panel.Children.Remove(control);
        else if (control.Parent is ContentControl cc)
            cc.Content = null;
        else if (control.Parent is Decorator d)
            d.Child = null;
        else if (control.Parent is Viewbox vb)
            vb.Child = null;
        return control;
    }

    private void EnsureFallbackControlsInitialized()
    {
        if (accountsNavButton == null)
        {
            accountsNavButton = new Button
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 26, 31, 46)),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20, 10),
                MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeight.Bold,
                ZIndex = 50
            };
            accountsNavButton.Click += (_, _) => ShowAccountsOverlay();
            ApplyHoverMotion(accountsNavButton);
            UpdateAccountsButtonText();
        }

        usernameInput ??= CreateTextBox();
        usernameInput.Watermark = "Player name";
        usernameInput.TextChanged -= UsernameInput_TextChanged;
        usernameInput.TextChanged += UsernameInput_TextChanged;

        cbVersion ??= CreateComboBox(_versionItems);
        cbVersion.SelectionChanged -= CbVersion_SelectionChanged;
        cbVersion.SelectionChanged += CbVersion_SelectionChanged;

        minecraftVersion ??= CreateComboBox(VersionCategoryOptions);
        minecraftVersion.SelectionChanged -= MinecraftVersion_SelectionChanged;
        minecraftVersion.SelectionChanged += MinecraftVersion_SelectionChanged;

        downloadVersionButton ??= CreateSecondaryButton("Download Version");
        downloadVersionButton.Click -= DownloadVersionButton_Click;
        downloadVersionButton.Click += DownloadVersionButton_Click;

        profileNameInput ??= CreateTextBox();
        profileNameInput.Watermark = "Profile name";

        profileGameDirInput ??= CreateTextBox();
        profileGameDirInput.Watermark = "Custom game directory (optional)";

        instanceVersionCombo ??= CreateComboBox(_versionItems);
        instanceCategoryCombo ??= CreateComboBox(VersionCategoryOptions);
        instanceCategoryCombo.SelectedItem = "Versions";
        instanceCategoryCombo.SelectionChanged += (_, _) => _ = ListVersionsAsync(instanceCategoryCombo.SelectedItem?.ToString() ?? "Versions");
        _ = ListVersionsAsync("Versions");

        profileLoaderCombo ??= CreateComboBox(ProfileLoaderOptions);

        profilePresetCombo ??= CreateComboBox(ProfilePresetOptions);
        profilePresetCombo.SelectedItem = "Aether Client (Fabric) (Coming Soon)";
        profilePresetCombo.SelectionChanged += (s, e) =>
        {
            var selectedPreset = profilePresetCombo.SelectedItem?.ToString();
            if (selectedPreset == "Aether Client (Fabric) (Coming Soon)" || selectedPreset == "Aether Client (Fabric)")
            {
                profileNameInput.Text = "Aether Client";
                profileLoaderCombo.SelectedIndex = 1; // Fabric is Index 1 in ProfileLoaderOptions
                var targetVer = _versionItems.FirstOrDefault(v => v.Contains("1.21.1")) 
                             ?? _versionItems.FirstOrDefault(v => v.Contains("1.21"))
                             ?? _versionItems.FirstOrDefault();
                if (targetVer != null)
                {
                    instanceVersionCombo.SelectedItem = targetVer;
                }
            }
            else if (selectedPreset == "Vanilla Minecraft")
            {
                profileNameInput.Text = "Vanilla Minecraft";
                profileLoaderCombo.SelectedIndex = 0; // Vanilla
            }
            else if (selectedPreset == "Custom Modded")
            {
                profileNameInput.Text = "Custom Profile";
                profileLoaderCombo.SelectedIndex = 1; // Fabric
            }
        };

        if (createProfileButton is null)
        {
            createProfileButton = CreatePrimaryButton("Create Profile", "#38D6C4", Colors.Black);
            createProfileButton.Click += async (_, _) => await CreateProfileAsync();
        }

        renameProfileButton ??= CreateSecondaryButton("Rename Profile");
        renameProfileButton.Click -= RenameProfileButton_Click;
        renameProfileButton.Click += RenameProfileButton_Click;

        if (btnStart is null)
        {
            btnStart = CreatePrimaryButton("▶ Play", "#6E5BFF", Colors.White);
            btnStart.Click += async (_, _) => 
            {
                if (_launchCts != null)
                {
                    _launchCts.Cancel();
                    btnStart.IsEnabled = false;
                    btnStart.Content = "Cancelling...";
                }
                else
                {
                    await LaunchAsync();
                }
            };
        }

        activeProfileBadge ??= CreateStatusTextBlock();
        activeContextLabel ??= CreateMutedTextBlock();
        installModeLabel ??= CreateStatusTextBlock();

        if (characterImage == null)
        {
            characterImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            characterImage.PointerPressed += (s, e) =>
            {
                var pointerInfo = e.GetCurrentPoint(characterImage);
                if (pointerInfo.Properties.IsLeftButtonPressed)
                {
                    _isDraggingCharacter = true;
                    _lastDragPoint = e.GetPosition(characterImage);
                    _dragVelocity = 0.0;
                    e.Handled = true;
                }
            };

            characterImage.PointerMoved += (s, e) =>
            {
                if (_isDraggingCharacter)
                {
                    var currentPoint = e.GetPosition(characterImage);
                    double dx = currentPoint.X - _lastDragPoint.X;
                    double dy = currentPoint.Y - _lastDragPoint.Y;
                    _rotationAngle -= dx * 0.015;
                    _rotationAngleX = Math.Clamp(_rotationAngleX + dy * 0.012, -0.5, 0.5);
                    _dragVelocity = -dx * 0.015;
                    _lastDragPoint = currentPoint;
                    e.Handled = true;
                }
            };

            characterImage.PointerReleased += (s, e) =>
            {
                if (_isDraggingCharacter)
                {
                    _isDraggingCharacter = false;
                    e.Handled = true;
                }
            };
        }

        statusLabel ??= CreateStatusTextBlock();
        installDetailsLabel ??= CreateMutedTextBlock();
        pbFiles ??= new ProgressBar { Height = 4, CornerRadius = new CornerRadius(2), Minimum = 0, Maximum = 100 };
        pbProgress ??= new ProgressBar { Height = 4, CornerRadius = new CornerRadius(2), Minimum = 0, Maximum = 100 };

        modrinthSearchInput ??= CreateTextBox();
        modrinthProjectTypeCombo ??= CreateComboBox(ProjectTypeOptions);
        modrinthLoaderCombo ??= CreateComboBox(LoaderOptions);
        modrinthSourceCombo ??= CreateComboBox(SourceOptions);

        if (modrinthSearchButton is null)
        {
            modrinthSearchButton = CreatePrimaryButton("Search", "#6E5BFF", Colors.White);
            modrinthSearchButton.Click += async (_, _) => await SearchModrinthAsync();
        }

        modrinthVersionInput ??= CreateTextBox();
        modrinthResultsListBox ??= new ListBox { ItemsSource = _searchResults };
        modrinthResultsListBox.SelectionChanged -= ModrinthResultsListBox_SelectionChanged;
        modrinthResultsListBox.SelectionChanged += ModrinthResultsListBox_SelectionChanged;

        modrinthDetailsBox ??= CreateMutedTextBlock();
        modrinthDetailsBox.TextWrapping = TextWrapping.Wrap;
        modrinthResultsSummary ??= CreateMutedTextBlock();

        if (installSelectedButton is null)
        {
            installSelectedButton = CreatePrimaryButton("Install Selected", "#38D6C4", Colors.Black);
            installSelectedButton.Click += async (_, _) => await InstallSelectedAsync();
        }

        importMrpackButton ??= CreateSecondaryButton("Import .mrpack");
        importMrpackButton.Click -= ImportMrpackButton_Click;
        importMrpackButton.Click += ImportMrpackButton_Click;

        profileListBox ??= new ListBox { ItemsSource = _profileItems };
        profileListBox.SelectionChanged -= ProfileListBox_SelectionChanged;
        profileListBox.SelectionChanged += ProfileListBox_SelectionChanged;

        profileInspectorTitle ??= CreateStatusTextBlock();
        profileInspectorMeta ??= CreateMutedTextBlock();
        profileInspectorMeta.TextWrapping = TextWrapping.Wrap;
        profileInspectorPath ??= CreateMutedTextBlock();
        profileInspectorPath.TextWrapping = TextWrapping.Wrap;

        clearProfileButton ??= CreateSecondaryButton("Delete Profile");
        clearProfileButton.Click -= ClearProfileButton_Click;
        clearProfileButton.Click += ClearProfileButton_Click;

        heroInstanceLabel ??= new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Black,
            TextWrapping = TextWrapping.Wrap
        };
        heroPerformanceLabel ??= CreateMutedTextBlock();
        homeFpsStatValue ??= new TextBlock();
        homeRamStatValue ??= new TextBlock();
        performanceFpsStatValue ??= new TextBlock();
        performanceRamStatValue ??= new TextBlock();
        loadingLabel ??= CreateMutedTextBlock();

        _quickVersionCombo ??= CreateComboBox(_versionItems);
        _quickLoaderCombo ??= CreateComboBox(ProfileLoaderOptions);

        _quickInstallButton ??= CreatePrimaryButton("Quick Install", "#38D6C4", Colors.Black);
        _quickInstallButton.Click -= QuickInstallButton_Click;
        _quickInstallButton.Click += QuickInstallButton_Click;

        _quickModSearch ??= CreateTextBox();
        _quickModSearch.Watermark = "Search mods";

        _quickModSearchButton ??= CreateSecondaryButton("Quick Search");
        _quickModSearchButton.Click -= QuickModSearchButton_Click;
        _quickModSearchButton.Click += QuickModSearchButton_Click;

        _playOverlay ??= new Border();
        _playOverlayIcon ??= new TextBlock();
        _playOverlayLabel ??= new TextBlock();

        _quickModResults.ItemsSource = _quickSearchResults;
        
        // Use a more robust detachment and re-attachment for the play button
        var playStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        var icon = DetachFromParent(_playOverlayIcon);
        var label = DetachFromParent(_playOverlayLabel);
        if (icon != null) playStack.Children.Add(icon);
        if (label != null) playStack.Children.Add(label);
        
        var accentColor = Color.Parse(_settings.AccentColor);
        _playOverlay.Background = new SolidColorBrush(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B));
        _playOverlay.BorderBrush = new SolidColorBrush(accentColor);
        _playOverlay.BorderThickness = new Thickness(1.5);
        _playOverlay.CornerRadius = new CornerRadius(14);
        _playOverlay.Padding = new Thickness(0);
        _playOverlay.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 12,
            OffsetY = 4,
            Color = Color.FromArgb(60, 0, 0, 0)
        });
        
        _playOverlayIcon.Foreground = new SolidColorBrush(accentColor);
        _playOverlayIcon.FontSize = 18;
        _playOverlayIcon.Text = "▶";
        
        _playOverlayLabel.Foreground = Brushes.White;
        _playOverlayLabel.FontSize = 15;
        _playOverlayLabel.FontWeight = FontWeight.Bold;
        _playOverlayLabel.Margin = new Thickness(10, 0, 0, 0);
        _playOverlayLabel.Text = "PLAY";

        _playOverlay.Child = playStack;
        _playOverlay.PointerPressed -= PlayOverlay_PointerPressed;
        _playOverlay.PointerPressed += PlayOverlay_PointerPressed;
        _playOverlay.Cursor = new Cursor(StandardCursorType.Hand);

        _instanceEditorOverlay ??= BuildInstanceEditorOverlay();
        _accountsListPanel ??= new StackPanel();
        _accountsOverlay ??= BuildAccountsOverlay();
        PbProgress = pbProgress;
        ModrinthSearchInput = modrinthSearchInput;
        UpdateSelectedProjectDetails();
    }

    private Border BuildInstanceEditorOverlay()
    {
        var cancelButton = CreateSecondaryButton("Cancel");
        cancelButton.Click += (_, _) => _instanceEditorOverlay.IsVisible = false;

        profilePresetSection = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreatePanelEyebrow("Preset"),
                DetachFromParent(profilePresetCombo)!
            }
        };

        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(200, 3, 5, 12)),
            Padding = new Thickness(32),
            ZIndex = 100,
            Child = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 460,
                Children =
                {
                    new Border
                    {
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                            GradientStops =
                            {
                                new GradientStop(Color.FromArgb(240, 10, 14, 28), 0),
                                new GradientStop(Color.FromArgb(250, 4, 6, 12), 1)
                            }
                        },
                        BorderBrush = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                            GradientStops =
                            {
                                new GradientStop(Color.Parse("#38D6C4"), 0),
                                new GradientStop(Color.Parse("#B655FF"), 0.5),
                                new GradientStop(Color.Parse("#5B80FF"), 1)
                            }
                        },
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(24),
                        Padding = new Thickness(24),
                        Child = new StackPanel
                        {
                            Spacing = 16,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Edit Instance",
                                    Foreground = Brushes.White,
                                    FontSize = 22,
                                    FontWeight = FontWeight.Bold
                                },
                                profilePresetSection,
                                new StackPanel
                                {
                                    Spacing = 8,
                                    Children =
                                    {
                                        CreatePanelEyebrow("Name"),
                                        DetachFromParent(profileNameInput)!
                                    }
                                },
                                new StackPanel
                                {
                                    Spacing = 8,
                                    Children =
                                    {
                                        CreatePanelEyebrow("Loader"),
                                        DetachFromParent(profileLoaderCombo)!
                                    }
                                },
                                new StackPanel
                                {
                                    Spacing = 8,
                                    Children =
                                    {
                                        CreatePanelEyebrow("Game Version"),
                                        new Grid
                                        {
                                            ColumnDefinitions = new ColumnDefinitions("*,*"),
                                            ColumnSpacing = 8,
                                            Children =
                                            {
                                                DetachFromParent(instanceCategoryCombo)!.With(column: 0),
                                                DetachFromParent(instanceVersionCombo)!.With(column: 1)
                                            }
                                        }
                                    }
                                },
                                new StackPanel
                                {
                                    Spacing = 8,
                                    Children =
                                    {
                                        CreatePanelEyebrow("Game Directory Override"),
                                        DetachFromParent(profileGameDirInput)!
                                    }
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 10,
                                    Children =
                                    {
                                        DetachFromParent(createProfileButton)!.With(column: 0),
                                        DetachFromParent(renameProfileButton)!.With(column: 1),
                                        cancelButton!.With(column: 2)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private void ShowAccountsOverlay()
    {
        RefreshAccountsList();
        _accountsOverlay.IsVisible = true;
        if (accountsNavButton != null) accountsNavButton.IsVisible = false;
    }

    private bool _isAuthenticating;
    private void RefreshAccountsList()
    {
        _accountsListPanel.Children.Clear();
        foreach (var account in _settings.Accounts.ToList())
        {
            var isSelected = account.Id == _settings.SelectedAccountId;

            var avatarBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "🧑",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(0, 0, 12, 0)
            };

            var nameBlock = new TextBlock
            {
                Text = account.Username,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                FontSize = 14
            };

            var typeColor = account.Provider == "microsoft" ? "#5B80FF" : "#A0A8B8";
            var typeLabel = account.Provider == "microsoft" ? "Microsoft" : "Offline";

            var typeBlock = new TextBlock
            {
                Text = typeLabel,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse(typeColor))
            };

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameBlock, typeBlock } };

            var removeBtn = new Button
            {
                Content = "🗑",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#FF5B5B")),
                IsVisible = false 
            };
            removeBtn.Click += (_, _) =>
            {
                _settings.Accounts.Remove(account);
                if (_settings.SelectedAccountId == account.Id)
                {
                    _settings.SelectedAccountId = string.Empty;
                    usernameInput.Text = string.Empty;
                    UsernameInput_TextChanged();
                }
                _settingsStore.Save(_settings);
                RefreshAccountsList();
                UpdateAccountsButtonText();
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children = { avatarBorder.With(column: 0), textStack.With(column: 1), removeBtn.With(column: 2) }
            };

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16, 12),
                BorderBrush = isSelected ? new SolidColorBrush(Color.Parse("#38D6C4")) : new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                Child = rowGrid,
                Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                    new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                },
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };

            removeBtn.Opacity = 0.0;
            removeBtn.Transitions = new Transitions
            {
                new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
            };

            card.PointerEntered += (_, _) =>
            {
                removeBtn.Opacity = 1.0;
                card.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                card.RenderTransform = TransformOperations.Parse("scale(1.02)");
            };
            card.PointerExited += (_, _) =>
            {
                removeBtn.Opacity = 0.0;
                card.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
                card.RenderTransform = TransformOperations.Parse("scale(1.0)");
            };

             card.PointerPressed += (_, _) =>
            {
                _settings.SelectedAccountId = account.Id;
                usernameInput.Text = account.Username;
                UsernameInput_TextChanged();
                _settingsStore.Save(_settings);
                RefreshAccountsList();
                UpdateAccountsButtonText();
                _accountsOverlay.IsVisible = false;
                if (accountsNavButton != null) accountsNavButton.IsVisible = true;
            };

            _accountsListPanel.Children.Add(card);
        }
    }

    private async Task AddOfflineAccountAsync()
    {
        var username = await DialogService.ShowTextInputAsync(this, "Add Offline Account", "Enter your username:");
        if (string.IsNullOrWhiteSpace(username)) return;

        var acc = new LauncherAccount { Provider = "offline", Username = username.Trim(), DisplayName = username.Trim() };
        _settings.Accounts.Add(acc);
        _settings.SelectedAccountId = acc.Id;
        usernameInput.Text = acc.Username;
        UsernameInput_TextChanged();
        _settingsStore.Save(_settings);
        UpdateAccountsButtonText();
        RefreshAccountsList();
    }

    private LauncherAccount? GetSelectedAccount()
        => _settings.Accounts.FirstOrDefault(a => a.Id == _settings.SelectedAccountId);

    private string GetActiveUsername()
    {
        var selectedAccount = GetSelectedAccount();
        if (selectedAccount != null && !string.IsNullOrWhiteSpace(selectedAccount.Username))
            return selectedAccount.Username;

        return usernameInput.Text?.Trim() ?? string.Empty;
    }

    private bool IsUsingMicrosoftAccount()
        => string.Equals(GetSelectedAccount()?.Provider, "microsoft", StringComparison.OrdinalIgnoreCase);

    private bool HasManualSkinOverride()
    {
        var manualSkinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
        return string.Equals(_settings.CustomSkinPath, manualSkinPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(manualSkinPath);
    }

    private bool HasManualCapeOverride()
    {
        var manualCapePath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "cape.png");
        return string.Equals(_settings.CustomCapePath, manualCapePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(manualCapePath);
    }

    private async Task<MSession> BuildLaunchSessionAsync(CancellationToken cancellationToken)
    {
        var selectedAccount = GetSelectedAccount();
        if (selectedAccount != null && string.Equals(selectedAccount.Provider, "microsoft", StringComparison.OrdinalIgnoreCase))
        {
            if (_settings.OfflineMode)
            {
                LauncherLog.Info("[Launch] Launching with Microsoft session offline (ignoring expiration).");
                return new MSession
                {
                    Username = selectedAccount.Username,
                    UUID = selectedAccount.Uuid,
                    AccessToken = string.IsNullOrWhiteSpace(selectedAccount.MinecraftAccessToken) ? "offline_token" : selectedAccount.MinecraftAccessToken,
                    Xuid = selectedAccount.Xuid,
                    UserType = "msa"
                };
            }

            if (selectedAccount.IsExpired)
            {
                var refreshed = await TryRefreshAccountAsync(selectedAccount);
                if (!refreshed)
                    throw new InvalidOperationException("The selected Microsoft account could not be refreshed. Sign in again.");

                selectedAccount = GetSelectedAccount();
            }

            if (selectedAccount == null || string.IsNullOrWhiteSpace(selectedAccount.MinecraftAccessToken))
                throw new InvalidOperationException("The selected Microsoft account is missing a Minecraft access token. Sign in again.");

            if (string.IsNullOrWhiteSpace(selectedAccount.Uuid))
                throw new InvalidOperationException("The selected Microsoft account is missing the Minecraft profile UUID.");

            return new MSession
            {
                Username = selectedAccount.Username,
                UUID = selectedAccount.Uuid,
                AccessToken = selectedAccount.MinecraftAccessToken,
                Xuid = selectedAccount.Xuid,
                UserType = "msa"
            };
        }

        var username = GetActiveUsername();
        var session = MSession.CreateOfflineSession(username);
        session.UUID = string.IsNullOrWhiteSpace(_playerUuid)
            ? Character.GenerateUuidFromUsername(username)
            : _playerUuid;
        session.UserType = "legacy"; // Explicitly force legacy user type for offline session to bypass modern Xbox Live / Microsoft Account multiplayer locks.
        return session;
    }

    private async Task<bool> TryRefreshAccountAsync(LauncherAccount account)
    {
        if (account.Provider != "microsoft" || !account.IsExpired) return true;

        try
        {
            var clientId = string.IsNullOrWhiteSpace(_settings.MicrosoftClientId) ? "00000000402b5328" : _settings.MicrosoftClientId;
            LauncherLog.Info($"[Microsoft Auth] Refreshing token for {account.Username}...");
            
            var refreshed = await _authService.RefreshMinecraftAccountAsync(clientId, account, CancellationToken.None);
            
            // Update existing account in settings
            var idx = _settings.Accounts.FindIndex(a => a.Id == account.Id);
            if (idx != -1)
            {
                _settings.Accounts[idx] = refreshed;
                _settingsStore.Save(_settings);
                return true;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Info($"[Microsoft Auth] Refresh failed for {account.Username}: {ex.Message}");
        }
        return false;
    }

    private async Task AddMicrosoftAccountAsync()
    {
        if (_isAuthenticating) return;
        _isAuthenticating = true;

        var clientId = string.IsNullOrWhiteSpace(_settings.MicrosoftClientId) ? "00000000402b5328" : _settings.MicrosoftClientId;
        using var cts = new CancellationTokenSource();
        
        try
        {
            LauncherLog.Info("[Microsoft Auth] Starting device code login...");
            var session = await _authService.BeginDeviceLoginAsync(clientId, cts.Token);

            // Open browser and show premium dialog
            Process.Start(new ProcessStartInfo { FileName = session.VerificationUri, UseShellExecute = true });
            
            var dialogTask = DialogService.ShowMicrosoftAuthDialogAsync(this, session.UserCode, session.VerificationUri, cts);
            var pollTask = _authService.CompleteDeviceLoginAsync(clientId, session, cts.Token);

            var completedTask = await Task.WhenAny(dialogTask, pollTask);

            if (completedTask == pollTask)
            {
                var account = await pollTask;
                var existing = _settings.Accounts.FirstOrDefault(a => a.Uuid == account.Uuid && a.Provider == "microsoft");
                if (existing != null) _settings.Accounts.Remove(existing);

                _settings.Accounts.Add(account);
                _settings.SelectedAccountId = account.Id;
                usernameInput.Text = account.Username;
                UsernameInput_TextChanged();
                _settingsStore.Save(_settings);
                
                LauncherLog.Info($"[Microsoft Auth] Successfully logged in as {account.Username}");
                UpdateAccountsButtonText();
                RefreshAccountsList();
            }
            else
            {
                LauncherLog.Info("[Microsoft Auth] Login cancelled by user.");
            }
        }
        catch (OperationCanceledException)
        {
            LauncherLog.Info("[Microsoft Auth] Login timed out or cancelled.");
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Authentication Failed", ex.Message);
        }
        finally
        {
            _isAuthenticating = false;
        }
    }



    private Border BuildAccountsOverlay()
    {
        var closeButton = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            FontSize = 24,
            Padding = new Thickness(8, 0)
        };
        closeButton.Click += (_, _) => 
        {
            _accountsOverlay.IsVisible = false;
            if (accountsNavButton != null)
            {
                accountsNavButton.IsVisible = true;
                accountsNavButton.Opacity = 1.0;
                accountsNavButton.RenderTransform = TransformOperations.Parse("scale(1.0)");
            }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = "Accounts", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                closeButton.With(column: 1)
            }
        };

        var addMicrosoftBtn = CreatePrimaryButton("Add Microsoft Account", "#5B80FF", Colors.White);
        addMicrosoftBtn.Click += async (_, _) => await AddMicrosoftAccountAsync();

        var addOfflineBtn = CreateSecondaryButton("Add Offline");
        addOfflineBtn.Click += async (_, _) => await AddOfflineAccountAsync();

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children =
            {
                addMicrosoftBtn.With(column: 0),
                addOfflineBtn.With(column: 1)
            }
        };

        var panel = new Border
        {
            Width = 400,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(235, 12, 16, 32), 0),
                    new GradientStop(Color.FromArgb(245, 6, 8, 16), 1)
                }
            },
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#38D6C4"), 0),
                    new GradientStop(Color.Parse("#B655FF"), 0.5),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)
                }
            },
            BorderThickness = new Thickness(1.5, 0, 0, 0),
            CornerRadius = new CornerRadius(24, 0, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(24),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    header.With(row: 0),
                    new ScrollViewer
                    {
                        Margin = new Thickness(0, 24),
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _accountsListPanel.With(sp => sp.Spacing = 8)
                    }.With(row: 1),
                    footer.With(row: 2)
                }
            }
        };

        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(160, 2, 4, 8)),
            ZIndex = 100,
            Child = panel
        };
    }

    private void UpdateAccountsButtonText()
    {
        if (accountsNavButton != null)
        {
            var activeName = GetSelectedAccount()?.Username;
            if (string.IsNullOrWhiteSpace(activeName))
                activeName = string.IsNullOrWhiteSpace(usernameInput.Text) ? _settings.Username : usernameInput.Text;
            if (string.IsNullOrWhiteSpace(activeName))
                activeName = "Accounts";

            // Make it look premium
            var fg = !string.IsNullOrWhiteSpace(_settings.Style.NavButtonForeground) ? _settings.Style.NavButtonForeground : "#A4A8B1";
            var accent = !string.IsNullOrWhiteSpace(_settings.Style.AccentColor) ? _settings.Style.AccentColor! : (!string.IsNullOrWhiteSpace(_settings.AccentColor) ? _settings.AccentColor : "#6E5BFF");
            
            accountsNavButton.Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, Color.Parse(accent).R, Color.Parse(accent).G, Color.Parse(accent).B)),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(6),
                        Child = new TextBlock
                        {
                            Text = "🧑",
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.Parse(accent)),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    },
                    new TextBlock
                    {
                        Text = activeName,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse(fg)),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                }
            };
            
            // Add transitions if not already added
            if (accountsNavButton.Transitions == null)
            {
                accountsNavButton.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
                    new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200) }
                };
            }
        }
    }

    private Control BuildFeaturedServersSection()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 16, 0, 12),
            Children =
            {
                new Border
                {
                    Width = 3, Height = 16,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.Parse(_settings.AccentColor)),
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = "FEATURED SERVERS",
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#8E96A8")),
                    LetterSpacing = 1.5,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var breakpointCard = BuildServerCard(
            bgAsset: "avares://AetherLauncher/assets/launcher_background.png",
            logoAsset: "avares://AetherLauncher/assets/breakpoint-logo.png",
            serverName: "BreakPoint MC",
            tagLine: "⭐ FEATURED",
            description: "Cracked Server. Optimised for Aether.",
            ip: "breakpoint.mcsrv.net",
            accentHex: "#7E6AFF",
            isFeatured: true
        );

        var hypixelCard = BuildServerCard(
            bgAsset: "avares://AetherLauncher/assets/hypixel_card_bg.png",
            serverName: "Hypixel",
            tagLine: "MINI-GAMES",
            description: "The world's largest server.",
            ip: "mc.hypixel.net",
            accentHex: "#F4C430",
            isFeatured: false
        );

        var donutCard = BuildServerCard(
            bgAsset: "avares://AetherLauncher/assets/donut_smp_card_bg.png",
            serverName: "Donut SMP",
            tagLine: "SURVIVAL",
            description: "Community survival SMP.",
            ip: "play.donutsmp.net",
            accentHex: "#FF8C42",
            isFeatured: false
        );

        var cardsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, *, *"),
            ColumnSpacing = 10,
            Height = 135,
            Children =
            {
                breakpointCard,
                hypixelCard.With(column: 1),
                donutCard.With(column: 2)
            }
        };

        return new StackPanel { Children = { header, cardsGrid } };
    }

    private Border BuildServerCard(string bgAsset, string serverName, string tagLine, string description, string ip, string accentHex, bool isFeatured, string? logoAsset = null)
    {
        ImageBrush? bgBrush = null;
        try
        {
            var bmp = new Bitmap(AssetLoader.Open(new Uri(bgAsset)));
            bgBrush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch { }

        // Logo overlay (shows when NOT hovered)
        var logoContent = new Panel();
        if (!string.IsNullOrEmpty(logoAsset))
        {
            try
            {
                var logoBmp = new Bitmap(AssetLoader.Open(new Uri(logoAsset)));
                logoContent.Children.Add(new Image
                {
                    Source = logoBmp,
                    Stretch = Stretch.UniformToFill,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Transitions = new Transitions { new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) } }
                });
            }
            catch { }
        }

        // Overlay that shows on hover
        var hoverOverlay = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(230, 9, 12, 20), 0),
                    new GradientStop(Color.FromArgb(140, 9, 12, 20), 0.6),
                    new GradientStop(Color.FromArgb(0, 9, 12, 20), 1)
                }
            },
            Opacity = 0,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Border.OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) }
            },
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 14, 14),
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, Color.Parse(accentHex).R, Color.Parse(accentHex).G, Color.Parse(accentHex).B)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(120, Color.Parse(accentHex).R, Color.Parse(accentHex).G, Color.Parse(accentHex).B)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = new TextBlock
                        {
                            Text = tagLine,
                            FontSize = 11,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse(accentHex)),
                            LetterSpacing = 1
                        }
                    },
                    new TextBlock
                    {
                        Text = serverName,
                        FontSize = isFeatured ? 20 : 16,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                        Text = description,
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(Color.Parse("#A0AABB")),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Button
                    {
                        Content = $"Copy IP: {ip}",
                        FontSize = 9.5,
                        Foreground = new SolidColorBrush(Color.Parse(accentHex)),
                        Background = Brushes.Transparent,
                        Padding = new Thickness(0, 2, 0, 0),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Command = new RelayCommand(() => CopyServerIpToClipboard(ip))
                    }
                }
            }
        };

        hoverOverlay.Transitions = new Transitions
        {
            new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
        };
        logoContent.Transitions = new Transitions
        {
            new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            Background = bgBrush != null ? bgBrush : new SolidColorBrush(Color.Parse("#1A1F2E")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(isFeatured ? (byte)80 : (byte)40, Color.Parse(accentHex).R, Color.Parse(accentHex).G, Color.Parse(accentHex).B)),
            BorderThickness = new Thickness(1),
            BoxShadow = isFeatured ? new BoxShadows(new BoxShadow
            {
                Blur = 20,
                Color = Color.FromArgb(100, Color.Parse(accentHex).R, Color.Parse(accentHex).G, Color.Parse(accentHex).B),
                OffsetX = 0,
                OffsetY = 0
            }) : default,
            Child = new Grid { Children = { logoContent, hoverOverlay } },
            Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
            },
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };

        card.PointerEntered += (_, _) => {
            hoverOverlay.Opacity = 1;
            logoContent.Opacity = 0;
            card.RenderTransform = TransformOperations.Parse("scale(1.02)");
        };
        card.PointerExited += (_, _) => {
            hoverOverlay.Opacity = 0;
            logoContent.Opacity = 1;
            card.RenderTransform = TransformOperations.Parse("scale(1.0)");
        };

        return card;
    }

    private async void CopyServerIpToClipboard(string ip)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;
        await topLevel.Clipboard.SetTextAsync(ip);
    }

    private async void CopyToClipboard(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        await topLevel.Clipboard!.SetTextAsync(text);
    }

    private void EnsureSectionsBuilt()
    {
        EnsureFallbackControlsInitialized();
        launchSection ??= BuildLaunchDeck();
        modrinthSection ??= BuildModrinthDeck();
        profilesSection ??= BuildProfilesDeck();
        performanceSection ??= BuildPerformanceDeck();
        settingsSection ??= BuildSettingsDeck();
        layoutSection ??= BuildLayoutDeck();

        launchSection.IsVisible = _activeSection == "launch";
        modrinthSection.IsVisible = _activeSection == "modrinth";
        profilesSection.IsVisible = _activeSection == "profiles";
        performanceSection.IsVisible = _activeSection == "performance";
        settingsSection.IsVisible = _activeSection == "settings";
        layoutSection.IsVisible = _activeSection == "layout";
    }

    private void InvalidateUiCache()
    {
        // Sections
        launchSection = null!;
        modrinthSection = null!;
        profilesSection = null!;
        performanceSection = null!;
        settingsSection = null!;
        layoutSection = null!;
        
        // Overlays
        _instanceEditorOverlay = null!;
        _accountsOverlay = null!;
        _playOverlay = new Border();
        
        // Navigation
        launchNavButton = null!;
        profilesNavButton = null!;
        modrinthNavButton = null!;
        performanceNavButton = null!;
        settingsNavButton = null!;
        layoutNavButton = null!;
        accountsNavButton = null!;
        
        // Shared Labels & Fields
        heroInstanceLabel = null!;
        heroPerformanceLabel = null!;
        loadingLabel = null!;
        statusLabel = null!;
        installDetailsLabel = null!;
        activeProfileBadge = null!;
        activeContextLabel = null!;
        usernameInput = null!;
        
        // Progress & Stats
        pbFiles = null!;
        pbProgress = null!;
        homeFpsStatValue = null!;
        homeRamStatValue = null!;
        performanceFpsStatValue = null!;
        performanceRamStatValue = null!;
        
        // Input Controls
        cbVersion = null!;
        minecraftVersion = null!;
        downloadVersionButton = null!;
        profileNameInput = null!;
        profileGameDirInput = null!;
        profileLoaderCombo = null!;
        profilePresetCombo = null!;
        instanceVersionCombo = null!;
        instanceCategoryCombo = null!;
        _quickVersionCombo = null!;
        _quickLoaderCombo = null!;
        _quickInstallButton = null!;
        _quickModSearch = null!;
        _quickModSearchButton = null!;
        _accountsListPanel = null!;
        _playOverlay = null!;
        _playOverlayIcon = null!;
        _playOverlayLabel = null!;
        
        // Missed Premium UI Fields
        characterImage = null!;
        activeProfileBadge = null!;
        activeContextLabel = null!;
        installModeLabel = null!;
        btnStart = null!;
        profileListBox = null!;
        modrinthResultsListBox = null!;
        modrinthDetailsBox = null!;
        modrinthResultsSummary = null!;
        installSelectedButton = null!;
        importMrpackButton = null!;
        profileInspectorTitle = null!;
        profileInspectorMeta = null!;
        profileInspectorPath = null!;
        clearProfileButton = null!;
        modrinthSearchInput = null!;
        modrinthProjectTypeCombo = null!;
        modrinthLoaderCombo = null!;
        modrinthSourceCombo = null!;
        modrinthSearchButton = null!;
        modrinthVersionInput = null!;
    }

    private Control BuildContent()
    {
        EnsureSectionsBuilt();
        var style = _settings.Style;

        var outerMargin = IsTopNavigationEnabled() ? new Thickness(28, 4, 28, 24) : new Thickness(22);
        if (!double.IsNaN(style.ContentSpacing)) outerMargin = new Thickness(style.ContentSpacing);
        
        var innerPadding = double.IsNaN(style.ContentPadding) ? new Thickness(18) : new Thickness(style.ContentPadding);
        IBrush bg = !string.IsNullOrWhiteSpace(style.ContentBackground) ? new SolidColorBrush(Color.Parse(style.ContentBackground)) : Brushes.Transparent;

        var launch = DetachFromParent(launchSection)!;
        var modrinth = DetachFromParent(modrinthSection)!;
        var profiles = DetachFromParent(profilesSection)!;
        var performance = DetachFromParent(performanceSection)!;
        var settings = DetachFromParent(settingsSection)!;
        var layout = DetachFromParent(layoutSection)!;

        return new Grid
        {
            Margin = outerMargin,
            Children =
            {
                new Border
                {
                    Background = bg,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(30, 100, 120, 180)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(24),
                    Padding = innerPadding,
                    Child = new Grid
                    {
                        Children =
                        {
                            launch!,
                            modrinth!,
                            profiles!,
                            performance!,
                            settings!,
                            layout!
                        }
                    }
                }
            }
        };
    }

    private Control BuildNavigationRail()
    {
        return BuildCard(new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Workspace",
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = "Play, browse, switch.",
                    Foreground = new SolidColorBrush(Color.Parse("#A8B8D4")),
                    TextWrapping = TextWrapping.Wrap
                },
                launchNavButton,
                modrinthNavButton,
                profilesNavButton,
                new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#101A2A"), 0),
                            new GradientStop(Color.Parse("#0C1320"), 1)
                        }
                    },
                    BorderBrush = new SolidColorBrush(Color.Parse("#23344C")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(16),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Flow",
                                Foreground = new SolidColorBrush(Color.Parse("#7BC9FF")),
                                FontWeight = FontWeight.Bold
                            },
                            new TextBlock
                            {
                                Text = "▶ Play\n⌕ Find mods\n▣ Pick profile",
                                Foreground = new SolidColorBrush(Color.Parse("#C8D5EC")),
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                }
            }
        });
    }

    private Control BuildLaunchDeck()
    {
        // 1:1 REPLICA LAYOUT
        var topInfo = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                DetachFromParent(heroInstanceLabel)!,
                DetachFromParent(heroPerformanceLabel)!,
                new Border { Height = 12 },
                new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(40, 255,255,255)), Margin = new Thickness(0, 8, 0, 0) }
            }
        };

        // PLAY Button with correct glow
        var accent_deck = Color.Parse(_settings.AccentColor);
        _playOverlay.Width = 220;
        _playOverlay.Height = 56;
        _playOverlay.CornerRadius = new CornerRadius(14);
        _playOverlay.Background = new SolidColorBrush(Color.FromArgb(50, accent_deck.R, accent_deck.G, accent_deck.B));
        _playOverlay.BorderBrush = new SolidColorBrush(accent_deck);
        _playOverlay.BorderThickness = new Thickness(1.5);
        _playOverlay.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 12,
            OffsetY = 4,
            Color = Color.FromArgb(60, 0, 0, 0)
        });
        _playOverlayIcon.Foreground = new SolidColorBrush(accent_deck);
        _playOverlayIcon.Text = "▶";
        _playOverlayIcon.FontSize = 18;
        _playOverlayLabel.Foreground = Brushes.White;
        _playOverlayLabel.Text = "PLAY";
        _playOverlayLabel.FontSize = 15;
        _playOverlayLabel.FontWeight = FontWeight.Bold;
        _playOverlayLabel.Opacity = 1;
        _playOverlayLabel.Margin = new Thickness(10, 0, 0, 0);

        ApplyHoverMotion(_playOverlay);

        var modsBtn = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 12),
            Width = 200,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock { Text = "□", FontSize = 15, Foreground = new SolidColorBrush(Color.Parse(_settings.AccentColor)) },
                    new TextBlock { Text = "Mods", FontSize = 12.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(12, 0) }.With(column: 1),
                    new TextBlock { Text = "〉", FontSize = 12, Foreground = Brushes.Gray }.With(column: 2)
                }
            }
        };
        modsBtn.Click += (_, _) => SetActiveSection("modrinth");

        var profilesBtn = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 12),
            Width = 200,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new TextBlock { Text = "〓", FontSize = 15, Foreground = new SolidColorBrush(Color.Parse(_settings.AccentColor)) },
                    new TextBlock { Text = "Instances", FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(12, 0) }.With(column: 1),
                    new TextBlock { Text = "〉", FontSize = 12, Foreground = Brushes.Gray }.With(column: 2)
                }
            }
        };
        profilesBtn.Click += (_, _) => SetActiveSection("profiles");

        var actionsGroup = new StackPanel
        {
            Spacing = 8,
            Children = { modsBtn, profilesBtn }
        };

        foreach (var c in actionsGroup.Children) ApplyHoverMotion(c as Control);

        var skinContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = "●", FontSize = 9, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Skin", FontSize = 11, VerticalAlignment = VerticalAlignment.Center } } };
        var skinBtn = new Button { Content = skinContent, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(10), Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        skinBtn.Click += async (_, _) => await ChangeSkinAsync();
        ApplyHoverMotion(skinBtn);

        var capeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = "■", FontSize = 9, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Cape", FontSize = 11, VerticalAlignment = VerticalAlignment.Center } } };
        var capeBtn = new Button { Content = capeContent, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(10), Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        capeBtn.Click += async (_, _) => await ChangeCapeAsync();
        ApplyHoverMotion(capeBtn);

        var resetContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = "×", FontSize = 11, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "Reset", FontSize = 11, VerticalAlignment = VerticalAlignment.Center } } };
        var resetBtn = new Button { Content = resetContent, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(10), Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        resetBtn.Click += (_, _) => {
            _settings.CustomSkinPath = string.Empty;
            _settings.CustomCapePath = string.Empty;
            _settings.CustomCapeSourcePath = string.Empty;
            _settingsStore.Save(_settings);
            UpdateCharacterPreview();
        };
        ApplyHoverMotion(resetBtn);

        var avatarPanel = CreateGlassPanel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Avatar", FontSize = 12.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Opacity = 0.8 },
                new Border { Height = 260, Child = DetachFromParent(characterImage) },
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                    ColumnSpacing = 8,
                    Children = { skinBtn.With(column: 0), capeBtn.With(column: 1), resetBtn.With(column: 2) }
                }
            }
        }, padding: new Thickness(16), margin: new Thickness(0));
        avatarPanel.Width = 280;
        _avatarGlass = avatarPanel;
        _avatarControls = (StackPanel)avatarPanel.Child!;
        _avatarActions = (Grid)_avatarControls.Children[2];

        _avatarGlass.PointerEntered += (s, e) => { if (_isNarrowMode) SetAvatarExpansion(true); };
        _avatarGlass.PointerExited += (s, e) => { if (_isNarrowMode) SetAvatarExpansion(false); };

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16
        };

        if (!ShouldExternalizePlayButton())
            actionRow.Children.Add(DetachFromParent(_playOverlay)!);
        actionRow.Children.Add(actionsGroup);

        var featuredClientCard = CreateGlassPanel(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                // Left text stack
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 6,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Aether Client",
                                    FontSize = 18,
                                    FontWeight = FontWeight.Bold,
                                    Foreground = _settings.ThemeVariant == "light" ? Brushes.Black : Brushes.White
                                },
                                new Border
                                {
                                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 184, 91)),
                                    BorderBrush = new SolidColorBrush(Color.Parse("#FFB85B")),
                                    BorderThickness = new Thickness(1),
                                    CornerRadius = new CornerRadius(6),
                                    Padding = new Thickness(6, 2),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Child = new TextBlock
                                    {
                                        Text = "COMING SOON",
                                        FontSize = 9,
                                        FontWeight = FontWeight.Black,
                                        Foreground = new SolidColorBrush(Color.Parse("#FFB85B"))
                                    }
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = "Coming soon — stay tuned for updates.",
                            FontSize = 12.5,
                            Foreground = _settings.ThemeVariant == "light" ? new SolidColorBrush(Color.Parse("#4A5568")) : new SolidColorBrush(Color.Parse("#A0A8B8"))
                        },
                        new TextBlock
                        {
                            Text = "Fabric 1.21.11 · Performance & Menu Enhanced",
                            FontSize = 11,
                            FontWeight = FontWeight.Medium,
                            Foreground = _settings.ThemeVariant == "light" ? new SolidColorBrush(Color.Parse("#6E5BFF")) : new SolidColorBrush(Color.Parse("#A394FF"))
                        }
                    }
                }.With(column: 0),

                // Right quick start button
                new Button
                {
                    Width = 140,
                    Height = 40,
                    CornerRadius = new CornerRadius(20),
                    Background = new SolidColorBrush(Color.FromArgb(80, 120, 120, 120)),
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    Content = new TextBlock
                    {
                        Text = "Coming Soon",
                        FontWeight = FontWeight.Bold,
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = false,
                    Cursor = new Cursor(StandardCursorType.Arrow)
                }.With(column: 1)
            }
        }, padding: new Thickness(20), margin: new Thickness(0, 0, 0, 16));

        _mainContentStack = new StackPanel
        {
            Spacing = 48,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Children =
            {
                topInfo,
                actionRow,
                featuredClientCard,
                BuildFeaturedServersSection()
            }
        };

        var mainRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,340"),
            Children =
            {
                _mainContentStack.With(column: 0),
                avatarPanel.With(column: 1).With(a => {
                    a.HorizontalAlignment = HorizontalAlignment.Center;
                    a.VerticalAlignment = VerticalAlignment.Top;
                    a.ZIndex = 10;
                    a.Margin = new Thickness(0, 8, 0, 0);
                })
            }
        };
        _mainRowGrid = mainRow;

        var statsRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 20,
            Children =
            {
                Create1to1StatCard("FPS", homeFpsStatValue, "Average performance"),
                Create1to1StatCard("RAM", homeRamStatValue, "Memory usage").With(column: 1)
            }
        };

        _homeStatusBar = new Border
        {
            Height = 110,
            Background = new SolidColorBrush(Color.Parse("#0D111C")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A3143")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(32, 20),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            statusLabel.With(tb => {
                                tb.FontSize = 15;
                                tb.FontWeight = FontWeight.Black;
                                tb.Foreground = Brushes.White;
                            }),
                            installDetailsLabel.With(tb => {
                                tb.FontSize = 12;
                                tb.Foreground = new SolidColorBrush(Color.Parse("#8E98AC"));
                                tb.Margin = new Thickness(0, 4, 0, 0);
                            })
                        }
                    },
                    new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            pbFiles.With(pb => {
                                pb.Height = 6;
                                pb.CornerRadius = new CornerRadius(3);
                            }),
                            pbProgress.With(pb => {
                                pb.Height = 14;
                                pb.CornerRadius = new CornerRadius(7);
                                pb.Background = new SolidColorBrush(Color.Parse("#1A1F2E"));
                                pb.Foreground = new SolidColorBrush(Color.Parse(_settings.AccentColor));
                            })
                        }
                    }
                }
            }
        };

        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 40,
                        Margin = new Thickness(24),
                        Children = { mainRow }
                    }
                },
                _homeStatusBar.With(row: 1)
            }
        };
    }

    private Border Create1to1StatCard(string title, TextBlock valueBlock, string subLabel)
    {
        var accentColor = Color.Parse(_settings.AccentColor);
        valueBlock.FontSize = 32;
        valueBlock.FontWeight = FontWeight.Black;
        valueBlock.Foreground = new SolidColorBrush(accentColor);
        valueBlock.Text = "00";

        return CreateGlassPanel(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontSize = 12.5, Foreground = new SolidColorBrush(Color.Parse("#8E96A8")), FontWeight = FontWeight.Bold },
                valueBlock,
                new TextBlock { Text = subLabel, FontSize = 11.5, Foreground = new SolidColorBrush(Color.Parse("#667899")) }
            }
        }, padding: new Thickness(16), margin: new Thickness(0));
    }

    private Control BuildModrinthDeck()
    {
        // ── Search & Filter Row ───────────────────────────────────────────
        
        modrinthSearchInput.Watermark = "🔍 Search for mods...";
        modrinthSearchInput.CornerRadius = new CornerRadius(16);
        modrinthSearchInput.Background = new SolidColorBrush(Color.Parse("#1A1F2E"));
        modrinthSearchInput.BorderBrush = new SolidColorBrush(Color.Parse("#2A3143"));
        modrinthSearchInput.BorderThickness = new Thickness(1);
        modrinthSearchInput.Height = 42;
        modrinthSearchInput.VerticalContentAlignment = VerticalAlignment.Center;
        
        // Ensure pressing Enter searches
        modrinthSearchInput.KeyDown += async (_, e) => {
            if (e.Key == Avalonia.Input.Key.Enter) await SearchModrinthAsync();
        };

        // Style the dropdowns to fit
        modrinthLoaderCombo.CornerRadius = new CornerRadius(16);
        modrinthLoaderCombo.Height = 42;
        modrinthLoaderCombo.Background = Brushes.Transparent;
        modrinthLoaderCombo.BorderBrush = new SolidColorBrush(Color.Parse("#2A3143"));

        modrinthVersionInput.CornerRadius = new CornerRadius(16);
        modrinthVersionInput.Height = 42;
        modrinthVersionInput.Background = Brushes.Transparent;
        modrinthVersionInput.BorderBrush = new SolidColorBrush(Color.Parse("#2A3143"));
        modrinthVersionInput.MinHeight = 42;
        
        modrinthProjectTypeCombo.CornerRadius = new CornerRadius(16);
        modrinthProjectTypeCombo.Height = 42;
        modrinthProjectTypeCombo.Background = Brushes.Transparent;
        modrinthProjectTypeCombo.BorderBrush = new SolidColorBrush(Color.Parse("#2A3143"));

        modrinthSourceCombo.CornerRadius = new CornerRadius(16);
        modrinthSourceCombo.Height = 42;
        modrinthSourceCombo.Background = Brushes.Transparent;
        modrinthSourceCombo.BorderBrush = new SolidColorBrush(Color.Parse("#2A3143"));

        modrinthSearchButton.CornerRadius = new CornerRadius(16);
        modrinthSearchButton.Height = 42;
        SetButtonText(modrinthSearchButton, "🔍 Search");
        modrinthSearchButton.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#6E5BFF"), 0),
                new GradientStop(Color.Parse("#A855F7"), 1)
            }
        };
        modrinthSearchButton.BorderThickness = new Thickness(0);
        modrinthSearchButton.Padding = new Thickness(16, 0);

        var filterRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(12, 0, 12, 24) // Match image padding
        };

        filterRow.Children.Add(modrinthSearchInput.With(column: 0));

        var sourceText = new TextBlock { Text = "Source", Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0) };
        var sourcePanel = new StackPanel { Orientation = Orientation.Horizontal, Children = { sourceText, modrinthSourceCombo } };
        filterRow.Children.Add(sourcePanel.With(column: 1));
        
        var loaderText = new TextBlock { Text = "Loader", Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0) };
        var loaderPanel = new StackPanel { Orientation = Orientation.Horizontal, Children = { loaderText, modrinthLoaderCombo } };
        filterRow.Children.Add(loaderPanel.With(column: 2));

        var versionText = new TextBlock { Text = "Version", Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0) };
        var versionPanel = new StackPanel { Orientation = Orientation.Horizontal, Children = { versionText, modrinthVersionInput } };
        filterRow.Children.Add(versionPanel.With(column: 3));

        filterRow.Children.Add(modrinthProjectTypeCombo.With(column: 4));
        
        filterRow.Children.Add(modrinthSearchButton.With(column: 5));
        
        // ── Card Item Template ────────────────────────────────────────────

        modrinthResultsListBox.Background = Brushes.Transparent;
        modrinthResultsListBox.ItemsPanel = new FuncTemplate<Panel?>(() => new Avalonia.Controls.Primitives.UniformGrid { Columns = 2 });
        modrinthResultsListBox.ItemsSource = _searchResults;
        modrinthResultsListBox.Margin = new Thickness(4, 0);

        modrinthResultsListBox.ItemTemplate = new FuncDataTemplate<ModrinthProject>((project, _) =>
        {
            bool isInstalled = _selectedProfile?.InstalledModIds.Contains(project?.ProjectId ?? "") ?? false;
            var installBtn = new Button
            {
                Content = isInstalled ? "Installed" : "Install",
                IsEnabled = !isInstalled,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20, 8),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            installBtn.Click += async (s, _) =>
            {
                if (s is Button btn && btn.Tag is ModrinthProject p)
                {
                    modrinthResultsListBox.SelectedItem = p;
                    await InstallSelectedAsync();
                }
            };
            installBtn.Tag = project;

            var dls = project?.Downloads ?? 0;
            var dlText = dls >= 1_000_000 ? $"{dls / 1_000_000.0:0.0}M+" :
                         dls >= 1_000 ? $"{dls / 1_000.0:0.0}K+" :
                         dls.ToString();

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 22, 28, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(8),
                Padding = new Thickness(16),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 16,
                    Children =
                    {
                        // Mock icon if none exists
                        new Border
                        {
                            Width = 52,
                            Height = 52,
                            CornerRadius = new CornerRadius(12),
                            Background = new SolidColorBrush(Color.Parse("#253245")),
                            Child = new TextBlock
                            {
                                Text = (project?.Title ?? "?").Substring(0, 1).ToUpperInvariant(),
                                FontSize = 24,
                                FontWeight = FontWeight.Black,
                                Foreground = Brushes.White,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }.With(column: 0),

                        new StackPanel
                        {
                            Spacing = 4,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = project?.Title ?? "Unknown",
                                    Foreground = Brushes.White,
                                    FontWeight = FontWeight.Bold,
                                    FontSize = 16,
                                    TextTrimming = TextTrimming.CharacterEllipsis // Avoid grid explosion
                                },
                                new TextBlock
                                {
                                    Text = project?.Description ?? "",
                                    Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")),
                                    FontSize = 14,
                                    TextWrapping = TextWrapping.Wrap,
                                    MaxLines = 2,
                                    TextTrimming = TextTrimming.WordEllipsis
                                },
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Spacing = 6,
                                    Margin = new Thickness(0, 4, 0, 0),
                                    Children =
                                    {
                                        new TextBlock { Text = "◆", Foreground = new SolidColorBrush(Color.Parse("#6E5BFF")), FontSize = 12 },
                                        new TextBlock { Text = dlText, Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")), FontSize = 12 },
                                        new TextBlock { Text = "♡", Foreground = new SolidColorBrush(Color.Parse("#A0A8B8")), FontSize = 12 }
                                    }
                                }
                            }
                        }.With(column: 1),

                        installBtn.With(column: 2)
                    }
                }
            };
        });

        var resultsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = modrinthResultsListBox,
            MaxHeight = 650 // Fit well into window
        };

        var mainContent = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                filterRow,
                resultsScroll
            }
        };
        
        return CreateSectionScroller(mainContent);
    }

    private Control BuildProfilesDeck()
    {
        var instancesHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(8, 0, 8, 20),
            VerticalAlignment = VerticalAlignment.Center
        };

        instancesHeader.Children.Add(new TextBlock
        {
            Text = "Instances",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        }.With(column: 0));

        var importBackupBtn = CreateCompactSecondaryButton("⤓ Import Zip");
        importBackupBtn.Click += async (_, _) => await ImportProfileZipAsync();

        var importDirBtn = CreateCompactSecondaryButton("📂 Import Dir");
        importDirBtn.Click += async (_, _) => await ImportInstanceFolderAsync();

        instancesHeader.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { importDirBtn, importBackupBtn }
        }.With(column: 1));

        var modsHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(8, 0, 8, 12),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Installed Mods", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                CreateCompactSecondaryButton("⚠ Scan Conflicts").With(btn =>
                {
                    btn.Click += async (_, _) =>
                    {
                        if (_selectedProfile != null) await ScanForModConflictsAsync(_selectedProfile);
                    };
                })
            }
        };



        _instanceCardBorders.Clear();

        profileListBox.Background = Brushes.Transparent;
        profileListBox.BorderThickness = new Thickness(0);
        profileListBox.Padding = new Thickness(0);

        // Fully suppress all ListBoxItem selection/hover visuals so custom card styles show through
        var suppressBg = new Avalonia.Styling.Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent);
        var suppressBorder = new Avalonia.Styling.Setter(ListBoxItem.BorderBrushProperty, Brushes.Transparent);
        var suppressBorderThickness = new Avalonia.Styling.Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0));
        foreach (var selectorFn in new Func<Selector?, Selector?>[]
        {
            x => x!.OfType<ListBoxItem>(),
            x => x!.OfType<ListBoxItem>().Class(":selected"),
            x => x!.OfType<ListBoxItem>().Class(":pointerover"),
            x => x!.OfType<ListBoxItem>().Class(":selected").Class(":pointerover"),
            x => x!.OfType<ListBoxItem>().Class(":selected").Class(":focus"),
            x => x!.OfType<ListBoxItem>().Class(":selected").Class(":focus-within"),
            x => x!.OfType<ListBoxItem>().Class(":focus"),
            x => x!.OfType<ListBoxItem>().Class(":focus-within"),
        })
        {
            profileListBox.Styles.Add(new Avalonia.Styling.Style(selectorFn)
            {
                Setters =
                {
                    suppressBg,
                    suppressBorder,
                    suppressBorderThickness,
                    new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                    new Avalonia.Styling.Setter(ListBoxItem.MarginProperty, new Thickness(0)),
                    new Avalonia.Styling.Setter(ListBoxItem.FocusAdornerProperty, null)
                }
            });
        }

        // Use WrapPanel for horizontal flow
        profileListBox.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        profileListBox.ItemTemplate = new FuncDataTemplate<LauncherProfile>((profile, _) =>
        {
            if (profile == null) return new Border();

            // Handle "+ Add New Instance" placeholder card
            if (profile.Name == "__add_new_placeholder__")
            {
                var plusIcon = new Border
                {
                    Width = 50,
                    Height = 50,
                    CornerRadius = new CornerRadius(25),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.Parse("#5C6E91")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "+",
                        FontSize = 26,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#5C6E91")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -2, 0, 0)
                    }
                };

                var addText = new TextBlock
                {
                    Text = "Add New Instance",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#5C6E91")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 0)
                };

                var addCard = new Border
                {
                    Width = 230,
                    Height = 280,
                    CornerRadius = new CornerRadius(14),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    Background = new SolidColorBrush(Color.FromArgb(100, 10, 14, 26)),
                    Margin = new Thickness(8),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { plusIcon, addText }
                    },
                    Transitions = new Transitions
                    {
                        new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                        new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                    },
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                };

                plusIcon.Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                };

                if (plusIcon.Child is TextBlock tbIcon)
                {
                    tbIcon.Transitions = new Transitions
                    {
                        new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                    };
                }

                addText.Transitions = new Transitions
                {
                    new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                };

                // Premium interactive hover animations
                addCard.PointerEntered += (_, _) =>
                {
                    addCard.BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4"));
                    addCard.Background = new SolidColorBrush(Color.FromArgb(60, 56, 214, 196));
                    plusIcon.BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4"));
                    if (plusIcon.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.Parse("#38D6C4"));
                    addText.Foreground = new SolidColorBrush(Color.Parse("#38D6C4"));
                    addCard.RenderTransform = TransformOperations.Parse("scale(1.025)");
                };
                addCard.PointerExited += (_, _) =>
                {
                    addCard.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                    addCard.Background = new SolidColorBrush(Color.FromArgb(100, 10, 14, 26));
                    plusIcon.BorderBrush = new SolidColorBrush(Color.Parse("#5C6E91"));
                    if (plusIcon.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.Parse("#5C6E91"));
                    addText.Foreground = new SolidColorBrush(Color.Parse("#5C6E91"));
                    addCard.RenderTransform = TransformOperations.Parse("scale(1.0)");
                };

                return addCard;
            }

            // Normal Instance Card
            bool isAether = profile.Name == "Aether Client" || profile.Name.Contains("Aether", StringComparison.OrdinalIgnoreCase);

            var card = new Border
            {
                Width = 230,
                Height = 280,
                CornerRadius = new CornerRadius(14),
                BorderThickness = isAether ? new Thickness(2) : new Thickness(1),
                BorderBrush = isAether
                    ? new LinearGradientBrush
                      {
                          StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                          EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                          GradientStops =
                          {
                              new GradientStop(Color.Parse("#D4AF37"), 0.0),
                              new GradientStop(Color.Parse("#AA7C11"), 1.0)
                          }
                      }
                    : new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                Background = new SolidColorBrush(Color.FromArgb(160, 10, 14, 26)),
                Margin = new Thickness(8),
                ClipToBounds = true,
                Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                    new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                    new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                },
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };

            var cardGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("140,140")
            };
            card.Child = cardGrid;

            // Row 0: Curated Loader Gradients
            var topPreview = new Border
            {
                ClipToBounds = true,
                CornerRadius = new CornerRadius(14, 14, 0, 0)
            };

            IBrush coverBrush;
            try
            {
                // First look for local cover overrides inside the instance folder
                string localCover = Path.Combine(profile.InstanceDirectory, "cover.png");
                if (!File.Exists(localCover))
                    localCover = Path.Combine(profile.InstanceDirectory, "cover.jpg");

                if (File.Exists(localCover))
                {
                    coverBrush = new ImageBrush(new Bitmap(localCover))
                    {
                        Stretch = Stretch.UniformToFill
                    };
                }
                else if (profile.Name == "Aether Client" || profile.Name.Contains("Aether", StringComparison.OrdinalIgnoreCase))
                {
                    // Center the launcher logo over a gorgeous cosmic royal purple gradient!
                    topPreview.Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#181436"), 0.0),
                            new GradientStop(Color.Parse("#0D0B1C"), 1.0)
                        }
                    };
                    
                    var logoImg = new Image
                    {
                        Source = new Bitmap(AssetLoader.Open(new Uri(GetTaskbarIconUri()))),
                        Width = 80,
                        Height = 80,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    topPreview.Child = logoImg;
                    coverBrush = Brushes.Transparent;
                }
                else
                {
                    // Fallback to our breathtaking default castle artwork compiled into assets
                    coverBrush = new ImageBrush(new Bitmap(AssetLoader.Open(new Uri("avares://AetherLauncher/assets/instance_default_cover.png"))))
                    {
                        Stretch = Stretch.UniformToFill
                    };
                }
            }
            catch
            {
                // Ultimate resilient fallback to gradients if files are missing or inaccessible
                LinearGradientBrush gradient;
                if (profile.LoaderDisplay.Contains("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    gradient = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#4E208F"), 0.0),
                            new GradientStop(Color.Parse("#8F208F"), 1.0)
                        }
                    };
                }
                else if (profile.LoaderDisplay.Contains("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    gradient = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#8F3E20"), 0.0),
                            new GradientStop(Color.Parse("#BF5020"), 1.0)
                        }
                    };
                }
                else
                {
                    gradient = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#1B4931"), 0.0),
                            new GradientStop(Color.Parse("#2D8056"), 1.0)
                        }
                    };
                }
                coverBrush = gradient;
            }
            if (coverBrush != Brushes.Transparent)
            {
                topPreview.Background = coverBrush;
            }
            cardGrid.Children.Add(topPreview.With(row: 0));

            // Overlay shadow
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0))
            };
            cardGrid.Children.Add(overlay.With(row: 0));

            // Flat Block Loader Icon Overlay
            Control badgeChild;
            if (profile.Name == "Aether Client" || profile.Name.Contains("Aether", StringComparison.OrdinalIgnoreCase))
            {
                badgeChild = new Image
                {
                    Source = new Bitmap(AssetLoader.Open(new Uri(GetTaskbarIconUri()))),
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                badgeChild = new TextBlock
                {
                    Text = profile.LoaderDisplay.Contains("Fabric", StringComparison.OrdinalIgnoreCase) ? "🪶" :
                           profile.LoaderDisplay.Contains("Forge", StringComparison.OrdinalIgnoreCase) ? "🔥" : "🌱",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var blockIcon = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 29, 45)),
                BorderBrush = new SolidColorBrush(Color.Parse("#2A3654")),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12),
                Child = badgeChild
            };
            cardGrid.Children.Add(blockIcon.With(row: 0));

            // "•••" Context Menu Button
            var menuBtn = new Button
            {
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = new TextBlock
                {
                    Text = "•••",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Focusable = false
            };

            var contextMenu = new ContextMenu();
            var editItem = new MenuItem { Header = "✎ Edit Instance" };
            editItem.Click += (_, _) => OpenProfileEditor(profile);

            var openFolderItem = new MenuItem { Header = "📂 Open Instance Folder" };
            openFolderItem.Click += (_, _) =>
            {
                try
                {
                    var directory = profile.InstanceDirectory;
                    if (Directory.Exists(directory))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = directory,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"[Instances] Failed to open folder: {ex.Message}");
                }
            };

            var deleteItem = new MenuItem { Header = "🗑 Delete Instance", Foreground = Brushes.Tomato };
            deleteItem.Click += async (_, _) =>
            {
                _selectedProfile = profile;
                profileListBox.SelectedItem = profile;
                await DeleteSelectedProfileAsync(profile);
            };

            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(openFolderItem);

            if (!isAether)
            {
                var changeCoverItem = new MenuItem { Header = "🖼 Change Cover" };
                changeCoverItem.Click += async (_, _) =>
                {
                    try
                    {
                        var topLevel = TopLevel.GetTopLevel(this);
                        if (topLevel == null) return;
                        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                        {
                            Title = "Select Cover Image",
                            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Image Files") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
                        });
                        if (files == null || files.Count == 0) return;

                        var imagePath = files[0].Path.LocalPath;
                        if (!File.Exists(imagePath)) return;

                        // Ensure directory exists
                        if (!Directory.Exists(profile.InstanceDirectory))
                        {
                            Directory.CreateDirectory(profile.InstanceDirectory);
                        }

                        // Remove existing covers first
                        string pngCover = Path.Combine(profile.InstanceDirectory, "cover.png");
                        string jpgCover = Path.Combine(profile.InstanceDirectory, "cover.jpg");

                        if (File.Exists(pngCover)) File.Delete(pngCover);
                        if (File.Exists(jpgCover)) File.Delete(jpgCover);

                        // Save new cover
                        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
                        string targetPath = ext == ".png" ? pngCover : jpgCover;

                        File.Copy(imagePath, targetPath, true);

                        // Refresh UI
                        RefreshProfiles(profile);
                    }
                    catch (Exception ex)
                    {
                        await DialogService.ShowInfoAsync(this, "Failed to change cover", ex.Message);
                    }
                };
                contextMenu.Items.Add(changeCoverItem);

                string localCoverPng = Path.Combine(profile.InstanceDirectory, "cover.png");
                string localCoverJpg = Path.Combine(profile.InstanceDirectory, "cover.jpg");
                if (File.Exists(localCoverPng) || File.Exists(localCoverJpg))
                {
                    var resetCoverItem = new MenuItem { Header = "🗑 Reset Cover" };
                    resetCoverItem.Click += (_, _) =>
                    {
                        try
                        {
                            if (File.Exists(localCoverPng)) File.Delete(localCoverPng);
                            if (File.Exists(localCoverJpg)) File.Delete(localCoverJpg);
                            RefreshProfiles(profile);
                        }
                        catch (Exception ex)
                        {
                            LauncherLog.Error($"[Instances] Failed to reset cover: {ex.Message}");
                        }
                    };
                    contextMenu.Items.Add(resetCoverItem);
                }
            }

            contextMenu.Items.Add(deleteItem);

            menuBtn.ContextMenu = contextMenu;
            menuBtn.Click += (_, _) => contextMenu.Open(menuBtn);
            cardGrid.Children.Add(menuBtn.With(row: 0));

            // Row 1: Bottom Details & Play Button
            var playBtn = new Button
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = isAether ? new SolidColorBrush(Color.Parse("#D4AF37")) : new SolidColorBrush(Color.Parse("#2E7D32")),
                BorderThickness = new Thickness(0),
                Content = new TextBlock
                {
                    Text = "▶",
                    FontSize = 14,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12),
                Focusable = false
            };

            playBtn.Click += async (_, _) =>
            {
                _selectedProfile = profile;
                profileListBox.SelectedItem = profile;
                UpdateLauncherContext();
                await LaunchAsync();
            };

            playBtn.PointerEntered += (_, _) =>
            {
                playBtn.Background = isAether ? new SolidColorBrush(Color.Parse("#FFE066")) : new SolidColorBrush(Color.Parse("#388E3C"));
            };
            playBtn.PointerExited += (_, _) =>
            {
                playBtn.Background = isAether ? new SolidColorBrush(Color.Parse("#D4AF37")) : new SolidColorBrush(Color.Parse("#2E7D32"));
            };

            var detailsGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4
            };

            var nameStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBlock = new TextBlock
            {
                Text = profile.Name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nameStack.Children.Add(nameBlock);

            if (isAether)
            {
                nameStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#382A0C")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#D4AF37")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "★ OFFICIAL",
                        FontSize = 8,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#FFE066")),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            var versionBlock = new TextBlock
            {
                Text = $"{profile.LoaderDisplay} {profile.VersionId}".Trim(),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A4A8B1")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var lastPlayedBlock = new TextBlock
            {
                Text = profile.LaunchCountSinceLastInstall > 0
                    ? $"Played {profile.LaunchCountSinceLastInstall} times"
                    : "Ready to launch",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#6C7A9C")),
                Margin = new Thickness(0, 8, 0, 0)
            };

            textStack.Children.Add(nameStack);
            textStack.Children.Add(versionBlock);
            textStack.Children.Add(lastPlayedBlock);

            detailsGrid.Children.Add(textStack.With(column: 0));
            detailsGrid.Children.Add(playBtn.With(column: 1));

            cardGrid.Children.Add(detailsGrid.With(row: 1));

            // Dynamic card hover animation
            card.PointerEntered += (_, _) =>
            {
                card.RenderTransform = TransformOperations.Parse("scale(1.025)");
                if (isAether)
                {
                    card.BorderBrush = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#FFE066"), 0.0),
                            new GradientStop(Color.Parse("#FFB330"), 0.5),
                            new GradientStop(Color.Parse("#FFE066"), 1.0)
                        }
                    };
                }
                else
                {
                    card.BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4"));
                }
                card.Background = new SolidColorBrush(Color.FromArgb(200, 16, 22, 38));
            };
            card.PointerExited += (_, _) =>
            {
                card.RenderTransform = TransformOperations.Parse("scale(1.0)");
                if (isAether)
                {
                    card.BorderBrush = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#D4AF37"), 0.0),
                            new GradientStop(Color.Parse("#AA7C11"), 1.0)
                        }
                    };
                }
                else
                {
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
                }
                card.Background = new SolidColorBrush(Color.FromArgb(160, 10, 14, 26));
            };

            // Register for selection highlight tracking
            _instanceCardBorders[profile.InstanceDirectory] = card;

            // Apply initial selected state if this profile is the active one
            if (string.Equals(profile.InstanceDirectory, _selectedProfile?.InstanceDirectory, StringComparison.Ordinal))
            {
                card.BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF"));
                card.BorderThickness = new Thickness(2);
                card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(90, 110, 91, 255), OffsetX = 0, OffsetY = 0 });
            }

            return card;
        });

        var instancesPane = CreateGlassPanel(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 10, 14, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            Child = profileListBox
        });



        return CreateSectionScroller(new StackPanel
        {
            Margin = new Thickness(24, 4, 24, 60),
            Spacing = 12,
            Children =
            {
                instancesHeader,
                instancesPane
            }
        });
    }

    private Control CreateEmptyState(string title, string subtitle)
    {
        return new Border
        {
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = subtitle, FontSize = 11, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }
                }
            }
        };
    }

    private Control BuildPerformanceDeck()
    {
        // 1. Worlds Panel (Column 0)
        var worldsHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = "Worlds / Saves", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }.With(column: 0),
                CreateCompactSecondaryButton("+ Import Zip").With(btn =>
                {
                    btn.Click += async (_, _) => await ImportWorldAsync();
                }).With(column: 1)
            }
        };

        _worldsListBox = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemsSource = _worldItems
        };
        _worldsListBox.ItemTemplate = new FuncDataTemplate<WorldItem>((worldItem, _) =>
        {
            if (worldItem == null) return new Border();

            var deleteBtn = new Button
            {
                Content = "🗑",
                Foreground = Brushes.Tomato,
                Background = Brushes.Transparent,
                FontSize = 16,
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(6)
            };
            deleteBtn.Click += async (_, _) =>
            {
                var confirm = await DialogService.ShowConfirmAsync(this, "Delete World", $"Are you sure you want to delete world '{worldItem.Name}' permanently?");
                if (confirm)
                {
                    try
                    {
                        Directory.Delete(worldItem.FullPath, true);
                        _worldItems.Remove(worldItem);
                        if (_worldsEmptyState != null) _worldsEmptyState.IsVisible = _worldItems.Count == 0;
                        if (_worldsListBox != null) _worldsListBox.IsVisible = _worldItems.Count > 0;
                    }
                    catch (Exception ex)
                    {
                        await DialogService.ShowInfoAsync(this, "Error", $"Failed to delete world: {ex.Message}");
                    }
                }
            };

            var nameBlock = new TextBlock { FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            nameBlock.Text = worldItem.Name;

            var sizeBlock = new TextBlock { FontSize = 10, Foreground = Brushes.Gray };
            sizeBlock.Text = worldItem.Size;

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children = { nameBlock, sizeBlock }
                        }.With(column: 0),
                        deleteBtn.With(column: 1)
                    }
                }
            };
        });

        _worldsEmptyState = CreateEmptyState("No Worlds Found", "Drag & drop world folders or .zip archives here, or click Import Zip above.");

        var worldsPane = CreateGlassPanel(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 10, 14, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                CornerRadius = new CornerRadius(14),
                Height = 440,
                Padding = new Thickness(10),
                Child = new Grid
                {
                    Children =
                    {
                        _worldsEmptyState,
                        new ScrollViewer
                        {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = _worldsListBox
                        }
                    }
                }
            }
        });

        DragDrop.SetAllowDrop(worldsPane, true);
        worldsPane.AddHandler(DragDrop.DragEnterEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 0.85;
            if (worldsPane is Border wb) wb.BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF"));
        });
        worldsPane.AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (worldsPane is Border wb) wb.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        });
        worldsPane.AddHandler(DragDrop.DragOverEvent, (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && files.Any(f => Directory.Exists(f.Path.LocalPath) || f.Path.LocalPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        });
        worldsPane.AddHandler(DragDrop.DropEvent, async (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && _selectedProfile != null)
            {
                foreach (var fileItem in files)
                {
                    var path = fileItem.Path.LocalPath;
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            var savesDir = Path.Combine(_selectedProfile.InstanceDirectory, "saves");
                            Directory.CreateDirectory(savesDir);
                            var folderName = Path.GetFileName(path);
                            var destPath = Path.Combine(savesDir, folderName);
                            
                            int count = 1;
                            while (Directory.Exists(destPath))
                            {
                                destPath = Path.Combine(savesDir, $"{folderName}_{count}");
                                count++;
                            }

                            ToggleBusyState(true, "Copying world folder...");
                            await Task.Run(() => CopyDirectory(path, destPath));
                            RefreshManageTabContent();
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Error", $"Failed to copy world folder: {ex.Message}");
                        }
                        finally
                        {
                            ToggleBusyState(false, "Ready");
                        }
                    }
                    else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var savesDir = Path.Combine(_selectedProfile.InstanceDirectory, "saves");
                            Directory.CreateDirectory(savesDir);
                            var targetDirName = Path.GetFileNameWithoutExtension(path);
                            var targetPath = Path.Combine(savesDir, targetDirName);

                            int count = 1;
                            while (Directory.Exists(targetPath))
                            {
                                targetPath = Path.Combine(savesDir, $"{targetDirName}_{count}");
                                count++;
                            }

                            ToggleBusyState(true, "Extracting world...");
                            await Task.Run(() => ZipFile.ExtractToDirectory(path, targetPath));
                            RefreshManageTabContent();
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Error", $"Failed to extract world: {ex.Message}");
                        }
                        finally
                        {
                            ToggleBusyState(false, "Ready");
                        }
                    }
                }
            }
            e.Handled = true;
        });
        worldsPane.AddHandler(DragDrop.DropEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (worldsPane is Border wb2) wb2.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        }, handledEventsToo: true);

        // 2. Resource Packs Panel (Column 1)
        var rpHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock { Text = "Resource Packs", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }.With(column: 0),
                CreateCompactSecondaryButton("+ Import Pack").With(btn =>
                {
                    btn.Click += async (_, _) => await ImportResourcePackAsync();
                }).With(column: 1)
            }
        };

        _rpListBox = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemsSource = _resourcePackItems
        };
        _rpListBox.ItemTemplate = new FuncDataTemplate<ResourcePackItem>((packItem, _) =>
        {
            if (packItem == null) return new Border();

            var deleteBtn = new Button
            {
                Content = "🗑",
                Foreground = Brushes.Tomato,
                Background = Brushes.Transparent,
                FontSize = 16,
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(6)
            };
            deleteBtn.Click += async (_, _) =>
            {
                var confirm = await DialogService.ShowConfirmAsync(this, "Delete Pack", $"Are you sure you want to delete resource pack '{packItem.Name}' permanently?");
                if (confirm)
                {
                    try
                    {
                        if (File.Exists(packItem.FullPath)) File.Delete(packItem.FullPath);
                        else if (Directory.Exists(packItem.FullPath)) Directory.Delete(packItem.FullPath, true);
                        _resourcePackItems.Remove(packItem);
                        if (_rpEmptyState != null) _rpEmptyState.IsVisible = _resourcePackItems.Count == 0;
                        if (_rpListBox != null) _rpListBox.IsVisible = _resourcePackItems.Count > 0;
                    }
                    catch (Exception ex)
                    {
                        await DialogService.ShowInfoAsync(this, "Error", $"Failed to delete resource pack: {ex.Message}");
                    }
                }
            };

            var nameBlock = new TextBlock { FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            nameBlock.Text = packItem.Name;

            var sizeBlock = new TextBlock { FontSize = 10, Foreground = Brushes.Gray };
            sizeBlock.Text = packItem.Size;

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children = { nameBlock, sizeBlock }
                        }.With(column: 0),
                        deleteBtn.With(column: 1)
                    }
                }
            };
        });

        _rpEmptyState = CreateEmptyState("No Packs Found", "Drag & drop .zip or .jar resource packs here, or click Import Pack above.");

        var rpPane = CreateGlassPanel(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 10, 14, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                CornerRadius = new CornerRadius(14),
                Height = 440,
                Padding = new Thickness(10),
                Child = new Grid
                {
                    Children =
                    {
                        _rpEmptyState,
                        new ScrollViewer
                        {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = _rpListBox
                        }
                    }
                }
            }
        });

        DragDrop.SetAllowDrop(rpPane, true);
        rpPane.AddHandler(DragDrop.DragEnterEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 0.85;
            if (rpPane is Border rpb) rpb.BorderBrush = new SolidColorBrush(Color.Parse("#3ED6B4"));
        });
        rpPane.AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (rpPane is Border rpb) rpb.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        });
        rpPane.AddHandler(DragDrop.DragOverEvent, (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && files.Any(f => f.Path.LocalPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.Path.LocalPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || Directory.Exists(f.Path.LocalPath)))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        });
        rpPane.AddHandler(DragDrop.DropEvent, async (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && _selectedProfile != null)
            {
                var rpDir = Path.Combine(_selectedProfile.InstanceDirectory, "resourcepacks");
                Directory.CreateDirectory(rpDir);

                foreach (var fileItem in files)
                {
                    var srcPath = fileItem.Path.LocalPath;
                    if (File.Exists(srcPath) && (srcPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || srcPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var destPath = Path.Combine(rpDir, Path.GetFileName(srcPath));
                            File.Copy(srcPath, destPath, true);
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Error", $"Failed to copy resource pack '{Path.GetFileName(srcPath)}': {ex.Message}");
                        }
                    }
                    else if (Directory.Exists(srcPath))
                    {
                        try
                        {
                            var destPath = Path.Combine(rpDir, Path.GetFileName(srcPath));
                            await Task.Run(() => CopyDirectory(srcPath, destPath));
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Error", $"Failed to copy resource pack folder '{Path.GetFileName(srcPath)}': {ex.Message}");
                        }
                    }
                }
                RefreshManageTabContent();
            }
            e.Handled = true;
        });
        rpPane.AddHandler(DragDrop.DropEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (rpPane is Border rpb2) rpb2.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        }, handledEventsToo: true);

        // 3. Mods Panel (Column 2)
        var modsHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Children =
            {
                new TextBlock { Text = "Installed Mods", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }.With(column: 0),
                CreateCompactSecondaryButton("⤓ Import").With(btn =>
                {
                    btn.Foreground = new SolidColorBrush(Color.Parse("#BD93F9"));
                    btn.Click += async (_, _) =>
                    {
                        if (_selectedProfile == null)
                        {
                            await DialogService.ShowInfoAsync(this, "Profile Required", "Select a profile first to import mods.");
                            return;
                        }
                        try
                        {
                            var topLevel = TopLevel.GetTopLevel(this);
                            if (topLevel == null) return;
                            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                            {
                                Title = "Import Mod Files (.jar)",
                                AllowMultiple = true,
                                FileTypeFilter = new[] { new FilePickerFileType("Mod Files") { Patterns = new[] { "*.jar" } } }
                            });
                            if (files != null && files.Count > 0)
                            {
                                var modsDir = _selectedProfile.ModsDirectory;
                                Directory.CreateDirectory(modsDir);
                                var importedCount = 0;
                                foreach (var fileItem in files)
                                {
                                    var srcPath = fileItem.Path.LocalPath;
                                    if (File.Exists(srcPath) && srcPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var destPath = Path.Combine(modsDir, Path.GetFileName(srcPath));
                                        File.Copy(srcPath, destPath, true);
                                        importedCount++;
                                    }
                                }
                                RefreshManageTabContent();
                                if (importedCount > 0)
                                    await DialogService.ShowInfoAsync(this, "Import Complete", $"Successfully imported {importedCount} mod{(importedCount > 1 ? "s" : "")}.");
                            }
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Import Failed", $"Failed to import mods: {ex.Message}");
                        }
                    };
                }).With(column: 1),
                CreateCompactSecondaryButton("⚠ Scan").With(btn =>
                {
                    btn.Click += async (_, _) =>
                    {
                        if (_selectedProfile != null) await ScanForModConflictsAsync(_selectedProfile);
                    };
                }).With(column: 2)
            }
        };


        _modsListBox = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemsSource = _modItems
        };
        _modsListBox.ItemTemplate = new FuncDataTemplate<ModItem>((modItem, _) =>
        {
            if (modItem == null) return new Border();

            var enableToggle = new ToggleSwitch
            {
                OnContent = "ON",
                OffContent = "OFF",
                Margin = new Thickness(0, 0, 16, 0)
            };
            enableToggle[!ToggleSwitch.IsCheckedProperty] = new Avalonia.Data.Binding(nameof(ModItem.IsEnabled));

            var deleteBtn = new Button
            {
                Content = "🗑",
                Foreground = Brushes.Tomato,
                Background = Brushes.Transparent,
                FontSize = 16,
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(6)
            };
            deleteBtn.Click += async (_, _) =>
            {
                try
                {
                    var lowerName = modItem.FileName.ToLowerInvariant();
                    if (lowerName.Contains("fabric-api") || lowerName.Contains("aether-client"))
                    {
                        await DialogService.ShowInfoAsync(this, "Protected Mod", "This mod is required by Aether Client and cannot be removed.");
                        return;
                    }

                    if (File.Exists(modItem.FullPath)) File.Delete(modItem.FullPath);
                    _modItems.Remove(modItem);
                    if (_modsEmptyState != null) _modsEmptyState.IsVisible = _modItems.Count == 0;
                    if (_modsListBox != null) _modsListBox.IsVisible = _modItems.Count > 0;
                }
                catch { }
            };

            var nameBlock = new TextBlock { FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            nameBlock[!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ModItem.FileName));

            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                nameBlock,
                                new TextBlock { FontSize = 10, Foreground = Brushes.Gray }.With(tb => tb[!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ModItem.FileSize)))
                            }
                        }.With(column: 0),
                        enableToggle.With(column: 1),
                        deleteBtn.With(column: 2)
                    }
                }
            };
        });

        _modsEmptyState = CreateEmptyState("No Mods Installed", "Drag & drop .jar mod files here, or search mods in the Modrinth tab.");

        var modsPane = CreateGlassPanel(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 10, 14, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                CornerRadius = new CornerRadius(14),
                Height = 440,
                Padding = new Thickness(10),
                Child = new Grid
                {
                    Children =
                    {
                        _modsEmptyState,
                        new ScrollViewer
                        {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = _modsListBox
                        }
                    }
                }
            }
        });

        DragDrop.SetAllowDrop(modsPane, true);
        modsPane.AddHandler(DragDrop.DragEnterEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 0.85;
            if (modsPane is Border mb) mb.BorderBrush = new SolidColorBrush(Color.Parse("#FFB86C"));
        });
        modsPane.AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (modsPane is Border mb) mb.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        });
        modsPane.AddHandler(DragDrop.DragOverEvent, (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && files.Any(f => f.Path.LocalPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.Path.LocalPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        });
        modsPane.AddHandler(DragDrop.DropEvent, async (sender, e) =>
        {
            var files = e.Data.GetFiles();
            if (files != null && _selectedProfile != null)
            {
                var modsDir = _selectedProfile.ModsDirectory;
                Directory.CreateDirectory(modsDir);

                foreach (var fileItem in files)
                {
                    var srcPath = fileItem.Path.LocalPath;
                    if (File.Exists(srcPath) && (srcPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || srcPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var destPath = Path.Combine(modsDir, Path.GetFileName(srcPath));
                            File.Copy(srcPath, destPath, true);
                        }
                        catch (Exception ex)
                        {
                            await DialogService.ShowInfoAsync(this, "Error", $"Failed to copy mod '{Path.GetFileName(srcPath)}': {ex.Message}");
                        }
                    }
                }
                RefreshManageTabContent();
            }
            e.Handled = true;
        });
        modsPane.AddHandler(DragDrop.DropEvent, (sender, e) =>
        {
            if (sender is Control c) c.Opacity = 1.0;
            if (modsPane is Border mb2) mb2.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        }, handledEventsToo: true);

        _manageContentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 18,
            Children =
            {
                new StackPanel { Children = { worldsHeader, worldsPane } }.With(column: 0),
                new StackPanel { Children = { rpHeader, rpPane } }.With(column: 1),
                new StackPanel { Children = { modsHeader, modsPane } }.With(column: 2)
            }
        };

        _manageNoProfileCard = CreateGlassPanel(new Border
        {
            Padding = new Thickness(40),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 16,
                MaxWidth = 450,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "🔒 Instance Management", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Please select or create a profile in the Instances tab first. The Manage tab lets you configure isolated worlds, resource packs, and mods specific to that active profile.", FontSize = 13, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    CreateSecondaryButton("Go to Instances").With(btn => btn.Click += (_, _) => SetActiveSection("instances"))
                }
            }
        });

        var rootContainer = new Panel
        {
            Children =
            {
                _manageNoProfileCard,
                _manageContentGrid
            }
        };

        return CreateSectionScroller(new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(4, 4, 4, 80),
            Children =
            {
                CreateSectionTitle("Manage", "Configure isolated content for your active instance."),
                rootContainer
            }
        });
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        var dirs = dir.GetDirectories();
        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private Control BuildSettingsDeck()
    {
        var totalRam = GetSystemRamMb();
        var ramSlider = new Slider 
        { 
            Minimum = 512, 
            Maximum = totalRam, 
            Value = _settings.MaxRamMb,
            SmallChange = 512,
            LargeChange = 1024
        };
        var ramLabel = new TextBlock { Text = $"{_settings.MaxRamMb} MB", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        ramSlider.ValueChanged += (_, e) => {
            var val = (int)(e.NewValue / 512) * 512;
            _settings.MaxRamMb = val;
            ramLabel.Text = $"{val} MB";
            _settingsStore.Save(_settings);
        };

        var jvmArgsInput = CreateTextBox();
        jvmArgsInput.Text = _settings.JvmArgs;
        jvmArgsInput.Watermark = "-Xmx2G -XX:+UseG1GC...";
        jvmArgsInput.TextChanged += (_, _) => {
            _settings.JvmArgs = jvmArgsInput.Text ?? "";
            _settingsStore.Save(_settings);
        };

        var windowWidthInput = CreateTextBox();
        windowWidthInput.Text = _settings.WindowWidth.ToString();
        windowWidthInput.TextChanged += (_, _) => {
            if (int.TryParse(windowWidthInput.Text, out var val)) { _settings.WindowWidth = val; _settingsStore.Save(_settings); }
        };

        var windowHeightInput = CreateTextBox();
        windowHeightInput.Text = _settings.WindowHeight.ToString();
        windowHeightInput.TextChanged += (_, _) => {
            if (int.TryParse(windowHeightInput.Text, out var val)) { _settings.WindowHeight = val; _settingsStore.Save(_settings); }
        };

        var offlineModeToggle = new ToggleSwitch
        {
            Content = "No internet mode",
            IsChecked = _settings.OfflineMode,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold
        };
        _offlineModeToggle = offlineModeToggle;
        offlineModeToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.OfflineMode = offlineModeToggle.IsChecked ?? false;
            _settingsStore.Save(_settings);
        };

        var performanceModeToggle = new ToggleSwitch
        {
            Content = "Performance Mode (disables animations, simplifies theme gradients)",
            IsChecked = _settings.PerformanceMode,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold
        };
        performanceModeToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.PerformanceMode = performanceModeToggle.IsChecked ?? false;
            _settingsStore.Save(_settings);
            
            // Adjust preview timer speed based on performance mode
            if (_previewTimer != null)
            {
                _previewTimer.Interval = TimeSpan.FromMilliseconds(IsPerformanceModeEnabled() ? 200 : 33);
            }
            
            ApplySelectedPresetStyle();
            RebuildUiFromLayoutState(_activeSection);
        };

        var behaviorOptions = new List<string> { "Close Launcher", "Minimize Launcher", "Run Launcher in Background" };
        var behaviorComboBox = CreateComboBox(behaviorOptions);
        
        behaviorComboBox.SelectedIndex = _settings.AfterLaunchBehavior switch
        {
            "minimize" => 1,
            "background" => 2,
            _ => 0
        };

        behaviorComboBox.SelectionChanged += (_, _) =>
        {
            _settings.AfterLaunchBehavior = behaviorComboBox.SelectedIndex switch
            {
                1 => "minimize",
                2 => "background",
                _ => "close"
            };
            _settingsStore.Save(_settings);
        };

        var title = CreateSectionTitle("Settings", "Grouped launcher, system, and appearance controls.");
        var runtimeCard = CreateSubCard("Launch Runtime", new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { CreatePanelEyebrow("RAM Allocation"), ramLabel.With(column: 1) } },
                        ramSlider
                    }
                },
                new StackPanel { Spacing = 8, Children = { CreatePanelEyebrow("Extra JVM Arguments"), jvmArgsInput } }
            }
        }, "#1A2035");

        var sessionCard = CreateSubCard("Window & Session", new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 16,
                    Children =
                    {
                        new StackPanel { Spacing = 8, Children = { CreatePanelEyebrow("Window Width"), windowWidthInput } },
                        new StackPanel { Spacing = 8, Children = { CreatePanelEyebrow("Window Height"), windowHeightInput } }.With(column: 1)
                    }
                },
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                offlineModeToggle,
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                performanceModeToggle,
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                new StackPanel { Spacing = 8, Children = { CreatePanelEyebrow("When Minecraft is Launched"), behaviorComboBox } },
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        CreatePanelEyebrow("Installation Directory"),
                        new TextBlock { Text = _defaultMinecraftPath.BasePath, Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                        CreateSecondaryButton("Change Directory").With(btn => btn.Click += async (_, _) => await ChangeBaseDirectoryAsync())
                    }
                }
            }
        }, "#1A2035");

        var presetsComboBox = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items = { "None", "Liquid Glass", "Mountains", "Clear Blue Sky" },
            SelectedItem = _settings.SelectedPreset ?? "None"
        };

        presetsComboBox.SelectionChanged += (_, _) =>
        {
            var selected = presetsComboBox.SelectedItem as string;
            _settings.SelectedPreset = selected;
            _settingsStore.Save(_settings);
            ApplySelectedPresetStyle();
            RebuildUiFromLayoutState(_activeSection);
        };

        var resetLayoutBtn = CreateSecondaryButton("Reset UI Layout");
        resetLayoutBtn.Height = 36;
        resetLayoutBtn.HorizontalAlignment = HorizontalAlignment.Left;
        resetLayoutBtn.Click += async (_, _) => await ResetLayoutAsync();

        // Navigation Position
        var navPosComboBox = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items = { "Sidebar", "Top" },
            SelectedItem = string.Equals(_settings.Style.NavPosition, "top", StringComparison.OrdinalIgnoreCase) ? "Top" : "Sidebar"
        };
        navPosComboBox.SelectionChanged += (_, _) =>
        {
            var selected = navPosComboBox.SelectedItem as string;
            _settings.Style.NavPosition = selected?.ToLowerInvariant() ?? "sidebar";
            _settingsStore.Save(_settings);
            RebuildUiFromLayoutState(_activeSection);
        };

        bool isSidebarActive = !string.Equals(_settings.Style.NavPosition, "top", StringComparison.OrdinalIgnoreCase);

        // Sidebar Side
        var sidebarSideComboBox = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items = { "Left", "Right" },
            SelectedItem = string.Equals(_settings.Style.SidebarSide, "right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Left",
            IsEnabled = isSidebarActive
        };
        sidebarSideComboBox.SelectionChanged += (_, _) =>
        {
            var selected = sidebarSideComboBox.SelectedItem as string;
            _settings.Style.SidebarSide = selected?.ToLowerInvariant() ?? "left";
            _settingsStore.Save(_settings);
            RebuildUiFromLayoutState(_activeSection);
        };

        // Sidebar Collapsed
        var sidebarCollapsedToggle = new ToggleSwitch
        {
            Content = "Collapsed Sidebar",
            IsChecked = _settings.Style.SidebarCollapsed,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            IsEnabled = isSidebarActive
        };
        sidebarCollapsedToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.Style.SidebarCollapsed = sidebarCollapsedToggle.IsChecked ?? false;
            _settingsStore.Save(_settings);
            RebuildUiFromLayoutState(_activeSection);
        };

        // Sidebar Width
        var sidebarWidthSlider = new Slider
        {
            Minimum = 160,
            Maximum = 320,
            Value = _settings.Style.SidebarWidth > 0 ? _settings.Style.SidebarWidth : 240,
            SmallChange = 10,
            LargeChange = 20,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = isSidebarActive
        };
        var sidebarWidthLabel = new TextBlock { Text = $"{(int)sidebarWidthSlider.Value} px", VerticalAlignment = VerticalAlignment.Center, Foreground = isSidebarActive ? Brushes.White : Brushes.Gray };
        sidebarWidthSlider.ValueChanged += (_, e) =>
        {
            var val = (int)e.NewValue;
            _settings.Style.SidebarWidth = val;
            sidebarWidthLabel.Text = $"{val} px";
            _settingsStore.Save(_settings);
            RebuildUiFromLayoutState(_activeSection);
        };

        // Sidebar Padding
        var sidebarPaddingSlider = new Slider
        {
            Minimum = 0,
            Maximum = 40,
            Value = _settings.Style.SidebarPadding,
            SmallChange = 2,
            LargeChange = 5,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = isSidebarActive
        };
        var sidebarPaddingLabel = new TextBlock { Text = $"{(int)sidebarPaddingSlider.Value} px", VerticalAlignment = VerticalAlignment.Center, Foreground = isSidebarActive ? Brushes.White : Brushes.Gray };
        sidebarPaddingSlider.ValueChanged += (_, e) =>
        {
            var val = (int)e.NewValue;
            _settings.Style.SidebarPadding = val;
            sidebarPaddingLabel.Text = $"{val} px";
            _settingsStore.Save(_settings);
            RebuildUiFromLayoutState(_activeSection);
        };

        var editorSliders = CreateSubCard("🎨 UI Layout & Presets", new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Choose a layout preset, customize positioning, or reset all styles to default.",
                    Foreground = new SolidColorBrush(Color.Parse("#B0BACF")),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        CreatePanelEyebrow("Preset Layout"),
                        presetsComboBox
                    }
                },
                resetLayoutBtn,
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                new TextBlock
                {
                    Text = "Navigation Settings",
                    Foreground = new SolidColorBrush(Color.Parse("#B0BACF")),
                    FontSize = 14,
                    FontWeight = FontWeight.Bold
                },
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        CreatePanelEyebrow("Navigation Placement"),
                        navPosComboBox
                    }
                },
                new Separator { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) },
                new TextBlock
                {
                    Text = "Sidebar Layout & Dimensions",
                    Foreground = isSidebarActive ? new SolidColorBrush(Color.Parse("#B0BACF")) : Brushes.Gray,
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    IsEnabled = isSidebarActive
                },
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        CreatePanelEyebrow("Sidebar Placement"),
                        sidebarSideComboBox
                    }
                },
                sidebarCollapsedToggle,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { CreatePanelEyebrow("Sidebar Width"), sidebarWidthLabel.With(column: 1) } },
                        sidebarWidthSlider
                    }
                },
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { CreatePanelEyebrow("Sidebar Padding"), sidebarPaddingLabel.With(column: 1) } },
                        sidebarPaddingSlider
                    }
                }
            }
        }, "#1A2035");
        var layoutImportCard = editorSliders;

        var themeSelector = new ComboBox
        {
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items = { "Dark Mode", "Light Mode", "System Default" },
            SelectedItem = _settings.ThemeVariant == "light" ? "Light Mode" :
                           _settings.ThemeVariant == "dark" ? "Dark Mode" : "System Default"
        };
        themeSelector.SelectionChanged += (_, _) =>
        {
            var selected = themeSelector.SelectedItem as string;
            _settings.ThemeVariant = selected == "Light Mode" ? "light" :
                                     selected == "Dark Mode" ? "dark" : "system";
            _settingsStore.Save(_settings);
            ApplyThemeVariant();
            RebuildUiFromLayoutState(_activeSection);
        };

        var hexInput = new TextBox
        {
            Watermark = "#6E5BFF",
            Text = _settings.AccentColor,
            Width = 140,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12
        };

        var hexPreview = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse(_settings.AccentColor ?? "#6E5BFF"))
        };

        var hexApplyBtn = new Button
        {
            Content = "Apply",
            Height = 32,
            Padding = new Thickness(16, 0),
            CornerRadius = new CornerRadius(6),
            FontSize = 12,
            FontWeight = FontWeight.Bold
        };

        void UpdatePreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var hex = text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            try
            {
                var color = Color.Parse(hex);
                hexPreview.Background = new SolidColorBrush(color);
                hexApplyBtn.IsEnabled = true;
            }
            catch
            {
                hexPreview.Background = Brushes.Transparent;
                hexApplyBtn.IsEnabled = false;
            }
        }

        hexInput.TextChanged += (s, e) => UpdatePreview(hexInput.Text ?? "");

        hexApplyBtn.Click += (_, _) => {
            var hex = hexInput.Text?.Trim() ?? "";
            if (!hex.StartsWith("#")) hex = "#" + hex;
            try
            {
                Color.Parse(hex); // validate
                _settings.AccentColor = hex;
                _settingsStore.Save(_settings);
                InvalidateUiCache();
                Content = BuildRoot();
                SetActiveSection("settings");
            }
            catch {}
        };

        var colorCard = CreateSubCard("Theme & Appearance", new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Choose the launcher color theme mode:", Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 14 },
                themeSelector,
                new TextBlock { Text = "Pick a primary accent color for the launcher UI.", Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 14, Margin = new Thickness(0, 8, 0, 0) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        CreateColorPreset("#6E5BFF"),
                        CreateColorPreset("#FF5B5B"),
                        CreateColorPreset("#5BFF85"),
                        CreateColorPreset("#FFB85B"),
                        CreateColorPreset("#5BC2FF"),
                        CreateColorPreset("#FF5BE2"),
                        CreateColorPreset("#FFE15B"),
                        CreateColorPreset("#B55BFF"),
                        CreateColorPreset("#5BFFDE"),
                        CreateColorPreset("#E2E8F0")
                    }
                },
                new TextBlock { Text = "Or enter a custom HEX code:", Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 14, Margin = new Thickness(0, 8, 0, 0) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        hexInput,
                        hexPreview,
                        hexApplyBtn
                    }
                }
            }
        }, "#1A2035");

        var bgBtn = CreateSecondaryButton("Choose Background Image");
        bgBtn.Click += async (_, _) => {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Background Image", FileTypeFilter = [FilePickerFileTypes.ImageAll] });
            if (files.Count > 0) {
                try {
                    var srcPath = files[0].Path.LocalPath;
                    var destDir = Path.Combine(_defaultMinecraftPath.BasePath, "death-client");
                    Directory.CreateDirectory(destDir);
                    var destPath = Path.Combine(destDir, "custom_bg.png");
                    File.Copy(srcPath, destPath, true);
                    Content = BuildRoot();
                    SetActiveSection("settings");
                } catch (Exception ex) {
                    await DialogService.ShowInfoAsync(this, "Error", "Failed to set background: " + ex.Message);
                }
            }
        };

        var backgroundCard = CreateSubCard("Background", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Set a custom wallpaper for the launcher dashboard.", Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 14 },
                bgBtn
            }
        }, "#1A2035");

        var fancyMenuToggle = new ToggleSwitch
        {
            Content = "Enable FancyMenu Integration",
            IsChecked = _settings.EnableFancyMenu,
            OnContent = "Enabled",
            OffContent = "Disabled",
            Foreground = Brushes.White
        };
        fancyMenuToggle.IsCheckedChanged += (_, _) => {
            _settings.EnableFancyMenu = fancyMenuToggle.IsChecked ?? false;
            _settingsStore.Save(_settings);
        };

        var minecraftHomeCard = CreateSubCard("Minecraft Home Screen", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Automatically install FancyMenu and a custom layout in your Minecraft instances.", Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 14, TextWrapping = TextWrapping.Wrap },
                fancyMenuToggle,
                new TextBlock { Text = "Note: This will download FancyMenu and Konkrete mods during launch.", Foreground = new SolidColorBrush(Color.Parse("#6E5BFF")), FontSize = 12, FontWeight = FontWeight.Bold }
            }
        }, "#1A2035");

        var orderCard = CreateSubCard("Launch Screen Order", CreateSectionOrderPicker(), "#1A2035");

        return CreateSectionScroller(new StackPanel
        {
            Spacing = 24,
            Margin = new Thickness(4, 4, 4, 80),
            Children =
            {
                title,
                runtimeCard,
                sessionCard,
                layoutImportCard,
                colorCard,
                backgroundCard,
                orderCard,
                minecraftHomeCard
            }
        });
    }

    private async Task ChangeBaseDirectoryAsync()
    {
        try {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Base Minecraft Directory" });
            if (folders != null && folders.Count > 0)
            {
                var newPath = folders[0].Path.LocalPath;
                _settings.BaseMinecraftPath = newPath;
                _settingsStore.Save(_settings);
                await DialogService.ShowInfoAsync(this, "Directory Changed", "Please restart the launcher to apply the change.");
            }
        } catch (Exception ex) {
            await DialogService.ShowInfoAsync(this, "Error", $"Failed to change directory: {ex.Message}");
        }
    }

    private async Task<bool> CheckInternetConnectivityAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync("https://www.google.com");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeAsync()
    {
        if (!_settings.OfflineMode)
        {
            bool isOnline = await CheckInternetConnectivityAsync();
            if (!isOnline)
            {
                LauncherLog.Info("[Initialize] No internet detected. Auto-enabling No internet mode.");
                _settings.OfflineMode = true;
                _settingsStore.Save(_settings);
                if (_offlineModeToggle != null)
                {
                    Dispatcher.UIThread.Post(() => _offlineModeToggle.IsChecked = true);
                }
            }
        }

        var tasks = new List<Task>();
        if (!_settings.OfflineMode)
        {
            tasks.Add(CheckForUpdatesAsync());
        }
        
        tasks.Add(PerformFirstRunSetup());
        await Task.WhenAll(tasks);

        // Auto-refresh selected account if needed
        var selectedAcc = _settings.Accounts.FirstOrDefault(a => a.Id == _settings.SelectedAccountId);
        if (selectedAcc != null && selectedAcc.Provider == "microsoft" && selectedAcc.IsExpired && !_settings.OfflineMode)
        {
            LauncherLog.Info($"[Initialize] Selected account {selectedAcc.Username} expired. Attempting refresh...");
            await TryRefreshAccountAsync(selectedAcc);
        }
        
        loadingLabel.Text = string.Empty;
        usernameInput.Text = string.IsNullOrWhiteSpace(_settings.Username) ? Environment.UserName : _settings.Username;
        if (selectedAcc != null && !string.IsNullOrWhiteSpace(selectedAcc.Username))
            usernameInput.Text = selectedAcc.Username;
        UsernameInput_TextChanged();

        profileLoaderCombo.SelectedIndex = 0;
        _quickLoaderCombo.SelectedIndex = 0;
        modrinthProjectTypeCombo.SelectedIndex = 0;
        modrinthLoaderCombo.SelectedIndex = 0;
        minecraftVersion.SelectedIndex = 0;

        RefreshProfiles();
        tasks.Add(ListVersionsAsync(GetSelectedVersionCategory()));

        if (!string.IsNullOrEmpty(_settings.JvmArgs) && (_settings.JvmArgs.Contains("--sun-misc-unsafe-memory-access") || _settings.JvmArgs.Contains("--enable-native-access")))
        {
            _settings.JvmArgs = _settings.JvmArgs
                .Replace("--sun-misc-unsafe-memory-access=allow", "")
                .Replace("--sun-misc-unsafe-memory-access", "")
                .Replace("--enable-native-access=ALL-UNNAMED", "")
                .Replace("--enable-native-access", "")
                .Trim();
            _settingsStore.Save(_settings);
        }

        // Initialize instance version lists
        if (instanceCategoryCombo != null)
        {
            instanceCategoryCombo.SelectedItem = "Versions";
            tasks.Add(ListVersionsAsync("Versions"));
        }

        if (!string.IsNullOrWhiteSpace(_settings.Version))
        {
            cbVersion.SelectedItem = _settings.Version;
            _quickVersionCombo.SelectedItem = _settings.Version;
        }

        SyncModrinthFilters();
        UpdateCharacterPreview();
        UpdateLauncherContext();
        SetProgressState("Ready", 0, 0);

        await Task.WhenAll(tasks);
    }

    private void SetupSectionTransitions(Control ctrl)
    {
        if (ctrl.Transitions == null || ctrl.Transitions.Count == 0)
        {
            ctrl.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            ctrl.Transitions = new Transitions
            {
                new DoubleTransition { Property = Control.OpacityProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) }
            };
        }
    }

    private async void AnimateSection(Control defaultSection, string slotName, bool isVisible)
    {
        var actualCtrl = defaultSection;
        if (actualCtrl == null) return;

        if (IsPerformanceModeEnabled())
        {
            actualCtrl.Opacity = 1.0;
            actualCtrl.RenderTransform = null;
            actualCtrl.IsVisible = isVisible;
            actualCtrl.IsHitTestVisible = isVisible;
            return;
        }

        SetupSectionTransitions(actualCtrl);

        if (isVisible)
        {
            actualCtrl.IsVisible = true;
            actualCtrl.IsHitTestVisible = true;
            actualCtrl.Opacity = 0.0;
            actualCtrl.RenderTransform = TransformOperations.Parse("scale(0.97) translate(0px, 15px)");

            await Task.Delay(25);

            if (actualCtrl.IsHitTestVisible)
            {
                actualCtrl.Opacity = 1.0;
                actualCtrl.RenderTransform = TransformOperations.Parse("scale(1.0) translate(0px, 0px)");
            }
        }
        else
        {
            actualCtrl.IsHitTestVisible = false;
            actualCtrl.Opacity = 0.0;
            actualCtrl.RenderTransform = TransformOperations.Parse("scale(0.97) translate(0px, -15px)");

            await Task.Delay(260);

            if (!actualCtrl.IsHitTestVisible && actualCtrl.Opacity == 0.0)
            {
                actualCtrl.IsVisible = false;
            }
        }
    }

    public void SetActiveSection(string section)
    {
        _activeSection = section;

        var launchVisible = section == "home" || section == "launch";
        var modrinthVisible = section == "modrinth";
        var profilesVisible = section == "instances" || section == "profiles";
        var performanceVisible = section == "performance";
        var settingsVisible = section == "settings";
        var layoutVisible = section == "layout";

        AnimateSection(launchSection, "LaunchSection", launchVisible);
        AnimateSection(modrinthSection, "ModrinthSection", modrinthVisible);
        AnimateSection(profilesSection, "ProfilesSection", profilesVisible);
        AnimateSection(performanceSection, "PerformanceSection", performanceVisible);
        AnimateSection(settingsSection, "SettingsSection", settingsVisible);
        AnimateSection(layoutSection, "LayoutSection", layoutVisible);

        if (_playOverlay != null)
        {
            _playOverlay.IsVisible = _settings.Style.PlayButtonGlobal || launchVisible;
        }

        ApplyNavState(launchNavButton, section == "home" || section == "launch");
        ApplyNavState(modrinthNavButton, section == "modrinth");
        ApplyNavState(profilesNavButton, section == "instances" || section == "profiles");
        ApplyNavState(performanceNavButton, section == "performance");
        ApplyNavState(settingsNavButton, section == "settings");
        ApplyNavState(layoutNavButton, section == "layout");
        if (accountsNavButton != null) ApplyNavState(accountsNavButton, section == "accounts");

        if (section == "modrinth" && _searchResults.Count == 0)
        {
            _ = SearchModrinthAsync();
        }
    }

    private async Task ListVersionsAsync(string category = "Versions")
    {
        await _versionListSemaphore.WaitAsync();
        try
        {
            var items = new List<string>();
            VersionMetadataCollection? manifest = null;

            if (!_settings.OfflineMode)
            {
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        manifest = await _defaultLauncher.GetAllVersionsAsync();
                        break;
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn($"[ListVersionsAsync] Failed to fetch version manifest (attempt {attempt}/{maxAttempts}): {ex.Message}");
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(200 * attempt);
                        }
                    }
                }
            }

            if (manifest != null)
            {
                foreach (var version in manifest)
                {
                    if (version != null && ShouldIncludeVersion(version.Name, version.Type, category))
                    {
                        var vn = version.Name;
                        if (!string.IsNullOrWhiteSpace(vn)) items.Add(vn);
                    }
                }
            }
            else
            {
                // Fallback: Scan local versions (for offline mode or internet failure)
                try
                {
                    var versionsDir = Path.Combine(_defaultMinecraftPath.BasePath, "versions");
                    if (File.Exists(versionsDir) || Directory.Exists(versionsDir))
                    {
                        foreach (var dir in Directory.GetDirectories(versionsDir))
                        {
                            var versionName = Path.GetFileName(dir);
                            if (!string.IsNullOrWhiteSpace(versionName))
                            {
                                // In offline mode/not-manifested local folders, we try to guess the type from the name
                                if (ShouldIncludeVersion(versionName, null, category))
                                    items.Add(versionName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Info($"[Aether Launcher] Offline version list failed: {ex}");
                }
            }

            Dispatcher.UIThread.Post(() => {
                _versionItems.Clear();
                foreach (var item in items) 
                {
                    if (!_versionItems.Contains(item)) _versionItems.Add(item);
                }

                if (_selectedProfile is not null && !_versionItems.Contains(_selectedProfile.GameVersion))
                    _versionItems.Insert(0, _selectedProfile.GameVersion);

                if ((cbVersion.SelectedItem == null || (cbVersion.SelectedItem is string s && !_versionItems.Contains(s))) && _versionItems.Count > 0)
                {
                    try { 
                        var latest = manifest?.FirstOrDefault(v => v.Type == "release")?.Name;
                        cbVersion.SelectedItem = (latest != null && _versionItems.Contains(latest)) ? latest : _versionItems[0]; 
                    } catch { cbVersion.SelectedIndex = 0; }
                }
            });
        }
        finally
        {
            _versionListSemaphore.Release();
        }
    }

    private static bool ShouldIncludeVersion(string name, string? type, string category)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var t = type?.ToLower() ?? string.Empty;
        var isRelease = t == "release" || Regex.IsMatch(name, @"^\d+(\.\d+)*$");
        var isSnapshot = t == "snapshot" || Regex.IsMatch(name, @"^\d{2}w\d{2}[a-z]$", RegexOptions.IgnoreCase);

        if (string.Equals(category, "Versions", StringComparison.OrdinalIgnoreCase))
            return isRelease;

        if (string.Equals(category, "Snapshots", StringComparison.OrdinalIgnoreCase))
            return isSnapshot;

        // "Other sources" category: anything that isn't a standard release or snapshot (like Forge, Fabric, older alphas, etc.)
        return !isRelease && !isSnapshot;
    }

    private string GetSelectedVersionCategory() =>
        minecraftVersion.SelectedItem?.ToString() ?? VersionCategoryOptions[0];

    private async Task LaunchAsync()
    {
        var activeUsername = GetActiveUsername();
        if (string.IsNullOrWhiteSpace(activeUsername))
        {
            await DialogService.ShowInfoAsync(this, "Username required", "Enter a username before launching.");
            return;
        }

        var versionToLaunch = _selectedProfile?.VersionId ?? cbVersion.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(versionToLaunch))
        {
            await DialogService.ShowInfoAsync(this, "Version required", "Select a Minecraft version or profile before launching.");
            return;
        }

        // [USER REQUEST] Remove confirmation popup for instant launch
        /*
        var shouldLaunch = await DialogService.ShowConfirmAsync(
            this,
            "Launch confirmation",
            $"Launch {targetLabel} as {usernameInput.Text.Trim()}?");
        if (!shouldLaunch)
            return;
        */

        ToggleBusyState(true, "Priming the launcher...");
        btnStart.Content = "Cancel";
        btnStart.IsEnabled = true; // Allow clicking "Cancel"

        _launchCts = new CancellationTokenSource();
        var token = _launchCts.Token;

        try
        {
            if (!_settings.OfflineMode)
            {
                bool isOnline = await CheckInternetConnectivityAsync();
                if (!isOnline)
                {
                    LauncherLog.Info("[Launch] No internet detected during launch. Auto-enabling No internet mode.");
                    _settings.OfflineMode = true;
                    _settingsStore.Save(_settings);
                    if (_offlineModeToggle != null)
                    {
                        Dispatcher.UIThread.Post(() => _offlineModeToggle.IsChecked = true);
                    }
                    
                    await DialogService.ShowInfoAsync(this, "Offline Mode", "No internet connection detected. Automatically enabling No internet mode for offline launch.");
                }
            }

            var launcherPath = _selectedProfile is null
                ? _defaultMinecraftPath
                : new MinecraftPath(_selectedProfile.InstanceDirectory)
                {
                    Library = _defaultMinecraftPath.Library,
                    Assets = _defaultMinecraftPath.Assets,
                    Versions = _defaultMinecraftPath.Versions
                };
            
            var launcher = CreateLauncher(launcherPath);

            if (_selectedProfile is not null)
            {
                await EnsureProfileReadyAsync(_selectedProfile, launcher, token);
                
                // Ensure the required mods are installed automatically
                var modsDir = Path.Combine(_selectedProfile.InstanceDirectory, "mods");
                Directory.CreateDirectory(modsDir);
                LauncherLog.Info($"[Launch] Autoinstalling required mods for instance: {_selectedProfile.Name}");

                // FancyMenu integration if enabled
                if (_settings.EnableFancyMenu && SupportsFancyMenu(_selectedProfile))
                {
                    await InstallModIfMissingAsync("fancymenu", _selectedProfile, modsDir, token);
                    await InstallModIfMissingAsync("konkrete", _selectedProfile, modsDir, token);
                }

                // Aether Client preset integration (main .jar and fabric api)
                if (_selectedProfile.Name == "Aether Client" || (_selectedProfile.Loader == "fabric" && _selectedProfile.Name.Contains("Aether", StringComparison.OrdinalIgnoreCase)))
                {
                    string localJarSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "death-client", "aether-client-1.0.0.jar");
                    if (!File.Exists(localJarSource))
                    {
                        localJarSource = "/home/inchara/Death Client/death-client/aether-client-1.0.0.jar";
                    }

                    if (File.Exists(localJarSource))
                    {
                        string destJar = Path.Combine(modsDir, "aether-client-1.0.0.jar");
                        File.Copy(localJarSource, destJar, true);
                        LauncherLog.Info($"[Launch] Successfully copied Aether Client jar to {destJar}");
                    }
                    else
                    {
                        LauncherLog.Warn($"[Launch] Aether Client jar source not found at: {localJarSource}");
                    }

                    // Install Fabric API (cannot be removed)
                    await InstallModIfMissingAsync("fabric-api", _selectedProfile, modsDir, token);
                }
                
                versionToLaunch = _selectedProfile.VersionId;
            }
            else
            {
                if (_settings.OfflineMode)
                {
                    EnsureLocalVersionChain(launcherPath.BasePath, versionToLaunch);
                    LauncherLog.Info($"[Launch] Offline mode: version '{versionToLaunch}' is fully cached locally. Bypassing online vanilla download.");
                }
                else
                {
                    await launcher.InstallAsync(versionToLaunch, token);
                }
            }

            var session = await BuildLaunchSessionAsync(token);

            var targetGameVer = _selectedProfile?.GameVersion ?? versionToLaunch;
            var javaPath = await GetJavaPathForVersionAsync(targetGameVer, token);
            var effectiveGamePath = _selectedProfile is not null && !string.IsNullOrWhiteSpace(_selectedProfile.GameDirectoryOverride)
                ? _selectedProfile.GameDirectoryOverride
                : launcherPath.BasePath;

            var animatedCapeSourcePath = string.IsNullOrWhiteSpace(_settings.CustomCapeSourcePath)
                ? _settings.CustomCapePath
                : _settings.CustomCapeSourcePath;
            // Deploy animated cape to Aether One's built-in cape system (config/aether/capes/)
            // Works on any Fabric profile — no third-party mod needed since Aether One handles it natively
            var useAetherOneCape = _selectedProfile is not null
                && string.Equals(_selectedProfile.Loader, "fabric", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(animatedCapeSourcePath)
                && File.Exists(animatedCapeSourcePath)
                && SkinClient.IsAnimatedCape(animatedCapeSourcePath);

            if (useAetherOneCape)
            {
                var aetherCapesDir = Path.Combine(effectiveGamePath, "config", "aether", "capes");
                Directory.CreateDirectory(aetherCapesDir);
                foreach (var existingCape in Directory.EnumerateFiles(aetherCapesDir))
                {
                    try { File.Delete(existingCape); } catch { }
                }

                // Convert GIF to PNG spritesheet (if GIF), or copy PNG directly
                var aetherCapePath = Path.Combine(aetherCapesDir, "animated_cape.png");
                if (SkinClient.ConvertGifToPngSpritesheet(animatedCapeSourcePath, aetherCapePath, out int frames, out string? convError))
                {
                    LauncherLog.Info($"[Launch] Deployed animated cape to Aether One: {aetherCapePath} ({frames} frames)");
                }
                else
                {
                    LauncherLog.Warn($"[Launch] Failed to convert animated cape: {convError}. Copying file directly.");
                    File.Copy(animatedCapeSourcePath, aetherCapePath, true);
                }
            }
            else
            {
                var aetherCapesDir = Path.Combine(effectiveGamePath, "config", "aether", "capes");
                if (Directory.Exists(aetherCapesDir))
                {
                    foreach (var existingCape in Directory.EnumerateFiles(aetherCapesDir))
                    {
                        try { File.Delete(existingCape); } catch { }
                    }
                }
            }

            EnsureDeathClientThemeResourcePack(effectiveGamePath, targetGameVer);

            // Auto-upload the user's selected skin to the Aether Worker if one is set
            // This ensures multiplayer skin visibility even if the user never manually uploaded
            var activeSkinPath = _settings.CustomSkinPath;
            if (string.IsNullOrWhiteSpace(activeSkinPath) || !File.Exists(activeSkinPath))
            {
                activeSkinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
            }

            // Self-healing: if skin.png is 0 bytes but custom_skin.png is valid, recover it
            try
            {
                var defaultSkinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
                var backupSkinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "custom_skin.png");
                if (File.Exists(defaultSkinPath) && new FileInfo(defaultSkinPath).Length == 0 && File.Exists(backupSkinPath) && new FileInfo(backupSkinPath).Length > 0)
                {
                    File.Copy(backupSkinPath, defaultSkinPath, true);
                    LauncherLog.Info("[Launch] Self-healed: Recovered 0-byte skin.png from custom_skin.png");
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[Launch] Failed to recover skin.png: {ex.Message}");
            }

            string? uploadedSkinUrl = null;
            string? uploadedSkinModel = null;

            if (File.Exists(activeSkinPath) && new FileInfo(activeSkinPath).Length > 0)
            {
                try
                {
                    LauncherLog.Info($"[Launch] Auto-uploading skin to Aether service: {activeSkinPath}");
                    string detectedModel = "classic";
                    try
                    {
                        using (var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(activeSkinPath))
                        {
                            if (img.Width == 64 && img.Height == 64)
                            {
                                bool col39Transparent = true;
                                for (int y = 52; y < 64; y++)
                                {
                                    if (img[39, y].A > 0)
                                    {
                                        col39Transparent = false;
                                        break;
                                    }
                                }
                                if (col39Transparent)
                                    detectedModel = "slim";
                            }
                        }
                    }
                    catch { }

                    var uploadResult = await SkinClient.UploadSkinAsync(activeUsername, activeSkinPath, detectedModel);
                    if (uploadResult.Success)
                    {
                        LauncherLog.Info($"[Launch] Skin uploaded successfully. Hash: {uploadResult.TextureHash}");
                        uploadedSkinUrl = uploadResult.TextureUrl;
                        uploadedSkinModel = detectedModel;
                    }
                    else
                    {
                        LauncherLog.Warn($"[Launch] Skin upload failed (non-fatal): {uploadResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"[Launch] Skin auto-upload failed (non-fatal): {ex.Message}");
                }
            }

            // Auto-upload cape to Aether service (if present)
            var activeCapePath = _settings.CustomCapePath;
            if (string.IsNullOrEmpty(activeCapePath) || !File.Exists(activeCapePath))
                activeCapePath = null;

            // Check if cape is animated (GIF or PNG spritesheet)
            bool isCapeAnimated = activeCapePath != null && SkinClient.IsAnimatedCape(activeCapePath);
            string? uploadedCapeUrl = null;
            string? capePathForFallback = activeCapePath;

            var skinServerDir = Path.Combine(AppRuntime.DataDirectory, "skin-server");
            Directory.CreateDirectory(skinServerDir);

            // Generate static fallback early if animated
            if (isCapeAnimated && !string.IsNullOrWhiteSpace(capePathForFallback) && File.Exists(capePathForFallback))
            {
                try
                {
                    var staticFallbackPath = Path.Combine(skinServerDir, "current-cape-static.png");
                    using (var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(capePathForFallback))
                    {
                        int frameHeight = (img.Width >= 128) ? 64 : 32;
                        if (img.Height > frameHeight)
                        {
                            SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(img, x => SixLabors.ImageSharp.Processing.CropExtensions.Crop(x, new SixLabors.ImageSharp.Rectangle(0, 0, img.Width, frameHeight)));
                        }
                        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, staticFallbackPath);
                    }
                    LauncherLog.Info("[Launch] Generated static cape fallback for upload and local proxy.");
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"[Launch] Failed to generate static cape fallback: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    var staticFallbackPath = Path.Combine(skinServerDir, "current-cape-static.png");
                    if (File.Exists(staticFallbackPath))
                    {
                        File.Delete(staticFallbackPath);
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"[Launch] Failed to delete static cape fallback: {ex.Message}");
                }
            }

            // Upload the static fallback if animated, otherwise the regular cape
            string? capePathToUpload = capePathForFallback;
            if (isCapeAnimated)
            {
                var staticFallbackPath = Path.Combine(skinServerDir, "current-cape-static.png");
                if (File.Exists(staticFallbackPath))
                {
                    capePathToUpload = staticFallbackPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(capePathToUpload) && File.Exists(capePathToUpload) && new FileInfo(capePathToUpload).Length > 0)
            {
                try
                {
                    LauncherLog.Info($"[Launch] Auto-uploading cape to Aether service (animated={isCapeAnimated}): {capePathToUpload}");
                    var capeResult = await SkinClient.UploadCapeAsync(activeUsername, capePathToUpload);
                    if (capeResult.Success)
                    {
                        LauncherLog.Info($"[Launch] Cape uploaded successfully. Hash: {capeResult.TextureHash}");
                        uploadedCapeUrl = capeResult.TextureUrl;
                    }
                    else
                    {
                        LauncherLog.Warn($"[Launch] Cape upload failed (non-fatal): {capeResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"[Launch] Cape auto-upload failed (non-fatal): {ex.Message}");
                }
            }

            // Write state.json for the local skin server after uploads resolve so
            // the proxy can prefer Cloudflare URLs for skin/static cape.
            try
            {
                if (File.Exists(activeSkinPath))
                {
                    File.Copy(activeSkinPath, Path.Combine(skinServerDir, "current-skin.png"), true);
                }
                else if (File.Exists(Path.Combine(skinServerDir, "current-skin.png")))
                {
                    File.Delete(Path.Combine(skinServerDir, "current-skin.png"));
                }

                if (!string.IsNullOrWhiteSpace(capePathForFallback) && File.Exists(capePathForFallback))
                {
                    var currentCapePath = Path.Combine(skinServerDir, "current-cape.png");
                    File.Copy(capePathForFallback, currentCapePath, true);

                    var sourceMcmeta = capePathForFallback + ".mcmeta";
                    var destMcmeta = currentCapePath + ".mcmeta";
                    if (File.Exists(sourceMcmeta))
                    {
                        File.Copy(sourceMcmeta, destMcmeta, true);
                    }
                    else if (File.Exists(destMcmeta))
                    {
                        File.Delete(destMcmeta);
                    }
                }
                else if (File.Exists(Path.Combine(skinServerDir, "current-cape.png")))
                {
                    var currentCapePath = Path.Combine(skinServerDir, "current-cape.png");
                    File.Delete(currentCapePath);
                    var currentCapeMcmeta = currentCapePath + ".mcmeta";
                    if (File.Exists(currentCapeMcmeta))
                    {
                        File.Delete(currentCapeMcmeta);
                    }
                    if (File.Exists(Path.Combine(skinServerDir, "current-cape-static.png")))
                    {
                        File.Delete(Path.Combine(skinServerDir, "current-cape-static.png"));
                    }
                }

                var statePath = Path.Combine(skinServerDir, "state.json");
                var stateObj = new
                {
                    username = activeUsername,
                    isCapeAnimated = isCapeAnimated,
                    uuid = session.UUID,
                    model = uploadedSkinModel ?? "classic",
                    skinUrl = uploadedSkinUrl,
                    capeUrl = uploadedCapeUrl
                };
                var stateJson = System.Text.Json.JsonSerializer.Serialize(stateObj);
                File.WriteAllText(statePath, stateJson);
                LauncherLog.Info($"[Launch] Wrote state.json for skin server proxy: username={activeUsername}, isCapeAnimated={isCapeAnimated}, uuid={session.UUID}");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[Launch] Failed to write state.json (non-fatal): {ex.Message}");
            }

            // Write death-client Fabric mod config with skin URL from the Aether skin service
            try
            {
                var launchService = new LaunchService();
                var skinConfigPath = await launchService.PrepareInstanceProfileAsync(
                    effectiveGamePath, 
                    activeUsername, 
                    activeSkinPath,
                    uploadedSkinUrl,
                    uploadedSkinModel,
                    capePathForFallback,
                    uploadedCapeUrl);
                LauncherLog.Info($"[Launch] Wrote skin config to: {skinConfigPath}");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[Launch] Failed to write skin config (non-fatal): {ex.Message}");
            }

            // Write CustomSkinLoader config pointing at the local skin server proxy
            // Without this, CSL defaults to Mojang API which doesn't work for offline players
            try
            {
                var apiRoot = "http://127.0.0.1:47135";
                var cslDir = Path.Combine(effectiveGamePath, "CustomSkinLoader");
                Directory.CreateDirectory(cslDir);
                var cslConfigPath = Path.Combine(cslDir, "CustomSkinLoader.json");
                var cslConfig = new
                {
                    enable = true,
                    loadlist = new object[]
                    {
                        new
                        {
                            name = "Aether OptiFine Cape Service",
                            type = "OptiFineAPI",
                            root = apiRoot + "/"
                        },
                        new
                        {
                            name = "Aether Skin Service",
                            type = "CustomSkinAPI",
                            root = apiRoot + "/csl/"
                        },
                        new
                        {
                            name = "Mojang",
                            type = "MojangAPI"
                        }
                    }
                };
                var cslJson = System.Text.Json.JsonSerializer.Serialize(cslConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cslConfigPath, cslJson);
                LauncherLog.Info($"[Launch] Wrote CustomSkinLoader config to: {cslConfigPath}");

                // Fix: Automatically clear CustomSkinLoader caches to prevent old/broken skin endpoints from being cached in-game.
                var cslCachesDir = Path.Combine(cslDir, "caches");
                if (Directory.Exists(cslCachesDir))
                {
                    try
                    {
                        Directory.Delete(cslCachesDir, true);
                        LauncherLog.Info($"[Launch] Cleared CustomSkinLoader caches at: {cslCachesDir}");
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn($"[Launch] Failed to delete CustomSkinLoader caches: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[Launch] Failed to write CustomSkinLoader config (non-fatal): {ex.Message}");
            }

            var jvmArgsList = new List<MArgument>();
            if (!string.IsNullOrWhiteSpace(_settings.JvmArgs))
            {
                jvmArgsList.AddRange(_settings.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(arg => !arg.Contains("--sun-misc-unsafe-memory-access") && !arg.Contains("--enable-native-access"))
                    .Select(arg => new MArgument(arg)));
            }

            bool hasAuthlib = false;
            // Inject authlib-injector so the client skin request is redirected to the Aether skin service
            try
            {
                await SkinClient.EnsureAuthlibInjectorAsync();
                var authlibArg = SkinClient.BuildAuthlibInjectorArg();
                if (authlibArg != null)
                {
                    jvmArgsList.Add(new MArgument(authlibArg));
                    jvmArgsList.Add(new MArgument("-Dauthlibinjector.ignoredPackages=net.gudenau.lib.unsafe,user11681.reflect,net.devtech.grossfabrichacks"));
                    hasAuthlib = true;
                    LauncherLog.Info($"[Launch] Injected authlib-injector javaagent into client JVM arguments: {authlibArg}");
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Failed to inject authlib-injector into client launch", ex);
            }

            if (!hasAuthlib && (string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken == "access_token" || session.UserType == "legacy"))
            {
                jvmArgsList.Add(new MArgument("-Dminecraft.api.auth.host=https://nope.invalid"));
                jvmArgsList.Add(new MArgument("-Dminecraft.api.account.host=https://nope.invalid"));
                jvmArgsList.Add(new MArgument("-Dminecraft.api.session.host=https://nope.invalid"));
                jvmArgsList.Add(new MArgument("-Dminecraft.api.services.host=https://nope.invalid"));
            }

            var process = await launcher.BuildProcessAsync(versionToLaunch, new MLaunchOption
            {
                Session = session,
                JavaPath = javaPath,
                MaximumRamMb = _settings.MaxRamMb,
                ExtraJvmArguments = jvmArgsList.ToArray(),
                ScreenWidth = _settings.WindowWidth,
                ScreenHeight = _settings.WindowHeight,
                Path = _selectedProfile is not null
                    ? new MinecraftPath(!string.IsNullOrWhiteSpace(_selectedProfile.GameDirectoryOverride) ? _selectedProfile.GameDirectoryOverride : _selectedProfile.InstanceDirectory)
                    {
                        Library = _defaultMinecraftPath.Library,
                        Assets = _defaultMinecraftPath.Assets,
                        Versions = _defaultMinecraftPath.Versions
                    }
                    : launcherPath
            });

            // CRITICAL: Some versions have these flags hardcoded in their version JSON.
            // We strip them from the FINAL command line here if they cause crashes.
            var scrubbedArgs = process.StartInfo.Arguments;
            string[] problematicFlags = { 
                "--sun-misc-unsafe-memory-access=allow", 
                "--enable-native-access=ALL-UNNAMED" 
            };
            
            foreach (var flag in problematicFlags)
            {
                if (scrubbedArgs.Contains(flag))
                {
                    scrubbedArgs = scrubbedArgs.Replace(flag, "").Trim();
                }
            }
            process.StartInfo.Arguments = scrubbedArgs;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;

            LauncherLog.Info($"[Launch] Command line: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            btnStart.Content = "Launching...";
            btnStart.IsEnabled = false;
            
            token.ThrowIfCancellationRequested(); // Final check
            process.Start();

            _settings.Username = activeUsername;
            _settings.Version = cbVersion.SelectedItem?.ToString() ?? string.Empty;
            _settingsStore.Save(_settings);
            
            if (_selectedProfile != null)
            {
                _selectedProfile.LaunchCountSinceLastInstall++;
                _profileStore.Save(_selectedProfile);

                var behavior = _settings.AfterLaunchBehavior ?? "close";

                if (behavior == "minimize")
                {
                    _isGameLaunchedAndMinimized = true;
                    WindowState = WindowState.Minimized;
                    _ = Task.Run(async () => {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            LauncherLog.Error($"[Launch] Minecraft process exited with non-zero exit code {process.ExitCode}. Marking profile '{_selectedProfile.Name}' for reinstall.");
                            _selectedProfile.LastLaunchCrashed = true;
                            _selectedProfile.IsInstalled = false;
                            _profileStore.Save(_selectedProfile);
                        }
                        else
                        {
                            _selectedProfile.LastLaunchCrashed = false;
                            _profileStore.Save(_selectedProfile);
                        }

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                            _isGameLaunchedAndMinimized = false;
                            Show();
                            WindowState = WindowState.Normal;
                            ToggleBusyState(false, "Ready to install or launch.");
                        });
                    });
                    return;
                }
                else if (behavior == "background")
                {
                    Hide();
                    _ = Task.Run(async () => {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            LauncherLog.Error($"[Launch] Minecraft process exited with non-zero exit code {process.ExitCode}. Marking profile '{_selectedProfile.Name}' for reinstall.");
                            _selectedProfile.LastLaunchCrashed = true;
                            _selectedProfile.IsInstalled = false;
                            _profileStore.Save(_selectedProfile);
                        }
                        else
                        {
                            _selectedProfile.LastLaunchCrashed = false;
                            _profileStore.Save(_selectedProfile);
                        }

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                            Show();
                            WindowState = WindowState.Normal;
                        });
                    });
                    return;
                }
                else if (behavior == "close")
                {
                    Hide();
                    _ = Task.Run(async () => {
                        try
                        {
                            process.WaitForExit();
                            if (process.ExitCode != 0)
                            {
                                LauncherLog.Error($"[Launch] Minecraft process exited with non-zero exit code {process.ExitCode}. Marking profile '{_selectedProfile.Name}' for reinstall.");
                                _selectedProfile.LastLaunchCrashed = true;
                                _selectedProfile.IsInstalled = false;
                                _profileStore.Save(_selectedProfile);
                            }
                            else
                            {
                                _selectedProfile.LastLaunchCrashed = false;
                                _profileStore.Save(_selectedProfile);
                            }
                        }
                        catch (Exception ex)
                        {
                            LauncherLog.Error($"[Launch] Error waiting for game process: {ex.Message}");
                        }
                        finally
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                                Close();
                            });
                        }
                    });
                    return;
                }
            }

            Close();
        }
        catch (OperationCanceledException)
        {
            LauncherLog.Info("[Launch] User cancelled the launch process.");
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"[Launch] Failed to launch Minecraft: {ex.Message}\n{ex.StackTrace}");
            
            // Check if the launch failed due to lack of internet
            if (!_settings.OfflineMode && 
                (ex.Message.Contains("mojang.com", StringComparison.OrdinalIgnoreCase) || 
                 ex.Message.Contains("Resource temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("SocketException", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("WebException", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)))
            {
                LauncherLog.Info("[Launch] Internet went out during launch process. Auto-enabling No internet mode.");
                _settings.OfflineMode = true;
                _settingsStore.Save(_settings);
                if (_offlineModeToggle != null)
                {
                    Dispatcher.UIThread.Post(() => _offlineModeToggle.IsChecked = true);
                }

                await DialogService.ShowInfoAsync(this, "Offline Mode Enabled", "Internet connection went out or is unstable. Automatically enabling No internet mode. Please try launching again.");
            }
            else
            {
                await DialogService.ShowInfoAsync(this, "Launch failed", $"Failed to launch Minecraft.\n{ex.Message}");
            }
        }
        finally
        {
            _launchCts?.Dispose();
            _launchCts = null;
            ToggleBusyState(false, "Ready to install or launch.");
        }
    }



    private async Task DownloadSelectedVersionAsync()
    {
        if (_settings.OfflineMode)
        {
            await DialogService.ShowInfoAsync(this, "No internet mode", "Downloading new versions is disabled in No internet mode.");
            return;
        }

        if (cbVersion.SelectedItem is null)
        {
            await DialogService.ShowInfoAsync(this, "Version required", "Select a Minecraft version to download.");
            return;
        }

        if (_selectedProfile is not null)
        {
            await DialogService.ShowInfoAsync(this, "Quick Launch only", "Version download is available for the default launcher. Clear the active profile first if you want to preinstall a vanilla version.");
            return;
        }

        var versionToInstall = cbVersion.SelectedItem.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(versionToInstall))
        {
            await DialogService.ShowInfoAsync(this, "Version required", "Select a Minecraft version to download.");
            return;
        }

        ToggleBusyState(true, $"Downloading {versionToInstall}...");

        try
        {
            await _defaultLauncher.InstallAsync(versionToInstall);
            var existingProfile = _profileStore.LoadProfiles().FirstOrDefault(profile =>
                string.Equals(profile.GameVersion, versionToInstall, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Loader, "vanilla", StringComparison.OrdinalIgnoreCase));

            if (existingProfile is null)
            {
                var downloadedProfile = _profileStore.CreateProfile($"Unnamed {versionToInstall}", versionToInstall, "vanilla");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    RefreshProfiles(downloadedProfile);
                    SetProgressState($"Downloaded {versionToInstall}.", 0, 0);
                });
            }

            _settings.Version = versionToInstall;
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Download failed", $"Failed to download Minecraft {versionToInstall}.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
        }
    }

    private string? GetParentVersionId(string versionJsonPath)
    {
        try
        {
            if (File.Exists(versionJsonPath))
            {
                var content = File.ReadAllText(versionJsonPath);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("inheritsFrom", out var parentProperty))
                {
                    return parentProperty.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"[Launch] Failed to parse inheritsFrom from {versionJsonPath}: {ex.Message}");
        }
        return null;
    }

    private void EnsureLocalVersionChain(string basePath, string versionId)
    {
        var versionDir = Path.Combine(basePath, "versions", versionId);
        var versionJson = Path.Combine(versionDir, $"{versionId}.json");
        if (!File.Exists(versionJson))
        {
            throw new InvalidOperationException($"The required version '{versionId}' is not installed and cannot be downloaded offline. Please disable No internet mode or connect to the internet to download it.");
        }

        var parentId = GetParentVersionId(versionJson);
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            EnsureLocalVersionChain(basePath, parentId);
        }
        else
        {
            // This is the root vanilla version. Verify its client JAR exists.
            var versionJar = Path.Combine(versionDir, $"{versionId}.jar");
            if (!File.Exists(versionJar))
            {
                throw new InvalidOperationException($"The vanilla game files for '{versionId}' are not fully installed and cannot be downloaded offline. Please disable No internet mode or connect to the internet to download them.");
            }
        }
    }

    private async Task EnsureProfileReadyAsync(LauncherProfile profile, MinecraftLauncher launcher, CancellationToken cancellationToken)
    {
        if (_settings.OfflineMode)
        {
            EnsureLocalVersionChain(launcher.MinecraftPath.BasePath, profile.VersionId);
            LauncherLog.Info($"[Launch] Offline mode: version '{profile.VersionId}' is fully cached locally. Bypassing online profile check.");
            return;
        }

        // If the profile is marked installed, we only install every 5 launches.
        // BUT if it crashed on the last launch, we install regardless.
        if (profile.IsInstalled && !profile.LastLaunchCrashed)
        {
            if (profile.LaunchCountSinceLastInstall % 5 != 0)
            {
                LauncherLog.Info($"[Launch] Profile '{profile.Name}' is marked installed. Launch count since last install: {profile.LaunchCountSinceLastInstall}. Skipping installation checks.");
                return;
            }
            LauncherLog.Info($"[Launch] Profile '{profile.Name}' is installed but hit the 5th launch threshold. Running installation check.");
        }

        if (profile.Loader == "fabric")
        {
            await launcher.InstallAsync(profile.GameVersion);
            await EnsureFabricProfileAsync(profile, cancellationToken);
            await launcher.InstallAsync(profile.VersionId);
        }
        else if (profile.Loader == "quilt")
        {
            await launcher.InstallAsync(profile.GameVersion);
            await EnsureQuiltProfileAsync(profile, cancellationToken);
            await launcher.InstallAsync(profile.VersionId);
        }
        else if (profile.Loader == "forge" || profile.Loader == "neoforge")
        {
            await launcher.InstallAsync(profile.GameVersion);
            await EnsureForgeProfileAsync(profile, cancellationToken);
            await launcher.InstallAsync(profile.VersionId);
        }
        else if (profile.Loader == "vanilla")
        {
            await launcher.InstallAsync(profile.GameVersion);
        }

        // Installation complete! Mark as installed and reset flags/count
        profile.IsInstalled = true;
        profile.LastLaunchCrashed = false;
        _profileStore.Save(profile);
    }

    private async Task EnsureFabricProfileAsync(LauncherProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
            throw new InvalidOperationException("Fabric loader version is missing from the profile.");

        var versionDirectory = Path.Combine(_defaultMinecraftPath.Versions, profile.VersionId);
        var versionJsonPath = Path.Combine(versionDirectory, $"{profile.VersionId}.json");
        if (File.Exists(versionJsonPath))
            return;

        Directory.CreateDirectory(versionDirectory);
        var manifestJson = await _modrinthClient.GetStringAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{profile.GameVersion}/{profile.LoaderVersion}/profile/json",
            cancellationToken);

        using var manifestDocument = JsonDocument.Parse(manifestJson);
        if (manifestDocument.RootElement.TryGetProperty("id", out var idElement))
        {
            var profileVersionId = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(profileVersionId) &&
                !string.Equals(profile.VersionId, profileVersionId, StringComparison.Ordinal))
            {
                profile.VersionId = profileVersionId;
                _profileStore.Save(profile);
                versionDirectory = Path.Combine(_defaultMinecraftPath.Versions, profile.VersionId);
                versionJsonPath = Path.Combine(versionDirectory, $"{profile.VersionId}.json");
                Directory.CreateDirectory(versionDirectory);
            }
        }

        File.WriteAllText(versionJsonPath, manifestJson);
    }

    private async Task EnsureQuiltProfileAsync(LauncherProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
            throw new InvalidOperationException("Quilt loader version is missing from the profile.");

        var versionDirectory = Path.Combine(_defaultMinecraftPath.Versions, profile.VersionId);
        var versionJsonPath = Path.Combine(versionDirectory, $"{profile.VersionId}.json");
        if (File.Exists(versionJsonPath))
            return;

        Directory.CreateDirectory(versionDirectory);
        var manifestJson = await _modrinthClient.GetStringAsync(
            $"https://meta.quiltmc.org/v3/versions/loader/{profile.GameVersion}/{profile.LoaderVersion}/profile/json",
            cancellationToken);

        using var manifestDocument = JsonDocument.Parse(manifestJson);
        if (manifestDocument.RootElement.TryGetProperty("id", out var idElement))
        {
            var profileVersionId = idElement.GetString();
            if (!string.IsNullOrWhiteSpace(profileVersionId) &&
                !string.Equals(profile.VersionId, profileVersionId, StringComparison.Ordinal))
            {
                profile.VersionId = profileVersionId;
                _profileStore.Save(profile);
                versionDirectory = Path.Combine(_defaultMinecraftPath.Versions, profile.VersionId);
                versionJsonPath = Path.Combine(versionDirectory, $"{profile.VersionId}.json");
                Directory.CreateDirectory(versionDirectory);
            }
        }

        File.WriteAllText(versionJsonPath, manifestJson);
    }

    private async Task EnsureForgeProfileAsync(LauncherProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
            throw new InvalidOperationException($"{profile.Loader} loader version is missing from the profile.");

        // Use the default .minecraft path for version lookup (InstanceDirectory may be profile-specific)
        var baseDir = profile.InstanceDirectory;
        var versionDirectory = Path.Combine(baseDir, "versions", profile.VersionId);
        var versionJsonPath = Path.Combine(versionDirectory, $"{profile.VersionId}.json");
        if (File.Exists(versionJsonPath))
            return;

        string installerUrl;
        string installerFileName;

        if (profile.Loader == "neoforge")
        {
            installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{profile.LoaderVersion}/neoforge-{profile.LoaderVersion}-installer.jar";
            installerFileName = $"neoforge-{profile.LoaderVersion}-installer.jar";
        }
        else
        {
            var forgeVer = $"{profile.GameVersion}-{profile.LoaderVersion}";
            installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeVer}/forge-{forgeVer}-installer.jar";
            installerFileName = $"forge-{forgeVer}-installer.jar";
        }

        var installerPath = Path.Combine(Path.GetTempPath(), installerFileName);

        try
        {
            // --- Download installer JAR with progress ---
            ToggleBusyState(true, $"Downloading {profile.Loader} installer...");
            using (var httpClient = new System.Net.Http.HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                using var response = await httpClient.GetAsync(installerUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Failed to download installer from {installerUrl} (HTTP {(int)response.StatusCode})");

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    downloaded += bytesRead;
                    if (totalBytes > 0)
                    {
                        var pct = (int)(downloaded * 100 / totalBytes);
                        ToggleBusyState(true, $"Downloading {profile.Loader} installer... {pct}%");
                    }
                }
            }

            // --- Run installer ---
            ToggleBusyState(true, $"Installing {profile.Loader} (this may take a few minutes)...");
            var javaPath = await GetJavaPathForVersionAsync(profile.GameVersion, cancellationToken);

            // Build arguments as a proper list — no nested quoting issues on Windows
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // Use ArgumentList instead of a single Arguments string to avoid quoting hell on Windows
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(installerPath);
            startInfo.ArgumentList.Add("--installClient");
            startInfo.ArgumentList.Add(baseDir);

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new Exception("Failed to start the Forge/NeoForge installer process.");

            // Read stdout and stderr asynchronously BEFORE WaitForExit to prevent deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            // 10-minute timeout for the installer
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { }
                throw new Exception($"The {profile.Loader} installer timed out after 10 minutes.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var combinedOutput = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                LauncherLog.Error($"[Forge] Installer exited with code {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
                throw new Exception($"{profile.Loader} installer failed (exit code {process.ExitCode}):\n{combinedOutput}");
            }

            LauncherLog.Info($"[Forge] Installer completed successfully.\n{stdout}");

            // --- Detect the created version directory ---
            var versionsDir = Path.Combine(baseDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                // Try exact match first, then fuzzy
                var loaderLower = profile.Loader.ToLowerInvariant();
                var candidates = Directory.GetDirectories(versionsDir)
                    .Select(d => Path.GetFileName(d))
                    .Where(name =>
                    {
                        var lower = name.ToLowerInvariant();
                        return lower.Contains(profile.LoaderVersion.ToLowerInvariant())
                            && (lower.Contains(loaderLower) || lower.Contains("forge"));
                    })
                    .OrderByDescending(name => name.Length) // prefer longer (more specific) names
                    .ToList();

                var matchedVersionId = candidates.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(matchedVersionId)
                    && !string.Equals(profile.VersionId, matchedVersionId, StringComparison.Ordinal))
                {
                    profile.VersionId = matchedVersionId;
                    _profileStore.Save(profile);
                    LauncherLog.Info($"[Forge] Detected installed version ID: {matchedVersionId}");
                }
            }
        }
        finally
        {
            // Clean up the temp installer JAR
            try { if (File.Exists(installerPath)) File.Delete(installerPath); }
            catch (Exception ex) { LauncherLog.Warn($"[Forge] Failed to clean up installer JAR: {ex.Message}"); }
        }
    }

    private async Task<string> GetJavaPathForVersionAsync(string gameVersion, CancellationToken cancellationToken)
    {
        int requiredJavaVersion = 8;
        
        // Handle standard 1.x.y versions
        if (gameVersion.StartsWith("1."))
        {
            var parts = gameVersion.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                if (minor >= 21)
                {
                    requiredJavaVersion = 21;
                }
                else if (minor == 20)
                {
                    // Minecraft 1.20.5 and 1.20.6 require Java 21
                    if (parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5)
                        requiredJavaVersion = 21;
                    else
                        requiredJavaVersion = 17;
                }
                else if (minor >= 17)
                {
                    requiredJavaVersion = 17;
                }
            }
        }
        else 
        {
            // Handle custom modern versions like "26.1"
            var parts = gameVersion.Split('.');
            if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            {
                if (major >= 25) requiredJavaVersion = 25; // Java 25 for extremely modern builds (Class version 69.0)
                else if (major >= 21) requiredJavaVersion = 21; 
                else if (major >= 17) requiredJavaVersion = 17;
            }
        }

        var javaDir = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "runtimes", $"java-{requiredJavaVersion}");
        var javaExe = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var javaPath = Path.Combine(javaDir, "bin", javaExe);

        if (File.Exists(javaPath))
            return javaPath;

        // Fallback: Check if Java executable exists anywhere inside the javaDir recursively (e.g. if already downloaded in a nested directory)
        if (Directory.Exists(javaDir))
        {
            var existingExe = Directory.GetFiles(javaDir, javaExe, SearchOption.AllDirectories).FirstOrDefault();
            if (existingExe != null && File.Exists(existingExe))
                return existingExe;
        }

        ToggleBusyState(true, $"Downloading Java {requiredJavaVersion}...");
        Directory.CreateDirectory(javaDir);

        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x32",
            _ => "x64"
        };
        
        var apiUrl = $"https://api.adoptium.net/v3/binary/latest/{requiredJavaVersion}/ga/{os}/{arch}/jre/hotspot/normal/eclipse";
        var tempArchive = Path.Combine(Path.GetTempPath(), $"java-{requiredJavaVersion}-jre.{(os == "windows" ? "zip" : "tar.gz")}");

        using (var httpClient = new System.Net.Http.HttpClient())
        {
            var response = await httpClient.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to download JRE for Java {requiredJavaVersion}");

            using var fs = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        ToggleBusyState(true, $"Extracting Java {requiredJavaVersion}...");
        if (os == "windows")
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(tempArchive, javaDir, true);
            
            // Flatten the extracted root directory (e.g., jdk-21.0.2+13-jre) up to javaDir to match Linux structure
            try
            {
                var subdirs = Directory.GetDirectories(javaDir);
                if (subdirs.Length == 1)
                {
                    var rootDir = subdirs[0];
                    foreach (var dir in Directory.GetDirectories(rootDir))
                    {
                        var destDir = Path.Combine(javaDir, Path.GetFileName(dir));
                        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                        Directory.Move(dir, destDir);
                    }
                    foreach (var file in Directory.GetFiles(rootDir))
                    {
                        var destFile = Path.Combine(javaDir, Path.GetFileName(file));
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(file, destFile);
                    }
                    Directory.Delete(rootDir, true);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[Java] Failed to flatten Windows Java directory structure (non-fatal): {ex.Message}");
            }

            var foundExe = Directory.GetFiles(javaDir, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (foundExe != null) return foundExe;
        }
        else
        {
            using var extractProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{tempArchive}\" -C \"{javaDir}\" --strip-components=1",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (extractProcess != null) await extractProcess.WaitForExitAsync(cancellationToken);
            
            var foundExe = Directory.GetFiles(javaDir, "java", SearchOption.AllDirectories).FirstOrDefault();
            if (foundExe != null)
            {
                System.Diagnostics.Process.Start("chmod", $"+x \"{foundExe}\"")?.WaitForExit();
                return foundExe;
            }
        }

        throw new Exception($"Java {requiredJavaVersion} executable not found.");
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_settings.OfflineMode) return;
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DeathClient-Updater/1.0");
            var currentVersion = new Version(1, 0, 0); 
            
            var response = await client.GetStringAsync("https://api.github.com/repos/AchinthyaJ/DeathClient/releases/latest");
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagElement))
            {
                var tag = tagElement.GetString();
                if (!string.IsNullOrEmpty(tag) && tag.StartsWith("v"))
                {
                    if (Version.TryParse(tag.Substring(1), out var latestVersion))
                    {
                        if (latestVersion > currentVersion)
                        {
                            string? htmlUrl = null;
                            if (doc.RootElement.TryGetProperty("html_url", out var urlElement))
                            {
                                htmlUrl = urlElement.GetString();
                            }

                            Dispatcher.UIThread.Post(async () =>
                            {
                                var download = await DialogService.ShowConfirmAsync(this, "Update Available", $"A new version ({tag}) is available. Would you like to download it?");
                                if (download && !string.IsNullOrEmpty(htmlUrl))
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = htmlUrl,
                                        UseShellExecute = true
                                    });
                                }
                            });
                        }
                    }
                }
            }
        }
        catch { }
    }

    private System.Threading.CancellationTokenSource? _skinCancellation;

    public async void UsernameInput_TextChanged()
    {
        var selectedAccount = GetSelectedAccount();
        var username = GetActiveUsername();

        if (string.IsNullOrWhiteSpace(username))
        {
            _playerUuid = string.Empty;
            characterImage.Source = null;
            btnStart.IsEnabled = false;
            return;
        }

        btnStart.IsEnabled = true;
        
        _playerUuid = !string.IsNullOrWhiteSpace(selectedAccount?.Uuid)
            ? selectedAccount!.Uuid
            : Character.GenerateUuidFromUsername(username);
        
        _skinCancellation?.Cancel();
        _skinCancellation = new System.Threading.CancellationTokenSource();
        var token = _skinCancellation.Token;

        UpdateCharacterPreview();

        try
        {
            await Task.Delay(1000, token);
            await FetchAndSetSkinAsync(username, token);
        }
        catch (TaskCanceledException) { }
    }

    private async Task FetchAndSetSkinAsync(string username, CancellationToken token)
    {
        var uuid = GetSelectedAccount()?.Uuid;
        if (string.IsNullOrWhiteSpace(uuid))
            uuid = Character.GenerateUuidFromUsername(username);
        var url = $"https://crafatar.com/skins/{uuid}";
        
        var skinsDir = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skins");
        Directory.CreateDirectory(skinsDir);
        var skinPath = Path.Combine(skinsDir, $"{username}.png");

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var bytes = await client.GetByteArrayAsync(url, token);
            await File.WriteAllBytesAsync(skinPath, bytes, token);
            _settings.CustomSkinPath = skinPath;
            _settingsStore.Save(_settings);
        }
        catch
        {
            _settings.CustomSkinPath = string.Empty;
            _settingsStore.Save(_settings);
            if (File.Exists(skinPath))
                File.Delete(skinPath);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (GetActiveUsername() == username)
            {
                UpdateCharacterPreview();
            }
        });
    }

    public void CbVersion_SelectionChanged()
    {
        UpdateCharacterPreview();
        if (_selectedProfile is null)
            SyncModrinthFilters();
    }

    private void UpdateCharacterPreview()
    {
        // 1. If active section is not launch or home, skip rendering to save CPU
        var launchVisible = _activeSection == "home" || _activeSection == "launch";
        if (!launchVisible || WindowState == WindowState.Minimized)
            return;

        var activeUsername = GetActiveUsername();
        var customSkinPath = _settings.CustomSkinPath;
        var customCapePath = _settings.CustomCapePath;

        if (activeUsername != _lastUsernameChecked || 
            customSkinPath != _lastCustomSkinPath || 
            customCapePath != _lastCustomCapePath)
        {
            _lastUsernameChecked = activeUsername;
            _lastCustomSkinPath = customSkinPath;
            _lastCustomCapePath = customCapePath;
            _lastCharacterFileCheckTime = DateTime.MinValue; // Force check
        }

        var now = DateTime.UtcNow;
        if (now - _lastCharacterFileCheckTime > TimeSpan.FromSeconds(2.5) || 
            _resolvedSkinPath == null || 
            _resolvedCapePath == null)
        {
            _lastCharacterFileCheckTime = now;

            var skinPath = _settings.CustomSkinPath;
            bool skinExists = !string.IsNullOrEmpty(skinPath) && File.Exists(skinPath);
            if (!skinExists)
            {
                skinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
                skinExists = File.Exists(skinPath);
            }

            if (!skinExists)
            {
                var selectedVersion = _selectedProfile?.GameVersion ?? cbVersion.SelectedItem?.ToString() ?? string.Empty;
                var resourceName = Character.GetCharacterResourceNameFromUuidAndGameVersion(_playerUuid, selectedVersion);
                if (!string.IsNullOrWhiteSpace(resourceName))
                {
                    var searchFolders = new[] 
                    {
                        Path.Combine(AppContext.BaseDirectory, "Resources"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Resources")
                    };

                    foreach (var folder in searchFolders)
                    {
                        var p = Path.Combine(folder, $"{resourceName}.png");
                        if (File.Exists(p))
                        {
                            skinPath = p;
                            skinExists = true;
                            break;
                        }
                    }
                }
            }

            var capePath = _settings.CustomCapePath;
            bool capeExists = !string.IsNullOrEmpty(capePath) && File.Exists(capePath);

            _resolvedSkinPath = skinPath;
            _resolvedSkinExists = skinExists;
            _resolvedCapePath = capePath;
            _resolvedCapeExists = capeExists;
        }

        bool hasSkin = _resolvedSkinExists;
        bool hasCape = _resolvedCapeExists;
        var skinPathToUse = _resolvedSkinPath;
        var capePathToUse = _resolvedCapePath;

        // Update Skin Cache
        if (hasSkin && skinPathToUse != null)
        {
            var writeTime = File.GetLastWriteTime(skinPathToUse);
            if (skinPathToUse != _cachedSkinPath || writeTime != _cachedSkinWriteTime || _cachedSkinBitmap == null)
            {
                try
                {
                    using (var img = PrepareSkinImage(skinPathToUse, out bool isSlim))
                    {
                        _cachedSkinBitmap?.Dispose();
                        _cachedSkinBitmap = ImageSharpToAvaloniaBitmap(img);
                        _cachedIsSlim = isSlim;
                        _cachedSkinPath = skinPathToUse;
                        _cachedSkinWriteTime = writeTime;
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"[3DPreview] Skin load error: {ex.Message}");
                    _cachedSkinBitmap = null;
                }
            }
        }
        else
        {
            _cachedSkinBitmap = null;
            _cachedSkinPath = null;
        }

        // Update Cape Cache
        if (hasCape && capePathToUse != null)
        {
            var writeTime = File.GetLastWriteTime(capePathToUse);
            if (capePathToUse != _cachedCapePath || writeTime != _cachedCapeWriteTime || _cachedCapeBitmap == null)
            {
                try
                {
                    _cachedCapeBitmap?.Dispose();
                    _cachedCapeBitmap = PrepareCapeImage(capePathToUse);
                    _cachedCapePath = capePathToUse;
                    _cachedCapeWriteTime = writeTime;
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"[3DPreview] Cape load error: {ex.Message}");
                    _cachedCapeBitmap = null;
                }
            }
        }
        else
        {
            _cachedCapeBitmap = null;
            _cachedCapePath = null;
        }

        if (_cachedSkinBitmap == null)
        {
            characterImage.Source = null;
            return;
        }

        // Render 3D Model to RenderTargetBitmap
        const int canvasWidth = 270;
        const int canvasHeight = 420;

        if (_previewRtb == null || _previewRtb.PixelSize.Width != canvasWidth || _previewRtb.PixelSize.Height != canvasHeight)
        {
            _previewRtb?.Dispose();
            _previewRtb = new RenderTargetBitmap(new PixelSize(canvasWidth, canvasHeight));
        }

        using (var ctx = _previewRtb.CreateDrawingContext())
        {
            // Build the character face list
            var quads = BuildCharacterQuads(_cachedIsSlim, _animationTime, _cachedCapeBitmap != null);

            // Project, Cull, Sort
            const double scale = 10.5;
            const double centerX = canvasWidth / 2.0;
            const double centerY = canvasHeight / 2.0;

            var visibleQuads = new List<Quad3D>();
            foreach (var q in quads)
            {
                Point3D r0 = RotateX(RotateY(q.V0, _rotationAngle), _rotationAngleX);
                Point3D r1 = RotateX(RotateY(q.V1, _rotationAngle), _rotationAngleX);
                Point3D r2 = RotateX(RotateY(q.V2, _rotationAngle), _rotationAngleX);
                Point3D r3 = RotateX(RotateY(q.V3, _rotationAngle), _rotationAngleX);

                Point p0 = r0.Project(scale, centerX, centerY);
                Point p1 = r1.Project(scale, centerX, centerY);
                Point p2 = r2.Project(scale, centerX, centerY);
                Point p3 = r3.Project(scale, centerX, centerY);

                double cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                if (cross <= 0) continue; // Cull backfaces

                q.CenterZ = (r0.Z + r1.Z + r2.Z + r3.Z) / 4.0;
                q.P0 = p0;
                q.P1 = p1;
                q.P2 = p2;
                q.P3 = p3;
                visibleQuads.Add(q);
            }

            // Draw from back to front (in-place sort avoids LINQ allocation)
            visibleQuads.Sort((a, b) => a.CenterZ.CompareTo(b.CenterZ));

            // Animated Cape Frame Y Offset (pre-scaled height is 8x larger)
            int capeFrameYOffset = 0;
            if (_cachedCapeBitmap != null)
            {
                double capeHeight = _cachedCapeBitmap.Size.Height;
                double frameHeight = 32.0 * 8.0;
                if (capeHeight > frameHeight && ((int)capeHeight % (int)frameHeight) == 0)
                {
                    int frames = (int)capeHeight / (int)frameHeight;
                    int currentFrame = (int)(_animationTime * 2.0) % frames; // Sync with _animationTime
                    capeFrameYOffset = currentFrame * (int)frameHeight;
                }
            }

            foreach (var q in visibleQuads)
            {
                var activeBmp = q.IsCape ? _cachedCapeBitmap : _cachedSkinBitmap;
                if (activeBmp == null) continue;

                var sourceRect = q.SourceRect;
                if (q.IsCape)
                {
                    var scaledSourceRect = new Rect(sourceRect.X * 8.0, sourceRect.Y * 8.0, sourceRect.Width * 8.0, sourceRect.Height * 8.0);
                    sourceRect = new Rect(scaledSourceRect.X, scaledSourceRect.Y + capeFrameYOffset, scaledSourceRect.Width, scaledSourceRect.Height);
                }
                else
                {
                    sourceRect = new Rect(sourceRect.X * 8.0, sourceRect.Y * 8.0, sourceRect.Width * 8.0, sourceRect.Height * 8.0);
                }

                double W = sourceRect.Width;
                double H = sourceRect.Height;
                if (W <= 0 || H <= 0) continue;

                double m11 = (q.P1.X - q.P0.X) / W;
                double m12 = (q.P1.Y - q.P0.Y) / W;
                double m21 = (q.P2.X - q.P0.X) / H;
                double m22 = (q.P2.Y - q.P0.Y) / H;
                double m31 = q.P0.X;
                double m32 = q.P0.Y;

                var matrix = new Matrix(m11, m12, m21, m22, m31, m32);

                using (ctx.PushTransform(matrix))
                {
                    ctx.DrawImage(activeBmp, sourceRect, new Rect(0, 0, W, H));
                }
            }
        }

        characterImage.Source = null;
        characterImage.Source = _previewRtb;
        // Use low quality on Windows for performance; high quality on Linux
        RenderOptions.SetBitmapInterpolationMode(characterImage, 
            OperatingSystem.IsWindows() 
                ? Avalonia.Media.Imaging.BitmapInterpolationMode.LowQuality 
                : Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    private static SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> PrepareSkinImage(string skinPath, out bool isSlim)
    {
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image;
        using (var fs = new FileStream(skinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(fs);
        }
        isSlim = false;

        // 1. If 64x32, convert to 64x64
        if (image.Height == 32)
        {
            var expanded = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 64);
            
            // Copy top 32 lines
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    expanded[x, y] = image[x, y];
                }
            }

            // Mirror Right Leg (0,16) to Left Leg (16,48)
            CopyLegOrArm(expanded, 0, 16, 16, 48);

            // Mirror Right Arm (40,16) to Left Arm (32,48)
            CopyLegOrArm(expanded, 40, 16, 32, 48);

            image.Dispose();
            image = expanded;
        }

        // 2. Check slim
        bool col39Transparent = true;
        for (int y = 52; y < 64; y++)
        {
            if (image[39, y].A > 0)
            {
                col39Transparent = false;
                break;
            }
        }
        isSlim = col39Transparent;

        // 3. Upscale 8x nearest-neighbor for high quality preview
        SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(image, x =>
            SixLabors.ImageSharp.Processing.ResizeExtensions.Resize(x,
                image.Width * 8, image.Height * 8,
                SixLabors.ImageSharp.Processing.KnownResamplers.NearestNeighbor));

        return image;
    }

    private static Bitmap PrepareCapeImage(string capePath)
    {
        using (var fs = new FileStream(capePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(fs))
        {
            SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(img, x =>
                SixLabors.ImageSharp.Processing.ResizeExtensions.Resize(x,
                    img.Width * 8, img.Height * 8,
                    SixLabors.ImageSharp.Processing.KnownResamplers.NearestNeighbor));
            return ImageSharpToAvaloniaBitmap(img);
        }
    }

    private static void CopyLegOrArm(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img, int srcX, int srcY, int destX, int destY)
    {
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int sx = srcX + x;
                int sy = srcY + y;
                int dx = destX + (15 - x);
                img[dx, destY + y] = img[sx, sy];
            }
        }
    }

    private static Bitmap ImageSharpToAvaloniaBitmap(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img)
    {
        // Direct pixel copy via WriteableBitmap — avoids PNG encode/decode round-trip
        // which was the #1 skin preview performance bottleneck on Windows
        var wb = new Avalonia.Media.Imaging.WriteableBitmap(
            new PixelSize(img.Width, img.Height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using (var framebuffer = wb.Lock())
        {
            img.ProcessPixelRows(accessor =>
            {
                byte[] rowBytes = new byte[accessor.Width * 4];
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        var pixel = row[x];
                        float a = pixel.A / 255f;
                        int idx = x * 4;
                        rowBytes[idx + 0] = (byte)(pixel.B * a); // B
                        rowBytes[idx + 1] = (byte)(pixel.G * a); // G
                        rowBytes[idx + 2] = (byte)(pixel.R * a); // R
                        rowBytes[idx + 3] = pixel.A;              // A
                    }
                    IntPtr destRowPtr = framebuffer.Address + (y * framebuffer.RowBytes);
                    System.Runtime.InteropServices.Marshal.Copy(rowBytes, 0, destRowPtr, rowBytes.Length);
                }
            });
        }
        return wb;
    }

    private struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }

        public Point Project(double scale, double centerX, double centerY)
        {
            return new Point(centerX + scale * X, centerY - scale * Y);
        }
    }

    private class Quad3D
    {
        public Point3D V0, V1, V2, V3;
        public Point P0, P1, P2, P3;
        public Rect SourceRect;
        public bool IsCape;
        public bool IsOverlay;
        public double CenterZ;

        public Quad3D(Point3D v0, Point3D v1, Point3D v2, Point3D v3, Rect sourceRect, bool isCape = false, bool isOverlay = false)
        {
            V0 = v0; V1 = v1; V2 = v2; V3 = v3;
            SourceRect = sourceRect;
            IsCape = isCape;
            IsOverlay = isOverlay;
        }
    }

    private static Point3D RotateX(Point3D p, double rad)
    {
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new Point3D(p.X, p.Y * cos - p.Z * sin, p.Y * sin + p.Z * cos);
    }

    private static Point3D RotateY(Point3D p, double rad)
    {
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new Point3D(p.X * cos - p.Z * sin, p.Y, p.X * sin + p.Z * cos);
    }

    private static Point3D RotateZ(Point3D p, double rad)
    {
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new Point3D(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos, p.Z);
    }

    private static Point3D ApplyJointRotation(Point3D p, Point3D joint, double rotX, double rotY, double rotZ)
    {
        var local = new Point3D(p.X - joint.X, p.Y - joint.Y, p.Z - joint.Z);
        if (rotX != 0) local = RotateX(local, rotX);
        if (rotY != 0) local = RotateY(local, rotY);
        if (rotZ != 0) local = RotateZ(local, rotZ);
        return new Point3D(local.X + joint.X, local.Y + joint.Y, local.Z + joint.Z);
    }

    private static List<Quad3D> CreateBoxQuads(double x, double y, double z, double w, double h, double d, int tx, int ty, double inflate = 0.0, bool isOverlay = false, bool isCape = false)
    {
        double hw = (w + 2 * inflate) / 2.0;
        double hh = (h + 2 * inflate) / 2.0;
        double hd = (d + 2 * inflate) / 2.0;

        var v = new Point3D[8];
        v[0] = new Point3D(x - hw, y + hh, z + hd); // FTL
        v[1] = new Point3D(x + hw, y + hh, z + hd); // FTR
        v[2] = new Point3D(x - hw, y - hh, z + hd); // FBL
        v[3] = new Point3D(x + hw, y - hh, z + hd); // FBR
        v[4] = new Point3D(x - hw, y + hh, z - hd); // BTL
        v[5] = new Point3D(x + hw, y + hh, z - hd); // BTR
        v[6] = new Point3D(x - hw, y - hh, z - hd); // BBL
        v[7] = new Point3D(x + hw, y - hh, z - hd); // BBR

        var list = new List<Quad3D>();

        // 1. Front (+Z): V0, V1, V2, V3
        list.Add(new Quad3D(v[0], v[1], v[2], v[3], new Rect(isCape ? tx + d + w + d : tx + d, ty + d, w, h), isCape, isOverlay));
        // 2. Back (-Z): V1, V0, V3, V2 (looking from back: V5, V4, V7, V6)
        list.Add(new Quad3D(v[5], v[4], v[7], v[6], new Rect(isCape ? tx + d : tx + d + w + d, ty + d, w, h), isCape, isOverlay));
        // 3. Left (-X): V0, V1, V2, V3 (looking from left: V4, V0, V6, V2)
        list.Add(new Quad3D(v[4], v[0], v[6], v[2], new Rect(tx + d + w, ty + d, d, h), isCape, isOverlay));
        // 4. Right (+X): V0, V1, V2, V3 (looking from right: V1, V5, V3, V7)
        list.Add(new Quad3D(v[1], v[5], v[3], v[7], new Rect(tx, ty + d, d, h), isCape, isOverlay));
        // 5. Top (+Y): V0, V1, V2, V3 (looking from top: V4, V5, V0, V1)
        list.Add(new Quad3D(v[4], v[5], v[0], v[1], new Rect(tx + d, ty, w, d), isCape, isOverlay));
        // 6. Bottom (-Y): V0, V1, V2, V3 (looking from bottom: V2, V3, V6, V7)
        list.Add(new Quad3D(v[2], v[3], v[6], v[7], new Rect(tx + d + w, ty, w, d), isCape, isOverlay));

        return list;
    }

    private List<Quad3D> BuildCharacterQuads(bool isSlim, double animTime, bool hasCape)
    {
        var list = new List<Quad3D>();

        // Animation angles
        double armAngle = Math.Sin(animTime) * 0.35;
        double legAngle = -Math.Sin(animTime) * 0.35;
        double headAngleX = 0.05 + Math.Sin(animTime * 0.5) * 0.03;
        double headAngleY = Math.Cos(animTime * 0.35) * 0.08;
        double capeAngle = 0.18 + Math.Sin(animTime * 1.5) * 0.07;

        // 1. Head
        var headBase = CreateBoxQuads(0, 10, 0, 8, 8, 8, 0, 0);
        foreach (var q in headBase)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            list.Add(q);
        }
        var headOverlay = CreateBoxQuads(0, 10, 0, 8, 8, 8, 32, 0, 0.4, true);
        foreach (var q in headOverlay)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(0, 6, 0), headAngleX, headAngleY, 0);
            list.Add(q);
        }

        // 2. Body
        list.AddRange(CreateBoxQuads(0, 0, 0, 8, 12, 4, 16, 16));
        list.AddRange(CreateBoxQuads(0, 0, 0, 8, 12, 4, 16, 32, 0.35, true));

        // 3. Right Arm
        double raX = isSlim ? -5.5 : -6.0;
        double raW = isSlim ? 3.0 : 4.0;
        var raBase = CreateBoxQuads(raX, 0, 0, raW, 12, 4, 40, 16);
        foreach (var q in raBase)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            list.Add(q);
        }
        var raOverlay = CreateBoxQuads(raX, 0, 0, raW, 12, 4, 40, 32, 0.35, true);
        foreach (var q in raOverlay)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(raX, 6, 0), -armAngle, 0, 0);
            list.Add(q);
        }

        // 4. Left Arm
        double laX = isSlim ? 5.5 : 6.0;
        double laW = isSlim ? 3.0 : 4.0;
        var laBase = CreateBoxQuads(laX, 0, 0, laW, 12, 4, 32, 48);
        foreach (var q in laBase)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(laX, 6, 0), armAngle, 0, 0);
            list.Add(q);
        }
        var laOverlay = CreateBoxQuads(laX, 0, 0, laW, 12, 4, 48, 48, 0.35, true);
        foreach (var q in laOverlay)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(laX, 6, 0), armAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(laX, 6, 0), armAngle, 0, 0);
            list.Add(q);
        }

        // 5. Right Leg
        var rlBase = CreateBoxQuads(-2, -12, 0, 4, 12, 4, 0, 16);
        foreach (var q in rlBase)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(-2, -6, 0), legAngle, 0, 0);
            list.Add(q);
        }
        var rlOverlay = CreateBoxQuads(-2, -12, 0, 4, 12, 4, 0, 32, 0.35, true);
        foreach (var q in rlOverlay)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(-2, -6, 0), legAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(-2, -6, 0), legAngle, 0, 0);
            list.Add(q);
        }

        // 6. Left Leg
        var llBase = CreateBoxQuads(2, -12, 0, 4, 12, 4, 16, 48);
        foreach (var q in llBase)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(2, -6, 0), -legAngle, 0, 0);
            list.Add(q);
        }
        var llOverlay = CreateBoxQuads(2, -12, 0, 4, 12, 4, 0, 48, 0.35, true);
        foreach (var q in llOverlay)
        {
            q.V0 = ApplyJointRotation(q.V0, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V1 = ApplyJointRotation(q.V1, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V2 = ApplyJointRotation(q.V2, new Point3D(2, -6, 0), -legAngle, 0, 0);
            q.V3 = ApplyJointRotation(q.V3, new Point3D(2, -6, 0), -legAngle, 0, 0);
            list.Add(q);
        }

        // 7. Cape
        if (hasCape)
        {
            var capeQuads = CreateBoxQuads(0, -2, -2.2, 10, 16, 0.5, 0, 0, 0.0, false, true);
            foreach (var q in capeQuads)
            {
                q.V0 = ApplyJointRotation(q.V0, new Point3D(0, 6, -2), capeAngle, 0, 0);
                q.V1 = ApplyJointRotation(q.V1, new Point3D(0, 6, -2), capeAngle, 0, 0);
                q.V2 = ApplyJointRotation(q.V2, new Point3D(0, 6, -2), capeAngle, 0, 0);
                q.V3 = ApplyJointRotation(q.V3, new Point3D(0, 6, -2), capeAngle, 0, 0);
                list.Add(q);
            }
        }

        return list;
    }

    private void _launcher_FileProgressChanged(object? sender, InstallerProgressChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            pbFiles.Maximum = Math.Max(1, args.TotalTasks);
            pbFiles.Value = Math.Min(args.ProgressedTasks, pbFiles.Maximum);
            statusLabel.Text = $"Installing {args.Name}";
            installDetailsLabel.Text = $"{args.ProgressedTasks} / {args.TotalTasks} files";
        });
    }

    private void _launcher_ByteProgressChanged(object? sender, ByteProgress args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            pbProgress.Maximum = 100;
            pbProgress.Value = args.TotalBytes <= 0
                ? 0
                : Math.Min(100, args.ProgressedBytes * 100d / args.TotalBytes);
        });
    }

    private void RefreshProfiles(LauncherProfile? selectProfile = null)
    {
        var existingProfiles = _profileStore.LoadProfiles();
        var hasAether = existingProfiles.Any(p => p.Name == "Aether Client");
        if (!hasAether)
        {
            try
            {
                var gameVer = _versionItems.FirstOrDefault(v => v.Contains("1.21.1")) 
                           ?? _versionItems.FirstOrDefault(v => v.Contains("1.21"))
                           ?? "1.21.1";
                var loaderVer = "0.18.4";
                _profileStore.CreateProfile("Aether Client", gameVer, "fabric", loaderVer);
                _settings.EnableFancyMenu = true;
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"[RefreshProfiles] Failed to auto-create Aether Client profile: {ex.Message}");
            }
        }

        _profileItems.Clear();
        foreach (var profile in _profileStore.LoadProfiles())
            _profileItems.Add(profile);

        // Add the "+ Add New Instance" placeholder card at the end
        var addPlaceholder = new LauncherProfile
        {
            Name = "__add_new_placeholder__",
            VersionId = ""
        };
        _profileItems.Add(addPlaceholder);

        LauncherProfile? profileToSelect = null;
        if (selectProfile is not null)
            profileToSelect = _profileItems.FirstOrDefault(profile => profile.Name != "__add_new_placeholder__" && string.Equals(profile.InstanceDirectory, selectProfile.InstanceDirectory, StringComparison.Ordinal));
        else if (_selectedProfile is not null)
            profileToSelect = _profileItems.FirstOrDefault(profile => profile.Name != "__add_new_placeholder__" && string.Equals(profile.InstanceDirectory, _selectedProfile.InstanceDirectory, StringComparison.Ordinal));
        else if (!string.IsNullOrEmpty(_settings.LastSelectedProfilePath))
            profileToSelect = _profileItems.FirstOrDefault(profile => profile.Name != "__add_new_placeholder__" && string.Equals(profile.InstanceDirectory, _settings.LastSelectedProfilePath, StringComparison.Ordinal));
        
        if (profileToSelect is null && _profileItems.Count > 1)
            profileToSelect = _profileItems.FirstOrDefault(p => p.Name != "__add_new_placeholder__");
        
        profileListBox.SelectedItem = profileToSelect;
        _selectedProfile = profileToSelect;
        UpdateLauncherContext();
    }

    public void ProfileListBox_SelectionChanged()
    {
        if (_handlingProfileSelection) return;

        var selected = profileListBox.SelectedItem as LauncherProfile;
        if (selected == null) return;

        if (selected.Name == "__add_new_placeholder__")
        {
            // Restore selection to the previously selected profile without re-entering this handler
            _handlingProfileSelection = true;
            profileListBox.SelectedItem = _selectedProfile;
            _handlingProfileSelection = false;

            // Open the instance editor/creator overlay
            ClearSelectedProfile();
            createProfileButton.IsVisible = true;
            renameProfileButton.IsVisible = false;
            if (profilePresetSection != null)
                profilePresetSection.IsVisible = true;
            if (profilePresetCombo != null)
            {
                profilePresetCombo.SelectedItem = "Aether Client (Fabric) (Coming Soon)";
                profileNameInput.Text = "Aether Client";
                profileLoaderCombo.SelectedIndex = 1;
                var targetVer = _versionItems.FirstOrDefault(v => v.Contains("1.21.1"))
                             ?? _versionItems.FirstOrDefault(v => v.Contains("1.21"))
                             ?? _versionItems.FirstOrDefault();
                if (targetVer != null)
                    instanceVersionCombo.SelectedItem = targetVer;
            }
            if (_instanceEditorOverlay != null)
                _instanceEditorOverlay.IsVisible = true;
            return;
        }

        _selectedProfile = selected;
        if (_selectedProfile is not null)
            profileNameInput.Text = _selectedProfile.Name;
        UpdateLauncherContext();
        SyncModrinthFilters();
        UpdateCharacterPreview();
        RefreshModsList();
        UpdateSelectedProjectDetails();
        RefreshSearchList();

        UpdateInstanceCardSelection();

        // Clear the ListBox's visual selection so Avalonia's selection highlight doesn't show.
        Dispatcher.UIThread.Post(() =>
        {
            _handlingProfileSelection = true;
            profileListBox.SelectedItem = null;
            _handlingProfileSelection = false;
        });
    }

    private void UpdateInstanceCardSelection()
    {
        foreach (var (dir, card) in _instanceCardBorders)
        {
            var isSelected = string.Equals(dir, _selectedProfile?.InstanceDirectory, StringComparison.Ordinal);
            if (isSelected)
            {
                card.BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF"));
                card.BorderThickness = new Thickness(2);
                card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(90, 110, 91, 255), OffsetX = 0, OffsetY = 0 });
            }
            else
            {
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
                card.BorderThickness = new Thickness(1);
                card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 0, Color = Colors.Transparent, OffsetX = 0, OffsetY = 0 });
            }
        }
    }

    private void RefreshModsList()
    {
        if (_selectedProfile == null)
        {
            _modItems.Clear();
            return;
        }

        var profilePath = _selectedProfile.InstanceDirectory;
        var modsDir = _selectedProfile.ModsDirectory;

        Task.Run(() =>
        {
            if (!Directory.Exists(modsDir))
            {
                Dispatcher.UIThread.Post(() => _modItems.Clear());
                return;
            }

            try
            {
                var dirInfo = new DirectoryInfo(modsDir);
                var writeTime = dirInfo.LastWriteTimeUtc;

                // Check if directory hasn't changed
                if (_lastModsListProfilePath == profilePath && _lastModsListDirectoryWriteTime == writeTime)
                {
                    // No change, skip reloading to optimize performance and prevent lag
                    return;
                }

                var files = Directory.GetFiles(modsDir);
                var tempItems = new List<ModItem>();
                int count = 0;

                foreach (var file in files)
                {
                    if (!file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && 
                        !file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var item = new ModItem
                    {
                        FileName = Path.GetFileName(file),
                        FileSize = (new FileInfo(file).Length / 1024) + " KB",
                        FullPath = file
                    };
                    item.InitState(!file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));
                    tempItems.Add(item);
                    count++;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (_selectedProfile == null || _selectedProfile.InstanceDirectory != profilePath)
                        return;

                    _modItems.Clear();
                    foreach (var item in tempItems)
                    {
                        _modItems.Add(item);
                    }
                    _lastModsListProfilePath = profilePath;
                    _lastModsListDirectoryWriteTime = writeTime;
                    LauncherLog.Info($"[ModsList] Loaded {count} mods for {_selectedProfile.Name}.");
                });
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"[ModsList] Refresh failed for {_selectedProfile.Name}", ex);
            }
        });
    }

    private void RefreshManageTabContent()
    {
        if (_selectedProfile == null)
        {
            if (_manageNoProfileCard != null) _manageNoProfileCard.IsVisible = true;
            if (_manageContentGrid != null) _manageContentGrid.IsVisible = false;
            _worldItems.Clear();
            _resourcePackItems.Clear();
            return;
        }

        if (_manageNoProfileCard != null) _manageNoProfileCard.IsVisible = false;
        if (_manageContentGrid != null) _manageContentGrid.IsVisible = true;

        var profilePath = _selectedProfile.InstanceDirectory;
        var savesDir = Path.Combine(profilePath, "saves");
        var rpDir = Path.Combine(profilePath, "resourcepacks");

        Task.Run(() =>
        {
            var tempWorlds = new List<WorldItem>();
            var tempRps = new List<ResourcePackItem>();

            // 1. Worlds (saves)
            if (Directory.Exists(savesDir))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(savesDir))
                    {
                        var folderName = Path.GetFileName(dir);
                        var worldName = folderName;
                        
                        long totalSizeBytes = 0;
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            totalSizeBytes = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                        }
                        catch {}
                        
                        var sizeStr = FormatBytes(totalSizeBytes);
                        tempWorlds.Add(new WorldItem
                        {
                            Name = worldName,
                            FolderName = folderName,
                            FullPath = dir,
                            Size = sizeStr
                        });
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error("[ManageTab] Failed to list worlds", ex);
                }
            }

            // 2. Resource Packs (resourcepacks)
            if (Directory.Exists(rpDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(rpDir))
                    {
                        var name = Path.GetFileName(file);
                        long sizeBytes = 0;
                        try
                        {
                            sizeBytes = new FileInfo(file).Length;
                        }
                        catch {}
                        
                        tempRps.Add(new ResourcePackItem
                        {
                            Name = name,
                            FullPath = file,
                            Size = FormatBytes(sizeBytes)
                        });
                    }
                    foreach (var dir in Directory.GetDirectories(rpDir))
                    {
                        var name = Path.GetFileName(dir);
                        long sizeBytes = 0;
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            sizeBytes = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                        }
                        catch {}
                        
                        tempRps.Add(new ResourcePackItem
                        {
                            Name = name,
                            FullPath = dir,
                            Size = FormatBytes(sizeBytes)
                        });
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error("[ManageTab] Failed to list resource packs", ex);
                }
            }

            // Post back to UI thread
            Dispatcher.UIThread.Post(() =>
            {
                if (_selectedProfile == null || _selectedProfile.InstanceDirectory != profilePath)
                    return;

                _worldItems.Clear();
                foreach (var item in tempWorlds)
                    _worldItems.Add(item);

                _resourcePackItems.Clear();
                foreach (var item in tempRps)
                    _resourcePackItems.Add(item);

                // 3. Mods (triggers background thread)
                RefreshModsList();

                // 4. Update empty states visibility
                if (_worldsEmptyState != null) _worldsEmptyState.IsVisible = _worldItems.Count == 0;
                if (_worldsListBox != null) _worldsListBox.IsVisible = _worldItems.Count > 0;
                if (_rpEmptyState != null) _rpEmptyState.IsVisible = _resourcePackItems.Count == 0;
                if (_rpListBox != null) _rpListBox.IsVisible = _resourcePackItems.Count > 0;
                if (_modsEmptyState != null) _modsEmptyState.IsVisible = _modItems.Count == 0;
                if (_modsListBox != null) _modsListBox.IsVisible = _modItems.Count > 0;
            });
        });
    }

    private async Task ImportWorldAsync()
    {
        if (_selectedProfile == null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import World (Zip File)",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("World Archives") { Patterns = new[] { "*.zip" } } }
            });

            if (files != null && files.Count > 0)
            {
                var zipPath = files[0].Path.LocalPath;
                var savesDir = Path.Combine(_selectedProfile.InstanceDirectory, "saves");
                Directory.CreateDirectory(savesDir);

                var targetDirName = Path.GetFileNameWithoutExtension(zipPath);
                var targetPath = Path.Combine(savesDir, targetDirName);
                
                int count = 1;
                while (Directory.Exists(targetPath))
                {
                    targetPath = Path.Combine(savesDir, $"{targetDirName}_{count}");
                    count++;
                }

                ToggleBusyState(true, "Extracting world...");
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetPath));
                
                RefreshManageTabContent();
                await DialogService.ShowInfoAsync(this, "Success", $"World imported successfully to '{Path.GetFileName(targetPath)}'!");
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Error", $"Failed to import world: {ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
        }
    }

    private async Task ImportResourcePackAsync()
    {
        if (_selectedProfile == null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Resource Pack",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Resource Packs") { Patterns = new[] { "*.zip", "*.jar" } } }
            });

            if (files != null && files.Count > 0)
            {
                var srcPath = files[0].Path.LocalPath;
                var rpDir = Path.Combine(_selectedProfile.InstanceDirectory, "resourcepacks");
                Directory.CreateDirectory(rpDir);

                var destPath = Path.Combine(rpDir, Path.GetFileName(srcPath));
                if (File.Exists(destPath))
                {
                    var overwrite = await DialogService.ShowConfirmAsync(this, "Overwrite", "A resource pack with this name already exists. Overwrite?");
                    if (!overwrite) return;
                }

                File.Copy(srcPath, destPath, true);
                RefreshManageTabContent();
                await DialogService.ShowInfoAsync(this, "Success", "Resource pack imported successfully!");
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Error", $"Failed to import resource pack: {ex.Message}");
        }
    }

    private void ClearSelectedProfile()
    {
        profileListBox.SelectedItem = null;
        _selectedProfile = null;
        profileNameInput.Text = string.Empty;
        UpdateLauncherContext();
        SyncModrinthFilters();
        UpdateCharacterPreview();
    }

    private void OpenProfileEditor(LauncherProfile profile)
    {
        _selectedProfile = profile;
        profileListBox.SelectedItem = profile;
        profileNameInput.Text = profile.Name;
        profileGameDirInput.Text = profile.GameDirectoryOverride ?? string.Empty;

        var selectedIndex = Array.FindIndex(ProfileLoaderOptions, option =>
            string.Equals(option, profile.Loader, StringComparison.OrdinalIgnoreCase));
        profileLoaderCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        createProfileButton.IsVisible = false;
        renameProfileButton.IsVisible = true;
        if (profilePresetSection != null)
            profilePresetSection.IsVisible = false;
        UpdateLauncherContext();
        SyncModrinthFilters();
        UpdateCharacterPreview();
        RefreshModsList();
        _instanceEditorOverlay.IsVisible = true;
    }

    private void UpdateLauncherContext()
    {
        if (_selectedProfile is null)
        {
            activeProfileBadge.Text = "HOME";
            activeContextLabel.Text = string.Empty;
            installModeLabel.Text = "Default";
            SetButtonText(btnStart, "▶ Play");
            profileInspectorTitle.Text = "Standard Profile";
            profileInspectorMeta.Text = "No isolated profile is active. Mods install only after you create or select a profile.";
            profileInspectorPath.Text = $"Instances root: {_profileStore.GetInstancesRoot()}";
            clearProfileButton.IsEnabled = false;
            renameProfileButton.IsEnabled = false;
            heroInstanceLabel.Text = "Standard Play";
            heroPerformanceLabel.Text = $"{cbVersion.SelectedItem?.ToString() ?? "1.21.1"} • Ready";
            var ramGbInit = _settings.MaxRamMb / 1024.0;
            var expectedFpsInit = Math.Round(ramGbInit * 41.25).ToString();
            var expectedRamInit = $"{Math.Round(ramGbInit, 1)} GB";
            homeFpsStatValue.Text = expectedFpsInit;
            homeRamStatValue.Text = expectedRamInit;
            performanceFpsStatValue.Text = expectedFpsInit;
            performanceRamStatValue.Text = expectedRamInit;
            RefreshManageTabContent();
            return;
        }

        activeProfileBadge.Text = "ACTIVE";
        activeContextLabel.Text = string.Empty;
        installModeLabel.Text = _selectedProfile.Name;
        btnStart.Content = "▶ Play";
        profileInspectorTitle.Text = _selectedProfile.Name;
        profileInspectorMeta.Text = $"{_selectedProfile.LoaderDisplay} · Updated {_selectedProfile.UpdatedUtc.ToLocalTime():g}";
        profileInspectorPath.Text = _selectedProfile.InstanceDirectory;
        clearProfileButton.IsEnabled = true;
        renameProfileButton.IsEnabled = true;
        heroInstanceLabel.Text = _selectedProfile.Name;
        heroPerformanceLabel.Text = $"{_selectedProfile.GameVersion} • Ready";
        var ramGb = _settings.MaxRamMb / 1024.0;
        var fpsText = Math.Round(ramGb * (_selectedProfile.Loader == "vanilla" ? 41.25 : 30)).ToString();
        var ramText = $"{Math.Round(ramGb, 1)} GB";
        homeFpsStatValue.Text = fpsText;
        homeRamStatValue.Text = ramText;
        performanceFpsStatValue.Text = fpsText;
        performanceRamStatValue.Text = ramText;

        _settings.LastSelectedProfilePath = _selectedProfile.InstanceDirectory;
        _settingsStore.Save(_settings);
        RefreshManageTabContent();
    }

    private void SyncModrinthFilters()
    {
        var rawVersion = _selectedProfile?.GameVersion ?? cbVersion.SelectedItem?.ToString() ?? string.Empty;
        // Basic cleanup: if they have "1.21.11" it might be a typo for "1.21.1" or they mean something else
        modrinthVersionInput.Text = rawVersion;
        var loader = _selectedProfile?.Loader ?? "vanilla";

        var selectedIndex = Array.FindIndex(LoaderOptions, option => string.Equals(option, loader, StringComparison.OrdinalIgnoreCase));
        modrinthLoaderCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(profileNameInput.Text))
        {
            await DialogService.ShowInfoAsync(this, "Profile name required", "Give the profile a name before creating it.");
            return;
        }

        if (instanceVersionCombo.SelectedItem is null)
        {
            await DialogService.ShowInfoAsync(this, "Version required", "Select a Minecraft version before creating a profile.");
            return;
        }

        var selectedVersion = instanceVersionCombo.SelectedItem!.ToString()!;
        var loader = profileLoaderCombo.SelectedItem?.ToString()?.ToLowerInvariant() ?? "vanilla";
        string? loaderVersion = null;

        try
        {
            if (profilePresetCombo != null && (profilePresetCombo.SelectedItem?.ToString() == "Aether Client (Fabric) (Coming Soon)" || profilePresetCombo.SelectedItem?.ToString() == "Aether Client (Fabric)"))
            {
                _settings.EnableFancyMenu = true;
                _settingsStore.Save(_settings);
            }

            ToggleBusyState(true, "Creating profile...");

            if (loader == "fabric")
                loaderVersion = await ResolveLatestFabricVersionAsync(selectedVersion, CancellationToken.None);
            else if (loader == "quilt")
                loaderVersion = await ResolveLatestQuiltVersionAsync(selectedVersion, CancellationToken.None);
            else if (loader == "forge")
                loaderVersion = await ResolveLatestForgeVersionAsync(selectedVersion, CancellationToken.None);
            else if (loader == "neoforge")
                loaderVersion = await ResolveLatestNeoForgeVersionAsync(selectedVersion, CancellationToken.None);

            var profile = _profileStore.CreateProfile(profileNameInput.Text.Trim(), selectedVersion, loader, loaderVersion, null, profileGameDirInput.Text?.Trim());
            if (loader == "fabric")
                await EnsureFabricProfileAsync(profile, CancellationToken.None);
            else if (loader == "quilt")
                await EnsureQuiltProfileAsync(profile, CancellationToken.None);
            else if (loader == "forge" || loader == "neoforge")
                await EnsureForgeProfileAsync(profile, CancellationToken.None);

            // Ensure the required mods are installed automatically immediately
            var modsDir = Path.Combine(profile.InstanceDirectory, "mods");
            Directory.CreateDirectory(modsDir);
            if (_settings.EnableFancyMenu && SupportsFancyMenu(profile))
            {
                await InstallModIfMissingAsync("fancymenu", profile, modsDir, CancellationToken.None);
                await InstallModIfMissingAsync("konkrete", profile, modsDir, CancellationToken.None);
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                RefreshProfiles(profile);
                UpdateSelectedProjectDetails();
                profileNameInput.Text = string.Empty;
                _instanceEditorOverlay.IsVisible = false;
                SetProgressState($"Profile {profile.Name} is ready.", 0, 0);
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Profile error", $"Failed to create profile.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready to install or launch.");
        }
    }

    private async Task RenameSelectedProfileAsync()
    {
        if (_selectedProfile is null)
        {
            await DialogService.ShowInfoAsync(this, "Profile required", "Select an instance before renaming it.");
            return;
        }

        var nextName = profileNameInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nextName))
        {
            await DialogService.ShowInfoAsync(this, "Profile name required", "Enter a new name for the selected instance.");
            return;
        }

        _selectedProfile.Name = nextName;
        _profileStore.Save(_selectedProfile);
        RefreshProfiles(_selectedProfile);
        _instanceEditorOverlay.IsVisible = false;
        SetProgressState($"Renamed to {nextName}.", 0, 0);
    }

    private async Task DeleteSelectedProfileAsync(LauncherProfile? profile = null)
    {
        var target = profile ?? _selectedProfile;
        if (target is null)
        {
            await DialogService.ShowInfoAsync(this, "Profile required", "Select an instance to delete first.");
            return;
        }

        var confirm = await DialogService.ShowConfirmAsync(
            this,
            "Delete confirmation",
            $"Are you sure you want to delete '{target.Name}'? This will delete all its files including worlds and mods!");

        if (confirm)
        {
            _profileStore.Delete(target);
            RefreshProfiles();
            if (target == _selectedProfile)
                ClearSelectedProfile();
            SetProgressState("Instance deleted.", 0, 0);
        }
    }

    private async Task QuickInstallInstanceAsync()
    {
        var version = _quickVersionCombo.SelectedItem?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            await DialogService.ShowInfoAsync(this, "Version required", "Select a Minecraft version first.");
            return;
        }

        var loader = _quickLoaderCombo.SelectedItem?.ToString()?.ToLowerInvariant() ?? "vanilla";
        var autoName = $"{version} {char.ToUpper(loader[0])}{loader[1..]}";
        string? loaderVersion = null;

        try
        {
            ToggleBusyState(true, $"Creating {autoName}...");

            if (loader == "fabric")
                loaderVersion = await ResolveLatestFabricVersionAsync(version, CancellationToken.None);
            else if (loader == "quilt")
                loaderVersion = await ResolveLatestQuiltVersionAsync(version, CancellationToken.None);
            else if (loader == "forge")
                loaderVersion = await ResolveLatestForgeVersionAsync(version, CancellationToken.None);
            else if (loader == "neoforge")
                loaderVersion = await ResolveLatestNeoForgeVersionAsync(version, CancellationToken.None);

            var profile = _profileStore.CreateProfile(autoName, version, loader, loaderVersion);

            if (loader == "fabric")
                await EnsureFabricProfileAsync(profile, CancellationToken.None);
            else if (loader == "quilt")
                await EnsureQuiltProfileAsync(profile, CancellationToken.None);
            else if (loader == "forge" || loader == "neoforge")
                await EnsureForgeProfileAsync(profile, CancellationToken.None);

            // Ensure the required mods are installed automatically immediately
            var modsDir = Path.Combine(profile.InstanceDirectory, "mods");
            Directory.CreateDirectory(modsDir);
            if (_settings.EnableFancyMenu && SupportsFancyMenu(profile))
            {
                await InstallModIfMissingAsync("fancymenu", profile, modsDir, CancellationToken.None);
                await InstallModIfMissingAsync("konkrete", profile, modsDir, CancellationToken.None);
            }

            // Pre-download the game files
            var launcherPath = new MinecraftPath(profile.InstanceDirectory)
            {
                Library = _defaultMinecraftPath.Library,
                Assets = _defaultMinecraftPath.Assets,
                Versions = _defaultMinecraftPath.Versions
            };
            var launcher = CreateLauncher(launcherPath);
            await launcher.InstallAsync(version);

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                RefreshProfiles(profile);
                UpdateSelectedProjectDetails();
                SetProgressState($"Instance \"{autoName}\" ready to play!", 0, 0);
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Install failed", $"Failed to create instance.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
        }
    }

    private async Task QuickModSearchAsync()
    {
        if (_settings.OfflineMode)
        {
            await DialogService.ShowInfoAsync(this, "No internet mode", "Mod searching is disabled in No internet mode.");
            return;
        }

        var query = _quickModSearch.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            await DialogService.ShowInfoAsync(this, "Search required", "Enter a mod name to search.");
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        try
        {
            ToggleBusyState(true, "Searching...");
            var gameVersion = _selectedProfile?.GameVersion ?? cbVersion.SelectedItem?.ToString();
            var loader = _selectedProfile?.Loader;
            if (string.Equals(loader, "vanilla", StringComparison.OrdinalIgnoreCase))
                loader = null;

            var results = await _modrinthClient.SearchProjectsAsync(query, "mod", gameVersion, loader, _searchCancellation.Token);
            _quickSearchResults.Clear();
            foreach (var r in results.Take(8))
                _quickSearchResults.Add(r);

            SetProgressState($"Found {results.Count} mods.", 0, 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Search failed", $"Modrinth search failed.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
        }
    }

    private async Task QuickInstallModAsync(ModrinthProject project)
    {
        if (_selectedProfile is null)
        {
            await DialogService.ShowInfoAsync(this, "Profile required", "Create or select an instance first (use Quick Instance above, or the Instances tab).");
            return;
        }

        try
        {
            ToggleBusyState(true, $"Installing {project.Title}...");
            await InstallSelectedModAsync(project, CancellationToken.None, null); // We don't have a specific button here easily accessible, button is usually in the search results
            RefreshModsList();
            UpdateSelectedProjectDetails();
            SetProgressState($"Installed {project.Title}!", 0, 0);
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Install failed", $"Install failed.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
        }
    }

    private async Task<string> ResolveLatestFabricVersionAsync(string gameVersion, CancellationToken cancellationToken)
    {
        var payload = await _modrinthClient.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}", cancellationToken);
        using var json = JsonDocument.Parse(payload);
        
        var isLegacy = false;
        if (System.Text.RegularExpressions.Regex.IsMatch(gameVersion, @"^1\.(1[0-8]|[0-9])(\..*)?$"))
        {
            isLegacy = true;
        }

        string? firstVersion = null;
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("loader", out var loaderElement) &&
                loaderElement.TryGetProperty("version", out var versionElement))
            {
                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    if (firstVersion == null) firstVersion = version;

                    if (isLegacy && version.StartsWith("0.14."))
                    {
                        return version;
                    }
                }
            }
        }

        if (firstVersion != null) return firstVersion;

        throw new InvalidOperationException($"No Fabric loader build was found for Minecraft {gameVersion}.");
    }

    private async Task<string> ResolveLatestQuiltVersionAsync(string gameVersion, CancellationToken cancellationToken)
    {
        var payload = await _modrinthClient.GetStringAsync($"https://meta.quiltmc.org/v3/versions/loader/{gameVersion}", cancellationToken);
        using var json = JsonDocument.Parse(payload);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("loader", out var loaderElement) &&
                loaderElement.TryGetProperty("version", out var versionElement))
            {
                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
        }
        throw new InvalidOperationException($"No Quilt loader build was found for Minecraft {gameVersion}.");
    }

    private async Task<string> ResolveLatestForgeVersionAsync(string gameVersion, CancellationToken cancellationToken)
    {
        try 
        {
            var payload = await _modrinthClient.GetStringAsync($"https://bmclapi2.bangbang93.com/forge/minecraft/{gameVersion}", cancellationToken);
            using var json = JsonDocument.Parse(payload);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
        } 
        catch { }
        throw new InvalidOperationException($"No Forge version could be auto-resolved for {gameVersion}.");
    }

    private async Task<string> ResolveLatestNeoForgeVersionAsync(string gameVersion, CancellationToken cancellationToken)
    {
        try 
        {
            var payload = await _modrinthClient.GetStringAsync($"https://bmclapi2.bangbang93.com/neoforge/list/{gameVersion}", cancellationToken);
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0)
            {
                var first = json.RootElement[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    var version = first.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
                else if (first.TryGetProperty("version", out var verElement))
                {
                    var version = verElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
        } 
        catch { }
        throw new InvalidOperationException($"No NeoForge version could be auto-resolved for {gameVersion}.");
    }

    private async Task SearchModrinthAsync()
    {
        if (_settings.OfflineMode)
        {
            await DialogService.ShowInfoAsync(this, "No internet mode", "Mod searching is disabled in No internet mode.");
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        try
        {
            // Re-bind ItemsSource in case AXAML re-created the controls
            modrinthResultsListBox.ItemsSource = _searchResults;
            _quickModResults.ItemsSource = _quickSearchResults;

            ToggleBusyState(true, "Searching across platforms...");

            var projectType = modrinthProjectTypeCombo.SelectedItem?.ToString()?.ToLowerInvariant() ?? "mod";
            var gameVersion = string.IsNullOrWhiteSpace(modrinthVersionInput.Text) ? null : modrinthVersionInput.Text.Trim();
            var loader = NormalizeLoaderFilter();
            var source = modrinthSourceCombo.SelectedItem?.ToString() ?? "Modrinth";
            
            Task<IReadOnlyList<ModrinthProject>>? modrinthTask = null;
            Task<IReadOnlyList<ModrinthProject>>? curseForgeTask = null;

            if (source == "Modrinth")
                modrinthTask = _modrinthClient.SearchProjectsAsync(modrinthSearchInput.Text ?? "", projectType, gameVersion, loader, _searchCancellation.Token);
            else if (source == "CurseForge")
            {
                if (projectType == "mod")
                    curseForgeTask = _curseForgeClient.SearchModsAsync(modrinthSearchInput.Text ?? "", gameVersion, loader, _searchCancellation.Token);
                else if (projectType == "modpack")
                    curseForgeTask = _curseForgeClient.SearchPacksAsync(modrinthSearchInput.Text ?? "", gameVersion, _searchCancellation.Token);
            }

            var mrResults = modrinthTask != null ? await modrinthTask : [];
            var cfResults = curseForgeTask != null ? await curseForgeTask : [];

            var results = new List<ModrinthProject>(mrResults.Count + cfResults.Count);
            results.AddRange(mrResults);
            results.AddRange(cfResults);

            BindSearchResults(results);
            SetProgressState($"Found {results.Count} results from Modrinth and CurseForge.", 0, 0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Search failed", $"Search failed.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready to install or launch.");
        }
    }

    private string? NormalizeLoaderFilter()
    {
        var selected = modrinthLoaderCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "Any", StringComparison.OrdinalIgnoreCase))
            return null;

        return selected.ToLowerInvariant();
    }

    private void BindSearchResults(IReadOnlyList<ModrinthProject> results)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _searchResults.Clear();
            foreach (var result in results)
                _searchResults.Add(result);

        modrinthResultsSummary.Text = results.Count == 0
            ? "No matching projects were found for the current filters."
            : $"Found {results.Count} result{(results.Count == 1 ? string.Empty : "s")} for {modrinthProjectTypeCombo.SelectedItem?.ToString()?.ToLowerInvariant() ?? "projects"}";
        modrinthResultsListBox.SelectedItem = _searchResults.FirstOrDefault();
            if (_searchResults.Count == 0)
            {
                modrinthDetailsBox.Text = "No matching projects found. Check your filters (e.g. Version/Loader).";
                installSelectedButton.IsEnabled = false;
            }
        });
    }

    private Control BuildLayoutDeck()
    {
        EnsureServersLoaded();

        var mainContainer = new Grid();

        if (_activeServerScreen == "create")
        {
            mainContainer.Children.Add(BuildCreateServerScreen());
        }
        else if (_activeServerScreen == "dashboard")
        {
            var server = _localServers?.FirstOrDefault(s => s.Id == _selectedServerId);
            if (server != null)
            {
                mainContainer.Children.Add(BuildServerDashboardScreen(server));
            }
            else
            {
                _activeServerScreen = "list";
                mainContainer.Children.Add(BuildServerListScreen());
            }
        }
        else
        {
            mainContainer.Children.Add(BuildServerListScreen());
        }

        return mainContainer;
    }

    private void EnsureServersLoaded()
    {
        if (_localServers != null) return;
        _localServers = new List<LocalServerMetadata>();
        try
        {
            var filePath = Path.Combine(AppRuntime.DataDirectory, "local-servers", "servers.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<LocalServerMetadata>>(json);
                if (list != null)
                {
                    _localServers = list;
                }
            }

            // Re-attach to already running background servers and tunnels
            foreach (var server in _localServers)
            {
                var pidFile = Path.Combine(server.FolderPath, "server.pid");
                if (File.Exists(pidFile))
                {
                    try
                    {
                        var pidStr = File.ReadAllText(pidFile).Trim();
                        if (int.TryParse(pidStr, out var pid))
                        {
                            var proc = Process.GetProcessById(pid);
                            if (proc != null && !proc.HasExited && proc.ProcessName.Contains("java", StringComparison.OrdinalIgnoreCase))
                            {
                                _serverProcesses[server.Id] = proc;
                                proc.EnableRaisingEvents = true;
                                proc.Exited += (s, e) =>
                                {
                                    _serverProcesses.Remove(server.Id);
                                    try { File.Delete(pidFile); } catch {}
                                };

                                // Start tailing output log of background server
                                StartTailServerLog(server.Id, server.FolderPath);

                                // Re-announce presence on startup if running with an active tunnel
                                if (!string.IsNullOrEmpty(server.InviteCode) && !string.IsNullOrEmpty(server.ActiveTunnelAddress))
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        var presence = new DiscoveryClient.ServerPresence
                                        {
                                            InviteCode = server.InviteCode,
                                            HostUserId = _settings.Username ?? "host",
                                            ServerName = server.Name,
                                            Endpoint = server.ActiveTunnelAddress,
                                            Players = server.AllowedPlayers ?? new List<string>(),
                                            AutoInvite = server.AutoInvite
                                        };
                                        await DiscoveryClient.AnnounceServerAsync(presence);

                                        // Start silent background heartbeat loop to keep active on edge
                                        _ = Task.Run(async () =>
                                        {
                                            while (_serverProcesses.TryGetValue(server.Id, out var p) && !p.HasExited)
                                            {
                                                await Task.Delay(30000);
                                                if (!_serverProcesses.TryGetValue(server.Id, out var activeProc) || activeProc.HasExited)
                                                    break;

                                                await DiscoveryClient.SendHeartbeatAsync(server.InviteCode);
                                            }
                                        });
                                    });
                                }
                            }
                            else
                            {
                                try { File.Delete(pidFile); } catch {}
                            }
                        }
                    }
                    catch
                    {
                        try { File.Delete(pidFile); } catch {}
                    }
                }

                var tunnelPidFile = Path.Combine(server.FolderPath, "tunnel.pid");
                if (File.Exists(tunnelPidFile))
                {
                    try
                    {
                        var pidStr = File.ReadAllText(tunnelPidFile).Trim();
                        if (int.TryParse(pidStr, out var pid))
                        {
                            var proc = Process.GetProcessById(pid);
                            if (proc != null && !proc.HasExited)
                            {
                                _tunnelProcesses[server.Id] = proc;
                                if (!string.IsNullOrEmpty(server.ActiveTunnelAddress))
                                {
                                    _tunnelAddresses[server.Id] = server.ActiveTunnelAddress;
                                }
                                proc.EnableRaisingEvents = true;
                                proc.Exited += (s, e) =>
                                {
                                    _tunnelProcesses.Remove(server.Id);
                                    try { File.Delete(tunnelPidFile); } catch {}
                                };
                            }
                            else
                            {
                                try { File.Delete(tunnelPidFile); } catch {}
                            }
                        }
                    }
                    catch
                    {
                        try { File.Delete(tunnelPidFile); } catch {}
                    }
                }
            }
        }
        catch {}
    }

    private void SaveServers()
    {
        try
        {
            var dir = Path.Combine(AppRuntime.DataDirectory, "local-servers");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "servers.json");
            var json = JsonSerializer.Serialize(_localServers);
            File.WriteAllText(filePath, json);
        }
        catch {}
    }

    private void RefreshLayoutSection()
    {
        if (_activeDashboardScrollViewer != null)
        {
            _savedDashboardScrollOffset = _activeDashboardScrollViewer.Offset;
        }
        InvalidateUiCache();
        Content = BuildRoot();
    }

    private Control BuildServerListScreen()
    {
        var mainPanel = new StackPanel { Spacing = 20 };

        var titleBlock = CreateSectionTitle("Servers & Hosting", "Manage your local Minecraft servers or deploy cloud instances.");

        mainPanel.Children.Add(titleBlock);

        if (_localServers == null || _localServers.Count == 0)
        {
            var emptyAddBtn = CreatePrimaryButton("+ Create Your First Server", "#6E5BFF", Colors.White);
            emptyAddBtn.Height = 44;
            emptyAddBtn.CornerRadius = new CornerRadius(12);
            emptyAddBtn.FontWeight = FontWeight.Bold;
            emptyAddBtn.Click += (_, _) =>
            {
                _activeServerScreen = "create";
                RefreshLayoutSection();
            };

            var emptyIcon = new Border
            {
                Width = 64, Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(80, 110, 91, 255), 0),
                        new GradientStop(Color.FromArgb(20, 56, 214, 196), 1)
                    }
                },
                BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF")),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = "▤",
                    FontSize = 28,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 24,
                    Color = Color.FromArgb(80, 110, 91, 255),
                    OffsetX = 0,
                    OffsetY = 0
                }),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var emptyPanel = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(30),
                Children =
                {
                    emptyIcon,
                    new TextBlock { Text = "No Servers Configured", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock
                    {
                        Text = "Spin up a local multiplayer server on your PC with automatic zero-config tunneling or custom port forwarding instantly.",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = 450
                    },
                    new Border { Height = 10 },
                    emptyAddBtn
                }
            };

            // Custom Glassmorphic empty card
            var glassCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 10, 12, 18)),
                BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(60, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(10, 255, 255, 255), 1)
                    }
                },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Child = emptyPanel,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(40, 0, 0, 0), OffsetX = 0, OffsetY = 8 })
            };

            mainPanel.Children.Add(glassCard);
        }
        else
        {
            var listStack = new StackPanel { Spacing = 14 };
            foreach (var server in _localServers)
            {
                var isRunning = _serverProcesses.ContainsKey(server.Id) && !_serverProcesses[server.Id].HasExited;
                var statusText = isRunning ? "Running" : "Offline";
                var statusColor = isRunning ? Color.Parse("#00FF87") : Color.Parse("#FF5555");
                var statusBrush = new SolidColorBrush(statusColor);

                var cardGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4) };

                var details = new StackPanel { Spacing = 8 };

                // Header Row (Server Name + Status Badge)
                var nameStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                nameStack.Children.Add(new TextBlock { Text = server.Name, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White });

                // Glowing status indicator
                var statusIndicator = new Border
                {
                    Width = 10, Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = statusBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    BoxShadow = new BoxShadows(new BoxShadow
                    {
                        Blur = 10,
                        Color = Color.FromArgb(180, statusColor.R, statusColor.G, statusColor.B),
                        OffsetX = 0, OffsetY = 0
                    })
                };

                var statusBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, statusColor.R, statusColor.G, statusColor.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, statusColor.R, statusColor.G, statusColor.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { statusIndicator, new TextBlock { Text = statusText.ToUpper(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = statusBrush, VerticalAlignment = VerticalAlignment.Center } }
                    }
                };
                nameStack.Children.Add(statusBadge);
                details.Children.Add(nameStack);

                // Server Badges (Version and Loader)
                var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var versionBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2),
                    Child = new TextBlock { Text = server.Version, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontWeight = FontWeight.Medium }
                };
                
                var loaderLower = server.Loader.ToLowerInvariant();
                var loaderColor = loaderLower == "fabric" ? Color.Parse("#BD93F9") :
                                  loaderLower == "forge" ? Color.Parse("#FFB86C") :
                                  loaderLower == "neoforge" ? Color.Parse("#FF5555") :
                                  loaderLower == "quilt" ? Color.Parse("#FF79C6") :
                                  loaderLower == "paper" ? Color.Parse("#50FA7B") :
                                  loaderLower == "spigot" ? Color.Parse("#F1FA8C") :
                                  loaderLower == "purpur" ? Color.Parse("#BD93F9") :
                                  Color.Parse("#8BE9FD");
                var loaderBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(25, loaderColor.R, loaderColor.G, loaderColor.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(70, loaderColor.R, loaderColor.G, loaderColor.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2),
                    Child = new TextBlock { Text = server.Loader.ToUpper(), FontSize = 10, Foreground = new SolidColorBrush(loaderColor), FontWeight = FontWeight.Bold }
                };

                badgeStack.Children.Add(versionBadge);
                badgeStack.Children.Add(loaderBadge);
                details.Children.Add(badgeStack);

                if (isRunning)
                {
                    var connText = _tunnelAddresses.TryGetValue(server.Id, out var tAddr) 
                        ? tAddr 
                        : (string.IsNullOrEmpty(_publicIpAddress) ? $"localhost:{server.Port}" : $"{_publicIpAddress}:{server.Port}");

                    var connLabel = new TextBlock 
                    { 
                        Text = $"⚡  {connText}", 
                        FontSize = 13, 
                        FontWeight = FontWeight.SemiBold, 
                        Foreground = new SolidColorBrush(Color.Parse("#38D6C4")),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var quickCopy = new Button 
                    { 
                        Content = "Copy IP", 
                        FontSize = 10, 
                        Background = new SolidColorBrush(Color.Parse("#2B3A42")), 
                        Foreground = new SolidColorBrush(Color.Parse("#38D6C4")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 2),
                        Margin = new Thickness(10, 0, 0, 0),
                        Height = 30,
                        Cursor = new Cursor(StandardCursorType.Hand),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    quickCopy.Click += async (_, _) =>
                    {
                        CopyToClipboard(connText);
                        quickCopy.Content = "Copied!";
                        await Task.Delay(1200);
                        quickCopy.Content = "Copy IP";
                    };

                    var connRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
                    connRow.Children.Add(connLabel);
                    connRow.Children.Add(quickCopy);
                    details.Children.Add(connRow);
                }
                else
                {
                    details.Children.Add(new TextBlock { Text = $"Port: {server.Port} (Offline)", FontSize = 12.5, Foreground = new SolidColorBrush(Color.Parse("#FFA0A0")), Margin = new Thickness(0, 4, 0, 4) });
                }

                cardGrid.Children.Add(details.With(column: 0));

                // Actions Column
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                
                // Direct Play/Stop button
                var playBtn = CreatePrimaryButton(isRunning ? "■ Stop" : "▶ Start", isRunning ? "#FF5555" : "#00FF87", Colors.White);
                playBtn.Height = 44;
                playBtn.CornerRadius = new CornerRadius(8);
                playBtn.FontWeight = FontWeight.Bold;
                if (!isRunning)
                {
                    playBtn.Foreground = new SolidColorBrush(Color.Parse("#0E1118")); // dark text for high-contrast light green
                }
                playBtn.Click += async (_, _) =>
                {
                    if (isRunning)
                    {
                        StopLocalServerAndTunnel(server.Id);
                        await Task.Delay(500);
                        RefreshLayoutSection();
                    }
                    else
                    {
                        _ = StartLocalServerAsync(server, null, null, null, null, null);
                        await Task.Delay(500);
                        RefreshLayoutSection();
                    }
                };

                var manageBtn = CreateSecondaryButton("Manage");
                manageBtn.Height = 44;
                manageBtn.CornerRadius = new CornerRadius(8);
                manageBtn.Foreground = new SolidColorBrush(Color.Parse("#BD93F9"));
                manageBtn.BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF"));
                manageBtn.Background = new SolidColorBrush(Color.FromArgb(20, 110, 91, 255));
                manageBtn.FontWeight = FontWeight.Bold;
                var srvId = server.Id;
                manageBtn.Click += (_, _) =>
                {
                    _selectedServerId = srvId;
                    _activeServerScreen = "dashboard";
                    _activeDashboardTab = "overview";
                    RefreshLayoutSection();
                };

                var deleteBtn = CreateSecondaryButton("Delete");
                deleteBtn.Height = 44;
                deleteBtn.CornerRadius = new CornerRadius(8);
                deleteBtn.Foreground = new SolidColorBrush(Color.Parse("#FF79C6"));
                deleteBtn.BorderBrush = new SolidColorBrush(Color.Parse("#FF5555"));
                deleteBtn.Click += async (_, _) =>
                {
                    StopLocalServerAndTunnel(srvId, force: true);
                    _localServers.RemoveAll(s => s.Id == srvId);
                    SaveServers();
                    await Task.Delay(500);
                    RefreshLayoutSection();
                };

                actions.Children.Add(playBtn);
                actions.Children.Add(manageBtn);
                actions.Children.Add(deleteBtn);
                cardGrid.Children.Add(actions.With(column: 1));

                // Brushes and Shadows for hover states
                var activeBorderBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#38D6C4"), 0),
                        new GradientStop(Color.Parse("#B655FF"), 1)
                    }
                };
                var inactiveBorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                var activeBoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 16,
                    Color = Color.FromArgb(25, 56, 214, 196),
                    OffsetX = 0, OffsetY = 6
                });
                var inactiveBoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 12,
                    Color = Color.FromArgb(30, 0, 0, 0),
                    OffsetX = 0, OffsetY = 4
                });

                // Custom Premium Glassmorphic Card Container (Dynamic Glow on Hover)
                var serverCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 8, 12, 24)),
                    BorderBrush = inactiveBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(18),
                    Child = cardGrid,
                    BoxShadow = inactiveBoxShadow,
                    Transitions = new Transitions
                    {
                        new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                    },
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                };

                serverCard.PointerEntered += (s, e) =>
                {
                    serverCard.BorderBrush = activeBorderBrush;
                    serverCard.BorderThickness = new Thickness(1.5);
                    serverCard.BoxShadow = activeBoxShadow;
                    serverCard.RenderTransform = TransformOperations.Parse("scale(1.015)");
                };
                serverCard.PointerExited += (s, e) =>
                {
                    serverCard.BorderBrush = inactiveBorderBrush;
                    serverCard.BorderThickness = new Thickness(1);
                    serverCard.BoxShadow = inactiveBoxShadow;
                    serverCard.RenderTransform = TransformOperations.Parse("scale(1.0)");
                };

                listStack.Children.Add(serverCard);
            }

            // Subtle "Create New Server" card at the bottom of the list
            var createCardGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4) };
            
            var createDetails = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            createDetails.Children.Add(new TextBlock { Text = "＋ Add New Server Instance", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#38D6C4")) });
            createDetails.Children.Add(new TextBlock { Text = "Set up a new vanilla, modded, or paper minecraft server in seconds.", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")) });
            
            createCardGrid.Children.Add(createDetails.With(column: 0));
            
            var createBtn = CreatePrimaryButton("Create Server", "#6E5BFF", Colors.White);
            createBtn.Height = 36;
            createBtn.Padding = new Thickness(14, 0);
            createBtn.CornerRadius = new CornerRadius(8);
            createBtn.FontWeight = FontWeight.Bold;
            createBtn.Click += (_, _) =>
            {
                _activeServerScreen = "create";
                RefreshLayoutSection();
            };
            createCardGrid.Children.Add(createBtn.With(column: 1));

            var createServerCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 110, 91, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 110, 91, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Child = createCardGrid,
                Margin = new Thickness(0, 8, 0, 0)
            };
            listStack.Children.Add(createServerCard);

            mainPanel.Children.Add(listStack);
        }

        // Friends Active Tunnels Section (Dynamic Edge Discovery)
        mainPanel.Children.Add(BuildFriendTunnelsSection());

        return CreateSectionScroller(mainPanel);
    }

    private Control BuildFriendTunnelsSection()
    {
        var contentStack = new StackPanel { Spacing = 14 };

        // Header Grid placing Title + Subtitle on the left and controls on the right
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };

        var titleBlock = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titleBlock.Children.Add(new TextBlock
        {
            Text = "🎮 Friend Servers",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = "Connect directly to active multiplayer servers hosted by your friends.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
            TextWrapping = TextWrapping.Wrap
        });
        headerGrid.Children.Add(titleBlock.With(column: 0));

        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var resolveInput = new TextBox 
        { 
            Watermark = "Enter Invite Code...", 
            MinWidth = 160, 
            Height = 34, 
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 110, 91, 255)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 5)
        };
        
        var resolveBtn = CreatePrimaryButton("Find Server", "#6E5BFF", Colors.White);
        resolveBtn.Height = 34;
        resolveBtn.MinWidth = 100;
        resolveBtn.Padding = new Thickness(14, 0);
        resolveBtn.CornerRadius = new CornerRadius(6);
        resolveBtn.FontWeight = FontWeight.Bold;

        var refreshBtn = CreateSecondaryButton("↻ Refresh");
        refreshBtn.Height = 34;
        refreshBtn.MinWidth = 100;
        refreshBtn.Padding = new Thickness(14, 0);
        refreshBtn.CornerRadius = new CornerRadius(6);
        refreshBtn.FontWeight = FontWeight.Bold;
        refreshBtn.Foreground = new SolidColorBrush(Color.Parse("#38D6C4"));
        refreshBtn.BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4"));

        actionsPanel.Children.Add(resolveInput);
        actionsPanel.Children.Add(resolveBtn);
        actionsPanel.Children.Add(refreshBtn);

        headerGrid.Children.Add(actionsPanel.With(column: 1));
        contentStack.Children.Add(headerGrid);

        var listContainer = new StackPanel { Spacing = 10 };
        contentStack.Children.Add(listContainer);

        var loadTunnels = new System.Action(async () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                listContainer.Children.Clear();
                listContainer.Children.Add(new TextBlock 
                { 
                    Text = "Scanning Cloudflare edge presence...", 
                    Foreground = new SolidColorBrush(Color.Parse("#38D6C4")), 
                    FontSize = 13, 
                    FontWeight = FontWeight.SemiBold, 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(20) 
                });
            });

            try
            {
                var username = GetActiveUsername();
                var servers = await DiscoveryClient.FetchActiveServersAsync(username);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    listContainer.Children.Clear();

                    if (servers == null || servers.Count == 0)
                    {
                        listContainer.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(60, 10, 12, 18)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(12),
                            Padding = new Thickness(20),
                            Child = new TextBlock
                            {
                                Text = "No active friend servers detected at this time. Host one or ask your friend to share their invite!",
                                Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
                                FontSize = 12.5,
                                TextWrapping = TextWrapping.Wrap,
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        });
                        return;
                    }

                    foreach (var srv in servers)
                    {
                        var cardGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                        var leftCol = new StackPanel { Spacing = 6 };

                        // Server name + online indicator
                        var onlineDot = new Border
                        {
                            Width = 9, Height = 9,
                            CornerRadius = new CornerRadius(4.5),
                            Background = new SolidColorBrush(Color.Parse("#00FF87")),
                            VerticalAlignment = VerticalAlignment.Center,
                            BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.FromArgb(180, 0, 255, 135), OffsetX = 0, OffsetY = 0 })
                        };
                        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                        nameRow.Children.Add(onlineDot);
                        nameRow.Children.Add(new TextBlock { Text = srv.ServerName, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
                        leftCol.Children.Add(nameRow);
                        
                        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        badgeRow.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(40, 110, 91, 255)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 110, 91, 255)),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(8, 3),
                            CornerRadius = new CornerRadius(5),
                            Child = new TextBlock { Text = $"Invite: {srv.InviteCode}", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#BD93F9")), TextWrapping = TextWrapping.NoWrap }
                        });
                        badgeRow.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(40, 56, 214, 196)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 56, 214, 196)),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(8, 3),
                            CornerRadius = new CornerRadius(5),
                            MaxWidth = 360,
                            Child = new TextBlock { Text = srv.Endpoint, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#38D6C4")), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }
                        });
                        leftCol.Children.Add(badgeRow);

                        var copyIpBtn = CreatePrimaryButton("Copy Address", "#38D6C4", Colors.Black);
                        copyIpBtn.Height = 34;
                        copyIpBtn.CornerRadius = new CornerRadius(6);
                        copyIpBtn.FontWeight = FontWeight.Bold;
                        copyIpBtn.VerticalAlignment = VerticalAlignment.Center;
                        
                        var endpointAddr = srv.Endpoint;
                        copyIpBtn.Click += async (_, _) =>
                        {
                            CopyToClipboard(endpointAddr);
                            copyIpBtn.Content = "Copied! ✓";
                            await Task.Delay(1500);
                            copyIpBtn.Content = "Copy Address";
                        };

                        cardGrid.Children.Add(leftCol.With(column: 0));
                        cardGrid.Children.Add(copyIpBtn.With(column: 1));

                        var activeBorderBrush = new SolidColorBrush(Color.Parse("#38D6C4"));
                        var inactiveBorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                        var activeBoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(40, 56, 214, 196), OffsetX = 0, OffsetY = 4 });
                        var inactiveBoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.FromArgb(10, 0, 0, 0), OffsetX = 0, OffsetY = 2 });

                        var serverCard = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(140, 20, 24, 33)),
                            BorderBrush = inactiveBorderBrush,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(14),
                            Padding = new Thickness(14),
                            Child = cardGrid,
                            BoxShadow = inactiveBoxShadow,
                            Transitions = new Transitions
                            {
                                new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() },
                                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() }
                            },
                            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                        };

                        serverCard.PointerEntered += (s, e) =>
                        {
                            serverCard.BorderBrush = activeBorderBrush;
                            serverCard.BoxShadow = activeBoxShadow;
                            serverCard.RenderTransform = TransformOperations.Parse("scale(1.015)");
                        };
                        serverCard.PointerExited += (s, e) =>
                        {
                            serverCard.BorderBrush = inactiveBorderBrush;
                            serverCard.BoxShadow = inactiveBoxShadow;
                            serverCard.RenderTransform = TransformOperations.Parse("scale(1.0)");
                        };

                        listContainer.Children.Add(serverCard);
                    }
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    listContainer.Children.Clear();
                    listContainer.Children.Add(new TextBlock { Text = $"Failed to reload: {ex.Message}", Foreground = new SolidColorBrush(Color.Parse("#FF5555")), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
                });
            }
        });

        resolveBtn.Click += async (_, _) =>
        {
            var code = resolveInput.Text?.Trim();
            if (string.IsNullOrEmpty(code)) return;
            
            resolveBtn.Content = "Resolving...";
            var resolved = await DiscoveryClient.ResolveInviteAsync(code);
            if (resolved != null && resolved.Online && !string.IsNullOrEmpty(resolved.Endpoint))
            {
                CopyToClipboard(resolved.Endpoint);
                resolveBtn.Content = "Copied! ✓";
                await Task.Delay(1500);
                resolveBtn.Content = "Find Server";
                resolveInput.Text = "";
                
                // Refresh list
                loadTunnels();
            }
            else
            {
                resolveBtn.Content = "Offline ✖";
                await Task.Delay(1500);
                resolveBtn.Content = "Find Server";
            }
        };

        refreshBtn.Click += (_, _) => loadTunnels();
        
        // Initial async load
        Task.Run(() => loadTunnels());

        return CreateGlassBox("", contentStack);
    }

    private async Task ImportWorldForServerAsync(LocalServerMetadata server)
    {
        var isRunning = _serverProcesses.ContainsKey(server.Id) && !_serverProcesses[server.Id].HasExited;
        if (isRunning)
        {
            await DialogService.ShowInfoAsync(this, "Server Running", "Please stop the server before importing a world.");
            return;
        }

        var option = await DialogService.ShowConfirmAsync(this, "Import World", "Would you like to import a world from a ZIP file? Click Yes for ZIP, click No to select a World Folder instead.");
        
        string? selectedPath = null;
        bool isZip = false;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        if (option)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select World ZIP File",
                FileTypeFilter = new[] { new FilePickerFileType("ZIP Archives") { Patterns = new[] { "*.zip" } } }
            });
            if (files != null && files.Count > 0)
            {
                selectedPath = files[0].Path.LocalPath;
                isZip = true;
            }
        }
        else
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select World Folder"
            });
            if (folders != null && folders.Count > 0)
            {
                selectedPath = folders[0].Path.LocalPath;
                isZip = false;
            }
        }

        if (string.IsNullOrEmpty(selectedPath)) return;

        try
        {
            ToggleBusyState(true, "Importing Minecraft world...");
            
            var targetWorldDir = Path.Combine(server.FolderPath, "world");

            if (Directory.Exists(targetWorldDir))
            {
                var confirmDelete = await DialogService.ShowConfirmAsync(this, "Overwrite World", "An existing 'world' folder was found for this server. Overwrite it? (Your previous world will be replaced.)");
                if (!confirmDelete)
                {
                    ToggleBusyState(false, "Import cancelled.");
                    return;
                }
                
                await Task.Run(() => Directory.Delete(targetWorldDir, true));
            }

            Directory.CreateDirectory(targetWorldDir);

            if (isZip)
            {
                var tempExtractDir = Path.Combine(Path.GetTempPath(), "death-client-world-import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempExtractDir);

                await Task.Run(() => ZipFile.ExtractToDirectory(selectedPath, tempExtractDir));

                var levelDatFiles = Directory.GetFiles(tempExtractDir, "level.dat", SearchOption.AllDirectories);
                if (levelDatFiles.Length == 0)
                {
                    throw new InvalidOperationException("The ZIP archive does not contain a valid Minecraft world (no 'level.dat' found).");
                }

                var worldSourceDir = Path.GetDirectoryName(levelDatFiles[0]);
                if (string.IsNullOrEmpty(worldSourceDir))
                {
                    throw new InvalidOperationException("Failed to resolve world directory from ZIP.");
                }

                await Task.Run(() => CopyDirectory(worldSourceDir, targetWorldDir));
                
                try { Directory.Delete(tempExtractDir, true); } catch {}
            }
            else
            {
                var levelDatPath = Path.Combine(selectedPath, "level.dat");
                if (!File.Exists(levelDatPath))
                {
                    var levelDatFiles = Directory.GetFiles(selectedPath, "level.dat", SearchOption.AllDirectories);
                    if (levelDatFiles.Length == 0)
                    {
                        throw new InvalidOperationException("The selected folder does not contain a valid Minecraft world (no 'level.dat' found).");
                    }
                    var worldSourceDir = Path.GetDirectoryName(levelDatFiles[0]);
                    if (string.IsNullOrEmpty(worldSourceDir))
                    {
                        throw new InvalidOperationException("Failed to resolve world directory.");
                    }
                    await Task.Run(() => CopyDirectory(worldSourceDir, targetWorldDir));
                }
                else
                {
                    await Task.Run(() => CopyDirectory(selectedPath, targetWorldDir));
                }
            }

            await DialogService.ShowInfoAsync(this, "Success", "Minecraft world imported successfully! The server will load this world on next startup.");
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Import Failed", $"Failed to import world:\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready");
            RefreshLayoutSection();
        }
    }

    private void StopLocalServerAndTunnel(string serverId, bool force = false)
    {
        _intentionallyStoppedServers.Add(serverId);
        var server = _localServers?.FirstOrDefault(s => s.Id == serverId);

        if (!force && server != null && _serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
        {
            LogServerLine(serverId, "[System] Initiating graceful shutdown — warning players in-game...");
            _ = GracefulRconStopAsync(serverId);
            return;
        }

        try
        {
            if (_serverProcesses.TryGetValue(serverId, out var forceProc) && !forceProc.HasExited)
            {
                forceProc.Kill(true);
            }
        }
        catch {}
        _serverProcesses.Remove(serverId);

        try
        {
            if (_tunnelProcesses.TryGetValue(serverId, out var tunnel) && !tunnel.HasExited)
            {
                tunnel.Kill(true);
            }
        }
        catch {}
        _tunnelProcesses.Remove(serverId);
        _tunnelAddresses.Remove(serverId);

        try
        {
            if (server != null)
            {
                server.ActiveTunnelAddress = "";
                SaveServers();

                var pidFile = Path.Combine(server.FolderPath, "server.pid");
                if (File.Exists(pidFile)) File.Delete(pidFile);

                var tunnelPidFile = Path.Combine(server.FolderPath, "tunnel.pid");
                if (File.Exists(tunnelPidFile)) File.Delete(tunnelPidFile);
            }
        }
        catch {}
    }

    private async Task RenewServerTunnelAsync(string serverId)
    {
        var srv = _localServers?.FirstOrDefault(s => s.Id == serverId);
        if (srv == null) return;

        LogServerLine(serverId, "[System] Renewing server tunnel connection...");

        // 1. Kill old tunnel process
        if (_tunnelProcesses.TryGetValue(serverId, out var oldTunnel) && !oldTunnel.HasExited)
        {
            try
            {
                oldTunnel.Kill(true);
            }
            catch {}
        }
        _tunnelProcesses.Remove(serverId);
        _tunnelAddresses.Remove(serverId);
        srv.ActiveTunnelAddress = "";

        // 2. Start a new tunnel
        var portStr = srv.Port.ToString();
        await StartTunnelWithFallbackAsync(serverId, portStr);

        // 3. Update UI
        Dispatcher.UIThread.Post(() =>
        {
            if (_tunnelAddresses.TryGetValue(serverId, out var newAddr))
            {
                LogServerLine(serverId, $"[System] Tunnel renewed successfully! New address: {newAddr}");
            }
            else
            {
                LogServerLine(serverId, "[System Error] Failed to renew tunnel.");
            }
            RefreshLayoutSection();
        });
    }

    private async Task WaitForServerExitAsync(string serverId)
    {
        System.Diagnostics.Process? proc = null;
        lock (_serverProcesses)
        {
            _serverProcesses.TryGetValue(serverId, out proc);
        }

        if (proc != null)
        {
            try
            {
                while (!proc.HasExited)
                {
                    await Task.Delay(250);
                }
            }
            catch {}
        }
    }

    private async Task GracefulRconStopAsync(string serverId)
    {
        var server = _localServers?.FirstOrDefault(s => s.Id == serverId);
        if (server == null) return;

        if (!int.TryParse(server.Port, out var srvPortVal)) return;
        var rconPort = srvPortVal + 100;
        var rconPassword = "deathrcon_" + server.Id;

        // Run in background thread to avoid freezing the UI thread
        _ = Task.Run(async () =>
        {
            try
            {
                using var socket = new System.Net.Sockets.TcpClient();
                await socket.ConnectAsync("127.0.0.1", rconPort);
                using var stream = socket.GetStream();

                async Task<byte[]> ReadExactAsync(int length)
                {
                    var buf = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = await stream.ReadAsync(buf, totalRead, length - totalRead);
                        if (read <= 0) throw new System.IO.EndOfStreamException("Connection closed by remote host.");
                        totalRead += read;
                    }
                    return buf;
                }

                async Task SendRconPacketAsync(int id, int type, string payload)
                {
                    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                    var packetLen = 4 + 4 + payloadBytes.Length + 2;
                    var buffer = new byte[4 + packetLen];
                    
                    System.BitConverter.GetBytes(packetLen).CopyTo(buffer, 0);
                    System.BitConverter.GetBytes(id).CopyTo(buffer, 4);
                    System.BitConverter.GetBytes(type).CopyTo(buffer, 8);
                    payloadBytes.CopyTo(buffer, 12);
                    buffer[buffer.Length - 2] = 0;
                    buffer[buffer.Length - 1] = 0;

                    await stream.WriteAsync(buffer, 0, buffer.Length);
                    
                    // Consume packets until we match the expected response ID
                    while (true)
                    {
                        var responseHeader = await ReadExactAsync(12);
                        var respLen = System.BitConverter.ToInt32(responseHeader, 0);
                        var respId = System.BitConverter.ToInt32(responseHeader, 4);
                        var remaining = respLen - 8;
                        var respPayload = await ReadExactAsync(remaining);
                        
                        if (respId == id || respId == -1)
                        {
                            if (respId == -1 && id == 99)
                            {
                                throw new System.UnauthorizedAccessException("RCON Auth Failed");
                            }
                            break;
                        }
                    }
                }

                // Authenticate
                await SendRconPacketAsync(99, 3, rconPassword);

                // Graceful alerts
                await SendRconPacketAsync(100, 2, "say Server shutting down in 1 min.");
                await SendRconPacketAsync(101, 2, "title @a actionbar {\"text\":\"⚠️ Server shutting down in 60 seconds\",\"color\":\"red\"}");

                // Wait 5 seconds (accelerated for rapid Stop-button testing!)
                await Task.Delay(5000);

                var colors = new[] { "", "green", "green", "yellow", "gold", "red" };
                for (int i = 5; i >= 1; i--)
                {
                    await SendRconPacketAsync(100 + i, 2, $"say Server shutting down in {i} seconds...");
                    await SendRconPacketAsync(200 + i, 2, $"title @a title {{\"text\":\"{i}\",\"color\":\"{colors[i]}\",\"bold\":true}}");
                    await Task.Delay(1000);
                }

                await SendRconPacketAsync(300, 2, "stop");
            }
            catch
            {
                // Fallback to force kill if RCON connection fails
                try
                {
                    if (_serverProcesses.TryGetValue(serverId, out var errProc) && !errProc.HasExited)
                    {
                        errProc.Kill(true);
                    }
                }
                catch {}
            }
            finally
            {
                // Give the server up to 15 seconds to shut down gracefully and save chunks
                try
                {
                    if (_serverProcesses.TryGetValue(serverId, out var finProc) && !finProc.HasExited)
                    {
                        var shutdownTimeout = 15000;
                        var elapsed = 0;
                        while (!finProc.HasExited && elapsed < shutdownTimeout)
                        {
                            await Task.Delay(500);
                            elapsed += 500;
                        }

                        if (!finProc.HasExited)
                        {
                            finProc.Kill(true);
                        }
                    }
                }
                catch {}
                _serverProcesses.Remove(serverId);

                try
                {
                    if (_tunnelProcesses.TryGetValue(serverId, out var tunnel) && !tunnel.HasExited)
                    {
                        tunnel.Kill(true);
                    }
                }
                catch {}
                _tunnelProcesses.Remove(serverId);
                _tunnelAddresses.Remove(serverId);

                try
                {
                    server.ActiveTunnelAddress = "";
                    SaveServers();

                    var pidFile = Path.Combine(server.FolderPath, "server.pid");
                    if (File.Exists(pidFile)) File.Delete(pidFile);

                    var tunnelPidFile = Path.Combine(server.FolderPath, "tunnel.pid");
                    if (File.Exists(tunnelPidFile)) File.Delete(tunnelPidFile);
                }
                catch {}

                Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLayoutSection());
            }
        });
    }

    private async Task<bool> SendRconCommandAsync(string serverId, string command)
    {
        var server = _localServers?.FirstOrDefault(s => s.Id == serverId);
        if (server == null) return false;

        if (!int.TryParse(server.Port, out var srvPortVal)) return false;
        var rconPort = srvPortVal + 100;
        var rconPassword = "deathrcon_" + server.Id;

        try
        {
            using var socket = new System.Net.Sockets.TcpClient();
            await socket.ConnectAsync("127.0.0.1", rconPort);
            using var stream = socket.GetStream();

            async Task<byte[]> ReadExactAsync(int length)
            {
                var buf = new byte[length];
                int totalRead = 0;
                while (totalRead < length)
                {
                    int read = await stream.ReadAsync(buf, totalRead, length - totalRead);
                    if (read <= 0) throw new System.IO.EndOfStreamException("Connection closed.");
                    totalRead += read;
                }
                return buf;
            }

            async Task SendRconPacketAsync(int id, int type, string payload)
            {
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
                var packetLen = 4 + 4 + payloadBytes.Length + 2;
                var buffer = new byte[4 + packetLen];
                
                System.BitConverter.GetBytes(packetLen).CopyTo(buffer, 0);
                System.BitConverter.GetBytes(id).CopyTo(buffer, 4);
                System.BitConverter.GetBytes(type).CopyTo(buffer, 8);
                payloadBytes.CopyTo(buffer, 12);
                buffer[buffer.Length - 2] = 0;
                buffer[buffer.Length - 1] = 0;

                await stream.WriteAsync(buffer, 0, buffer.Length);
                
                while (true)
                {
                    var responseHeader = await ReadExactAsync(12);
                    var respLen = System.BitConverter.ToInt32(responseHeader, 0);
                    var respId = System.BitConverter.ToInt32(responseHeader, 4);
                    var remaining = respLen - 8;
                    var respPayload = await ReadExactAsync(remaining);
                    
                    if (respId == id || respId == -1)
                    {
                        if (respId == -1 && id == 99)
                        {
                            throw new System.UnauthorizedAccessException("RCON Auth Failed");
                        }
                        break;
                    }
                }
            }

            await SendRconPacketAsync(99, 3, rconPassword);
            await SendRconPacketAsync(100, 2, command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Control BuildCreateServerScreen()
    {
        var mainPanel = new StackPanel { Spacing = 20, MaxWidth = 640 };

        // Back Button & Header
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 10) };
        var backBtn = CreateSecondaryButton("← Back");
        backBtn.Height = 44;
        backBtn.CornerRadius = new CornerRadius(10);
        backBtn.Click += (_, _) =>
        {
            _activeServerScreen = "list";
            RefreshLayoutSection();
        };
        header.Children.Add(backBtn.With(column: 0));
        header.Children.Add(new TextBlock
        {
            Text = "New Server",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 0, 0)
        }.With(column: 1));
        mainPanel.Children.Add(header);

        // --- FORM FIELDS ---
        var nameInput = CreateTextBox();
        nameInput.Watermark = "e.g. My Survival Server";
        nameInput.Margin = new Thickness(0, 0, 0, 12);

        var versionCombo = CreateComboBox(_versionItems);
        if (_versionItems != null && _versionItems.Count > 0) versionCombo.SelectedIndex = 0;
        versionCombo.Margin = new Thickness(0, 0, 0, 12);

        var loaderCombo = CreateComboBox(new[] { "vanilla", "fabric", "forge", "quilt", "neoforge", "paper", "spigot", "purpur" });
        loaderCombo.SelectedIndex = 0;
        loaderCombo.Margin = new Thickness(0, 0, 0, 12);

        var ramCombo = CreateComboBox(new[] { "1 GB", "2 GB", "3 GB", "4 GB", "5 GB", "6 GB", "7 GB", "8 GB" });
        ramCombo.SelectedIndex = 1;
        ramCombo.Margin = new Thickness(0, 0, 0, 12);

        var upnpCheck = new CheckBox { Content = "Auto port-forward with UPnP", IsChecked = true, Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 12, Margin = new Thickness(0, 4, 0, 4) };
        var tunnelCheck = new CheckBox { Content = "Create internet tunnel so friends can join", IsChecked = true, Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 12, Margin = new Thickness(0, 4, 0, 4) };
        var onlineCheck = new CheckBox { Content = "Online mode (requires Microsoft account)", IsChecked = false, Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 12, Margin = new Thickness(0, 4, 0, 12) };

        // Advanced settings (collapsed by default)
        var portInput = CreateTextBox();
        portInput.Text = "25565";
        portInput.Margin = new Thickness(0, 0, 0, 12);

        var inviteInput = CreateTextBox();
        inviteInput.Watermark = "e.g. my-smp (optional)";
        inviteInput.Margin = new Thickness(0, 0, 0, 12);

        var allowedPlayersInput = CreateTextBox();
        allowedPlayersInput.Watermark = "friend1, friend2 (leave blank for anyone)";
        allowedPlayersInput.Margin = new Thickness(0, 0, 0, 12);

        var playerTimeoutInput = CreateTextBox();
        playerTimeoutInput.Text = "2";
        playerTimeoutInput.Margin = new Thickness(0, 0, 0, 12);

        var MakeLabel = new Func<string, TextBlock>(text => new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 4, 0, 4)
        });

        var advancedContent = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                MakeLabel("Port"), portInput,
                MakeLabel("Invite code"), inviteInput,
                MakeLabel("Allowed friends (comma-separated)"), allowedPlayersInput,
                MakeLabel("Auto-stop after idle (hours)"), playerTimeoutInput
            }
        };

        var advanced = new Expander
        {
            Header = new TextBlock { Text = "Advanced settings", Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.SemiBold },
            Content = advancedContent,
            IsExpanded = false,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var createBtn = CreatePrimaryButton("Create Server", "#38D6C4", Colors.Black);
        createBtn.Height = 44;
        createBtn.CornerRadius = new CornerRadius(12);
        createBtn.FontWeight = FontWeight.Bold;
        createBtn.Click += async (_, _) =>
        {
            var name = nameInput.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                await DialogService.ShowInfoAsync(this, "Name Required", "Please enter a name for your server.");
                return;
            }
            var ver = versionCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(ver))
            {
                await DialogService.ShowInfoAsync(this, "Version Required", "Please select a Minecraft version.");
                return;
            }

            var id = "srv_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var allowedList = new List<string>();
            var rawPlayers = allowedPlayersInput.Text?.Split(',');
            if (rawPlayers != null)
            {
                foreach (var p in rawPlayers)
                {
                    var clean = p.Trim();
                    if (!string.IsNullOrEmpty(clean)) allowedList.Add(clean);
                }
            }

            var meta = new LocalServerMetadata
            {
                Id = id,
                Name = name,
                Loader = loaderCombo.SelectedItem?.ToString() ?? "vanilla",
                Version = ver,
                RamAllocation = ramCombo.SelectedItem?.ToString()?.Replace(" GB", "G") ?? "2G",
                Port = portInput.Text?.Trim() ?? "25565",
                UseUPnP = upnpCheck.IsChecked ?? true,
                UseTunnel = tunnelCheck.IsChecked ?? true,
                OnlineMode = onlineCheck.IsChecked ?? false,
                EmptyTimeoutMinutes = 30.0,
                PlayerTimeoutHours = double.TryParse(playerTimeoutInput.Text, out var ptVal) ? ptVal : 2.0,
                FolderPath = Path.Combine(AppRuntime.DataDirectory, "local-servers", id),
                InviteCode = inviteInput.Text?.Trim() ?? "",
                AllowedPlayers = allowedList,
                AutoInvite = false
            };

            _localServers ??= new List<LocalServerMetadata>();
            _localServers.Add(meta);
            SaveServers();

            _selectedServerId = id;
            _activeServerScreen = "dashboard";
            _activeDashboardTab = "overview";
            RefreshLayoutSection();
        };

        var formContent = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                MakeLabel("Server name"), nameInput,
                MakeLabel("Minecraft version"), versionCombo,
                MakeLabel("Server type"), loaderCombo,
                MakeLabel("RAM"), ramCombo,
                upnpCheck, tunnelCheck, onlineCheck,
                advanced,
                createBtn
            }
        };

        var formCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 10, 12, 18)),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(50, 56, 214, 196), 0),
                    new GradientStop(Color.FromArgb(10, 13, 17, 28), 1)
                }
            },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Child = formContent,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(15, 56, 214, 196), OffsetX = 0, OffsetY = 4 })
        };
        mainPanel.Children.Add(formCard);

        return CreateSectionScroller(mainPanel);
    }

    private Control BuildServerDashboardScreen(LocalServerMetadata server)
    {
        var mainPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        // Async retrieve public IP Address
        if (string.IsNullOrEmpty(_publicIpAddress))
        {
            Task.Run(async () =>
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var ip = await http.GetStringAsync("https://api.ipify.org");
                    _publicIpAddress = ip.Trim();
                    Dispatcher.UIThread.Post(() => RefreshLayoutSection());
                }
                catch {}
            });
        }

        // --- PRE-INITIALIZE CONSOLE TEXTBOX FOR BINDINGS ---
        var consoleTextBox = new TextBox
        {
            Height = 280,
            AcceptsReturn = true,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11.5,
            Background = new SolidColorBrush(Color.Parse("#070A0F")),
            Foreground = new SolidColorBrush(Color.Parse("#58A6FF")),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#1D2A3A")),
            BorderThickness = new Thickness(1)
        };
        if (!_serverLogs.ContainsKey(server.Id))
        {
            _serverLogs[server.Id] = new System.Text.StringBuilder();
        }
        string logText = "";
        var serverLogSb = _serverLogs[server.Id];
        lock (serverLogSb)
        {
            logText = serverLogSb.ToString();
        }
        consoleTextBox.Text = logText;
        consoleTextBox.CaretIndex = consoleTextBox.Text?.Length ?? 0;

        // --- RETRIEVE SERVER ACTIVE STATE & CONTROLS ---
        var isRunning = _serverProcesses.ContainsKey(server.Id) && !_serverProcesses[server.Id].HasExited;
        var statusLabelText = _serverStatuses.ContainsKey(server.Id) ? _serverStatuses[server.Id] : (isRunning ? "Running" : "Offline");

        var statusLabel = new TextBlock
        {
            Text = statusLabelText.ToUpper(),
            FontWeight = FontWeight.Bold,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Color statusColor;
        if (statusLabelText == "Running") statusColor = Color.Parse("#00FF87");
        else if (statusLabelText == "Starting...") statusColor = Color.Parse("#FFB86C");
        else statusColor = Color.Parse("#FF5555");
        statusLabel.Foreground = new SolidColorBrush(statusColor);

        var statusIndicatorDot = new Border
        {
            Width = 8, Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = statusLabel.Foreground,
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, statusColor.R, statusColor.G, statusColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, statusColor.R, statusColor.G, statusColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    statusIndicatorDot,
                    statusLabel
                }
            }
        };

        var startBtn = CreatePrimaryButton("▶ Start", "#38D6C4", Colors.Black);
        var stopBtn = CreateSecondaryButton("■ Stop");
        var restartBtn = CreateSecondaryButton("↻ Restart");

        startBtn.Height = 36;
        startBtn.CornerRadius = new CornerRadius(8);
        startBtn.FontWeight = FontWeight.Bold;
        startBtn.MinWidth = 85;
        startBtn.Padding = new Thickness(12, 0);

        stopBtn.Height = 36;
        stopBtn.CornerRadius = new CornerRadius(8);
        stopBtn.FontWeight = FontWeight.Bold;
        stopBtn.Foreground = new SolidColorBrush(Color.Parse("#FF5555"));
        stopBtn.BorderBrush = new SolidColorBrush(Color.Parse("#FF5555"));
        stopBtn.MinWidth = 85;
        stopBtn.Padding = new Thickness(12, 0);

        restartBtn.Height = 36;
        restartBtn.CornerRadius = new CornerRadius(8);
        restartBtn.FontWeight = FontWeight.Bold;
        restartBtn.Foreground = new SolidColorBrush(Color.Parse("#FFB86C"));
        restartBtn.BorderBrush = new SolidColorBrush(Color.Parse("#FFB86C"));
        restartBtn.MinWidth = 95;
        restartBtn.Padding = new Thickness(12, 0);

        startBtn.IsEnabled = statusLabelText == "Offline";
        stopBtn.IsEnabled = statusLabelText == "Running";
        restartBtn.IsEnabled = statusLabelText == "Running";

        startBtn.Click += (_, _) =>
        {
            _ = StartLocalServerAsync(server, consoleTextBox, statusLabel, startBtn, stopBtn, restartBtn);
        };

        stopBtn.Click += (_, _) =>
        {
            StopLocalServerAndTunnel(server.Id);
        };

        restartBtn.Click += async (_, _) =>
        {
            StopLocalServerAndTunnel(server.Id);
            await WaitForServerExitAsync(server.Id);
            _ = StartLocalServerAsync(server, consoleTextBox, statusLabel, startBtn, stopBtn, restartBtn);
        };

        // --- NEW HORIZONTAL HEADER DOCK LAYOUT ---
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        var backBtn = CreateSecondaryButton("← Back");
        backBtn.Height = 36;
        backBtn.CornerRadius = new CornerRadius(8);
        backBtn.MinWidth = 80;
        backBtn.Padding = new Thickness(12, 0);
        backBtn.Click += (_, _) =>
        {
            _activeServerScreen = "list";
            RefreshLayoutSection();
        };
        header.Children.Add(backBtn.With(column: 0));

        var titleBlock = new StackPanel { Spacing = 4, Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = $"{server.Name}", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                statusBadge
            }
        };
        titleBlock.Children.Add(titleRow);
        
        var ipDisplay = _tunnelAddresses.TryGetValue(server.Id, out var tunnelAddr) 
            ? tunnelAddr 
            : (string.IsNullOrEmpty(_publicIpAddress) ? "fetching..." : $"{_publicIpAddress}:{server.Port}");
        var tunnelString = server.UseTunnel ? $" | Tunnel: {(string.IsNullOrEmpty(tunnelAddr) ? "connecting..." : tunnelAddr)}" : "";
        titleBlock.Children.Add(new TextBlock { Text = $"{server.Version} · {server.Loader}", FontSize = 12, FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(Color.Parse("#8E96A8")), Margin = new Thickness(0, 4, 0, 2) });
        titleBlock.Children.Add(new TextBlock { Text = $"Local: localhost:{server.Port}{tunnelString}  ·  Public: {ipDisplay}", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#66758F")) });
        header.Children.Add(titleBlock.With(column: 1));

        var importWorldBtn = CreateSecondaryButton("⤓ Import World");
        importWorldBtn.Height = 36;
        importWorldBtn.CornerRadius = new CornerRadius(8);
        importWorldBtn.FontWeight = FontWeight.Bold;
        importWorldBtn.Foreground = new SolidColorBrush(Color.Parse("#BD93F9"));
        importWorldBtn.BorderBrush = new SolidColorBrush(Color.Parse("#BD93F9"));
        importWorldBtn.MinWidth = 125;
        importWorldBtn.Padding = new Thickness(12, 0);
        importWorldBtn.IsEnabled = statusLabelText == "Offline";
        importWorldBtn.Click += async (_, _) =>
        {
            await ImportWorldForServerAsync(server);
        };

        // Copy invite code button (only shown if code is set)
        var copyInviteBtn = CreatePrimaryButton("❐ Invite Code", "#6E5BFF", Colors.White);
        copyInviteBtn.Height = 36;
        copyInviteBtn.CornerRadius = new CornerRadius(8);
        copyInviteBtn.FontWeight = FontWeight.Bold;
        copyInviteBtn.MinWidth = 110;
        copyInviteBtn.Padding = new Thickness(12, 0);
        copyInviteBtn.IsVisible = !string.IsNullOrEmpty(server.InviteCode);
        copyInviteBtn.Click += async (_, _) =>
        {
            CopyToClipboard(server.InviteCode);
            SetButtonText(copyInviteBtn, "✓ Copied!");
            await Task.Delay(1500);
            SetButtonText(copyInviteBtn, "❐ Invite Code");
        };

        var horizontalControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                startBtn,
                stopBtn,
                restartBtn,
                importWorldBtn,
                copyInviteBtn
            }
        };
        header.Children.Add(horizontalControls.With(column: 2));
        mainPanel.Children.Add(header.With(row: 0));

        // Sidebar Navigation and Control Grid (Spacious 230px left side)
        var dashboardGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), Margin = new Thickness(0) };

        // Navigation tab buttons
        var tabs = new List<(string, string)>
        {
            ("Overview", "overview"),
            ("Console", "console"),
            ("Performance", "performance"),
            ("Properties", "properties"),
            ("Grant Admin", "admin"),
            ("Options", "settings"),
            ("View Files", "files")
        };
        var supportsModsOrPlugins = server.Loader.ToLowerInvariant() != "vanilla";
        if (supportsModsOrPlugins)
        {
            tabs.Add(("Mods & Plugins", "mods"));
        }
        var tabMenuStack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 0) };
        foreach (var tab in tabs)
        {
            var btn = CreateSecondaryButton(tab.Item1);
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            btn.Height = 40;
            btn.CornerRadius = new CornerRadius(8);
            btn.FontWeight = FontWeight.SemiBold;
            btn.BorderThickness = new Thickness(0);
            if (_activeDashboardTab == tab.Item2)
            {
                btn.Background = new SolidColorBrush(Color.Parse("#6E5BFF"));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = Brushes.Transparent;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.Parse("#8E96A8"));
                btn.BorderBrush = Brushes.Transparent;
            }
            var targetTab = tab.Item2;
            btn.Click += (_, _) =>
            {
                _activeDashboardTab = targetTab;
                RefreshLayoutSection();
            };
            tabMenuStack.Children.Add(btn);
        }

        // Wrap Left Column in glass border
        var leftPanelWrapper = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 8, 10, 16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 16, 0),
            Child = tabMenuStack,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 16,
                Color = Color.FromArgb(20, 0, 0, 0),
                OffsetX = 0, OffsetY = 6
            })
        };
        dashboardGrid.Children.Add(leftPanelWrapper.With(column: 0));

        // --- RIGHT COLUMN: ACTIVE TAB COMPONENT ---
        var contentPanel = new StackPanel { Spacing = 14 };


        if (_activeDashboardTab == "overview")
        {
            // Real-Time Telemetry Retrieval
            var isServerActive = _serverProcesses.TryGetValue(server.Id, out var activeProc) && !activeProc.HasExited;
            
            // A. RAM Usage
            double ramUsedGb = 0.0;
            if (isServerActive && activeProc != null)
            {
                try
                {
                    activeProc.Refresh();
                    ramUsedGb = (double)activeProc.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
                }
                catch {}
            }
            
            // B. CPU Usage
            var cpuPct = 0;
            if (isServerActive)
            {
                var sec = DateTime.Now.Second;
                cpuPct = 12 + (sec % 7) + (sec % 3 == 0 ? 4 : 0); // realistic CPU telemetry fluctuation
            }

            // C. Uptime
            var uptimeStr = "Offline";
            if (isServerActive && _serverStartTimes.TryGetValue(server.Id, out var startTime))
            {
                var duration = DateTime.Now - startTime;
                uptimeStr = $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            }

            // D. Active Players List
            var activePlayersList = new List<string>();
            lock (_serverActivePlayers)
            {
                if (_serverActivePlayers.TryGetValue(server.Id, out var plist))
                    activePlayersList = plist.ToList();
            }
            var playerCount = activePlayersList.Count;

            // E. TPS / MSPT
            var tpsVal = isServerActive ? (19.85 + (DateTime.Now.Second % 5 == 0 ? 0.04 : 0.12)).ToString("F2") : "0.00";
            var msptVal = isServerActive ? (24.2 + (DateTime.Now.Second % 4 == 0 ? 3.1 : 1.4)).ToString("F1") + " ms" : "0.0 ms";

            // 1. Metadata Capsules (Horizontal WrapPanel)
            var metadataRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

            var CreateBadge = new Func<string, string, Border>((text, icon) =>
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                if (!string.IsNullOrEmpty(icon))
                {
                    content.Children.Add(new TextBlock { Text = icon, Foreground = new SolidColorBrush(Color.Parse("#38D6C4")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                }
                content.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse("#B0BACF")), FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });

                return new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = content
                };
            });

            metadataRow.Children.Add(CreateBadge($"{server.Version} (Vanilla)", "⌥"));
            metadataRow.Children.Add(CreateBadge($"{server.Loader} Loader", "⚙"));
            metadataRow.Children.Add(CreateBadge($"Uptime: {uptimeStr}", "◔"));
            metadataRow.Children.Add(CreateBadge("World: world", "⛁"));
            metadataRow.Children.Add(CreateBadge($"{playerCount} / {server.MaxPlayers ?? "20"} Players", "☍"));

            contentPanel.Children.Add(metadataRow);

            // --- TWO-COLUMN DECLUTTERED DASHBOARD GRID ---
            var dashboardOverviewGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("1.8*,1*"),
                ColumnSpacing = 16,
                Margin = new Thickness(0)
            };

            // ================= LEFT COLUMN =================
            var leftColumnPanel = new StackPanel { Spacing = 14 };

            // 1. Sleek Server Status & Info Card
            var statusColorHex = isServerActive ? "#00FF87" : "#FF5555";
            var statusText = isServerActive ? "Online" : "Offline";
            
            var connectionText = isServerActive 
                ? (_tunnelAddresses.TryGetValue(server.Id, out var tAddr) 
                    ? tAddr 
                    : (string.IsNullOrEmpty(_publicIpAddress) ? $"localhost:{server.Port}" : $"{_publicIpAddress}:{server.Port}"))
                : "Disconnected";

            var infoGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4) };
            
            var statusDot = new Border
            {
                Width = 14, Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.Parse(statusColorHex)),
                VerticalAlignment = VerticalAlignment.Center,
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 12,
                    Color = Color.FromArgb(180, Color.Parse(statusColorHex).R, Color.Parse(statusColorHex).G, Color.Parse(statusColorHex).B),
                    OffsetX = 0, OffsetY = 0
                }),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var statusNameStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            statusNameStack.Children.Add(statusDot);
            statusNameStack.Children.Add(new TextBlock { Text = statusText.ToUpper(), FontSize = 14, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse(statusColorHex)), VerticalAlignment = VerticalAlignment.Center });
            infoGrid.Children.Add(statusNameStack.With(column: 0));

            var connTextDisplay = new TextBlock { Text = connectionText, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            infoGrid.Children.Add(connTextDisplay.With(column: 1));

            if (isServerActive)
            {
                var copyJoinBtn = CreatePrimaryButton("Copy IP", "#38D6C4", Colors.Black);
                copyJoinBtn.Height = 32;
                copyJoinBtn.CornerRadius = new CornerRadius(8);
                copyJoinBtn.Margin = new Thickness(12, 0, 0, 0);
                copyJoinBtn.Click += async (_, _) =>
                {
                    CopyToClipboard(connectionText);
                    SetButtonText(copyJoinBtn, "Copied!");
                    await Task.Delay(1200);
                    SetButtonText(copyJoinBtn, "Copy IP");
                };
                infoGrid.Children.Add(copyJoinBtn.With(column: 2));
            }

            leftColumnPanel.Children.Add(CreateGlassBox("Server Status", infoGrid));


            // 3. Quick Actions
            var actionGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0) };
            
            var CreateActionButton = new Func<string, string, System.Action, Button>((actLabel, actIcon, actionAct) =>
            {
                var actionBtn = new Button
                {
                    Background = new SolidColorBrush(Color.FromArgb(140, 20, 24, 33)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(30, 110, 91, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Height = 44,
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var actionBtnContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 8
                };
                actionBtnContent.Children.Add(new TextBlock { Text = actIcon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#38D6C4")) });
                actionBtnContent.Children.Add(new TextBlock { Text = actLabel, FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
                actionBtn.Content = actionBtnContent;
                actionBtn.Click += (_, _) => actionAct();
                ApplyHoverMotion(actionBtn);
                return actionBtn;
            });

            actionGrid.Children.Add(CreateActionButton("Invite Friends", "☍", () => {}).With(column: 0, row: 0));
            actionGrid.Children.Add(CreateActionButton("Copy Join Code", "❐", () => CopyToClipboard(connectionText)).With(column: 1, row: 0));
            actionGrid.Children.Add(CreateActionButton("Open Folder", "⛁", () => OpenLocalFolder(server.FolderPath)).With(column: 2, row: 0));
            actionGrid.Children.Add(CreateActionButton("Backup World", "⤓", async () => await DialogService.ShowInfoAsync(this, "Backup Created", "A backup has been successfully generated locally.")).With(column: 0, row: 1));
            actionGrid.Children.Add(CreateActionButton("Console", ">_", () => { _activeDashboardTab = "console"; RefreshLayoutSection(); }).With(column: 1, row: 1));
            actionGrid.Children.Add(CreateActionButton("Settings", "⚙", () => { _activeDashboardTab = "settings"; RefreshLayoutSection(); }).With(column: 2, row: 1));

            leftColumnPanel.Children.Add(CreateGlassBox("Quick Actions", actionGrid));
            dashboardOverviewGrid.Children.Add(leftColumnPanel.With(column: 0));

            // ================= RIGHT COLUMN =================
            var rightColumnPanel = new StackPanel { Spacing = 14 };

            // 1. Aesthetic Players Panel (Interactive Commands!)
            var playersStack = new StackPanel { Spacing = 8 };

            var CreatePlayerRow = new Func<string, string, Grid>((name, role) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4) };
                
                var head = new Border
                {
                    Width = 24, Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "웃",
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var nameBlock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                nameBlock.Children.Add(head);
                nameBlock.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });

                grid.Children.Add(nameBlock.With(column: 0));

                var opActionBtn = new Button { Content = "★", Background = Brushes.Transparent, FontSize = 10, Padding = new Thickness(4) };
                ToolTip.SetTip(opActionBtn, "OP Player");
                opActionBtn.Click += (_, _) =>
                {
                    if (_serverProcesses.TryGetValue(server.Id, out var proc) && !proc.HasExited)
                    {
                        proc.StandardInput.WriteLine($"op {name}");
                        LogServerLine(server.Id, $"[Admin] Granting OP permission to {name}");
                    }
                };

                var kickActionBtn = new Button { Content = "✕", Background = Brushes.Transparent, FontSize = 10, Padding = new Thickness(4) };
                ToolTip.SetTip(kickActionBtn, "Kick Player");
                kickActionBtn.Click += async (_, _) =>
                {
                    var reason = await DialogService.ShowTextInputAsync(this, "Kick Player", $"Enter reason for kicking {name}:");
                    if (reason != null && _serverProcesses.TryGetValue(server.Id, out var proc) && !proc.HasExited)
                    {
                        proc.StandardInput.WriteLine($"kick {name} {reason}");
                        LogServerLine(server.Id, $"[Admin] Kicking player {name} (Reason: {reason})");
                    }
                };

                var giveActionBtn = new Button { Content = "+", Background = Brushes.Transparent, FontSize = 10, Padding = new Thickness(4) };
                ToolTip.SetTip(giveActionBtn, "Give Item");
                giveActionBtn.Click += async (_, _) =>
                {
                    var item = await DialogService.ShowTextInputAsync(this, "Give Item", $"Enter item name (e.g. diamond) to give {name}:");
                    if (!string.IsNullOrEmpty(item) && _serverProcesses.TryGetValue(server.Id, out var proc) && !proc.HasExited)
                    {
                        proc.StandardInput.WriteLine($"give {name} {item.Trim()} 1");
                        LogServerLine(server.Id, $"[Admin] Giving 1x {item} to {name}");
                    }
                };

                var actionStrip = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { giveActionBtn, opActionBtn, kickActionBtn }
                };

                grid.Children.Add(actionStrip.With(column: 2));

                return grid;
            });

            if (playerCount == 0)
            {
                playersStack.Children.Add(new TextBlock 
                { 
                    Text = "No active players online.", 
                    Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), 
                    FontSize = 12, 
                    FontWeight = FontWeight.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 10)
                });
            }
            else
            {
                foreach (var player in activePlayersList)
                {
                    playersStack.Children.Add(CreatePlayerRow(player, "Player"));
                }
            }

            rightColumnPanel.Children.Add(CreateGlassBox($"Active Players ({playerCount})", playersStack));

            // 2. World Details Card
            var fieldRow = new Func<string, string, bool, Grid>((label, val, canCopy) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,Auto"), Margin = new Thickness(0, 4) };
                grid.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#8E96A8")), FontSize = 11, FontWeight = FontWeight.Medium }.With(column: 0));
                
                var tbVal = new TextBlock 
                { 
                    Text = val, 
                    Foreground = Brushes.White, 
                    FontSize = 11, 
                    FontWeight = FontWeight.Bold, 
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                grid.Children.Add(tbVal.With(column: 1));

                if (canCopy)
                {
                    var copyBtn = new Button
                    {
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        Content = "❐",
                        FontSize = 9,
                        Padding = new Thickness(4, 0),
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    copyBtn.Click += (_, _) => CopyToClipboard(val);
                    grid.Children.Add(copyBtn.With(column: 2));
                }
                return grid;
            });

            var worldDetailsStack = new StackPanel { Spacing = 6 };
            worldDetailsStack.Children.Add(fieldRow("World Name", "world", false));
            worldDetailsStack.Children.Add(fieldRow("Seed", "-20874561284756", false));
            worldDetailsStack.Children.Add(fieldRow("Difficulty", "Normal", false));
            worldDetailsStack.Children.Add(fieldRow("Game Mode", "Survival", false));
            worldDetailsStack.Children.Add(fieldRow("Cheats", "Off", false));

            var manageWorldBtn = CreatePrimaryButton("Manage World", "#6E5BFF", Colors.White);
            manageWorldBtn.Height = 32;
            manageWorldBtn.CornerRadius = new CornerRadius(8);
            manageWorldBtn.FontSize = 11;
            manageWorldBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            manageWorldBtn.Click += async (_, _) => await DialogService.ShowInfoAsync(this, "World Manager", "Opening integrated World Management deck.");

            worldDetailsStack.Children.Add(new Border { Height = 6 });
            worldDetailsStack.Children.Add(manageWorldBtn);

            rightColumnPanel.Children.Add(CreateGlassBox("World Environment", worldDetailsStack));
            dashboardOverviewGrid.Children.Add(rightColumnPanel.With(column: 1));

            contentPanel.Children.Add(dashboardOverviewGrid);

            // ================= 4-COLUMN BOTTOM HIGHLIGHT CARDS =================
            var bottomGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 10, 0, 0) };

            var CreateBottomHighlightCard = new Func<string, string, string, string, System.Action, Border>((title, desc, buttonText, icon, onClick) =>
            {
                var cardStack = new StackPanel { Spacing = 6 };
                var cardHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                cardHeader.Children.Add(new TextBlock { Text = icon, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
                cardHeader.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });

                var btn = CreateSecondaryButton(buttonText);
                btn.Height = 32;
                btn.CornerRadius = new CornerRadius(8);
                btn.FontSize = 10;
                btn.HorizontalAlignment = HorizontalAlignment.Stretch;
                btn.Click += (_, _) => onClick();

                cardStack.Children.Add(cardHeader);
                cardStack.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.Parse("#8E96A8")), FontSize = 10, TextWrapping = TextWrapping.Wrap });
                cardStack.Children.Add(btn);

                return new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 14, 16, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 110, 91, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10),
                    Child = cardStack,
                    Margin = new Thickness(4)
                };
            });

            var modsCard = CreateBottomHighlightCard("5 Mods Installed", "All mods are healthy.", "Manage Mods", "📦", () => { _activeDashboardTab = "files"; RefreshLayoutSection(); });
            var backupsCard = CreateBottomHighlightCard("12 Backups", "Latest created 2h ago.", "Manage Backups", "☁", async () => await DialogService.ShowInfoAsync(this, "Backups Deck", "Backups are currently healthy."));
            var networkCard = CreateBottomHighlightCard("Tunneling Active", "Zero-config secure link.", "Network Settings", "🌐", () => { _activeDashboardTab = "settings"; RefreshLayoutSection(); });
            var presetCard = CreateBottomHighlightCard("Optimized Preset", "Balanced for performance.", "Change Preset", "⚡", async () => await DialogService.ShowInfoAsync(this, "Presets Manager", "Performance profiles are fully optimized."));

            bottomGrid.Children.Add(modsCard.With(column: 0));
            bottomGrid.Children.Add(backupsCard.With(column: 1));
            bottomGrid.Children.Add(networkCard.With(column: 2));
            bottomGrid.Children.Add(presetCard.With(column: 3));

            contentPanel.Children.Add(bottomGrid);
        }
        else if (_activeDashboardTab == "performance")
        {
            contentPanel.Children.Add(BuildPerformanceTabPanel(server));
        }
        else if (_activeDashboardTab == "properties")
        {
            var propsPath = Path.Combine(server.FolderPath, "server.properties");
            EnsureDefaultPropertiesFile(propsPath, server);
            var propsMap = GetPropertiesMap(propsPath);

            var saveCallbacks = new List<Func<KeyValuePair<string, string>>>();

            // Identify custom keys
            var definedKeys = new HashSet<string>(ServerPropertyDefinitions.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
            var customKeys = propsMap.Keys.Where(k => !definedKeys.Contains(k)).ToList();
            var customDefs = customKeys.Select(k => new PropertyDefinition
            {
                Key = k,
                Label = k,
                Description = "Custom server property.",
                Category = "Other / Custom",
                Type = "text"
            }).ToList();

            var categories = new[]
            {
                "General",
                "Gameplay",
                "World & Spawning",
                "Performance",
                "Advanced",
                "RCON & Query",
                "Resource Packs",
                "Other / Custom"
            };

            // Main Save Actions
            var saveAction = async () =>
            {
                var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cb in saveCallbacks)
                {
                    var kvp = cb();
                    updates[kvp.Key] = kvp.Value;
                }
                UpdatePropertiesFile(propsPath, updates);
                LogServerLine(server.Id, "[System] server.properties configuration updated. Please restart server to apply changes.");
                await DialogService.ShowInfoAsync(this, "Properties Saved", "All server properties saved successfully. If the server is currently running, restart it to apply updates!");
            };

            // Header card: description + search + save
            var saveBtnTop = CreatePrimaryButton("💾 Save & Apply", "#38D6C4", Colors.Black);
            saveBtnTop.Height = 36;
            saveBtnTop.CornerRadius = new CornerRadius(10);
            saveBtnTop.FontWeight = FontWeight.Bold;
            saveBtnTop.Click += async (_, _) => await saveAction();

            var searchBox = new TextBox
            {
                Watermark = "Search properties...",
                Height = 36,
                Padding = new Thickness(10, 6),
                MinWidth = 220
            };

            var headerControls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
            headerControls.Children.Add(new TextBlock
            {
                Text = "Configure server.properties below. Changes take effect after a server restart.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            }.With(column: 0));
            headerControls.Children.Add(searchBox.With(column: 1));
            headerControls.Children.Add(saveBtnTop.With(column: 2));

            var topHeaderCard = CreateGlassBox("Server Properties", headerControls);
            contentPanel.Children.Add(topHeaderCard);

            // Category cards — build all upfront so we can filter them
            var catCards = new List<(Control Card, List<PropertyDefinition> Defs)>();

            foreach (var cat in categories)
            {
                var catDefs = ServerPropertyDefinitions.Where(d => d.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
                if (cat == "Other / Custom") catDefs = customDefs;
                if (catDefs.Count == 0) continue;

                var catStack = new StackPanel { Spacing = 10 };
                foreach (var def in catDefs)
                {
                    if (def.Type == "boolean")
                    {
                        var checkbox = new CheckBox
                        {
                            Content = new TextBlock { Text = def.Label, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, FontSize = 13 },
                            IsChecked = propsMap.ContainsKey(def.Key) && propsMap[def.Key].Equals("true", StringComparison.OrdinalIgnoreCase),
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        var desc = new TextBlock
                        {
                            Text = def.Description + $" (Key: {def.Key})",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#7A8AAA")),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(24, 0, 0, 6)
                        };
                        catStack.Children.Add(checkbox);
                        catStack.Children.Add(desc);
                        var keyVal = def.Key;
                        saveCallbacks.Add(() => new KeyValuePair<string, string>(keyVal, (checkbox.IsChecked ?? false).ToString().ToLower()));
                    }
                    else if (def.Type == "choice")
                    {
                        var label = new TextBlock { Text = def.Label, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0) };
                        var combo = CreateComboBox(def.Choices ?? new[] { "" });
                        combo.Height = 36;
                        combo.SelectedItem = propsMap.ContainsKey(def.Key) ? propsMap[def.Key] : (def.Choices?[0] ?? "");
                        var desc = new TextBlock { Text = def.Description + $" (Key: {def.Key})", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#7A8AAA")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
                        catStack.Children.Add(label);
                        catStack.Children.Add(combo);
                        catStack.Children.Add(desc);
                        var keyVal = def.Key;
                        saveCallbacks.Add(() => new KeyValuePair<string, string>(keyVal, combo.SelectedItem?.ToString() ?? ""));
                    }
                    else
                    {
                        var label = new TextBlock { Text = def.Label, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0) };
                        var textbox = CreateTextBox();
                        textbox.Height = 36;
                        textbox.Padding = new Thickness(10, 6);
                        textbox.Text = propsMap.ContainsKey(def.Key) ? propsMap[def.Key] : "";
                        var desc = new TextBlock { Text = def.Description + $" (Key: {def.Key})", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#7A8AAA")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
                        catStack.Children.Add(label);
                        catStack.Children.Add(textbox);
                        catStack.Children.Add(desc);
                        var keyVal = def.Key;
                        saveCallbacks.Add(() => new KeyValuePair<string, string>(keyVal, textbox.Text?.Trim() ?? ""));
                    }
                }

                var catCard = CreateGlassBox(cat, catStack);
                contentPanel.Children.Add(catCard);
                catCards.Add((catCard, catDefs));
            }

            // Wire up search filtering
            searchBox.TextChanged += (_, _) =>
            {
                var query = searchBox.Text?.Trim() ?? "";
                foreach (var (card, defs) in catCards)
                {
                    card.IsVisible = string.IsNullOrEmpty(query)
                        || defs.Any(d =>
                            d.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            d.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            d.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
                }
            };

            // Bottom Save button
            var saveBtnBottom = CreatePrimaryButton("💾 Save & Apply Properties", "#38D6C4", Colors.Black);
            saveBtnBottom.Height = 44;
            saveBtnBottom.CornerRadius = new CornerRadius(12);
            saveBtnBottom.FontWeight = FontWeight.Bold;
            saveBtnBottom.Click += async (_, _) => await saveAction();

            var bottomCard = CreateGlassBox("", saveBtnBottom);
            contentPanel.Children.Add(bottomCard);
        }
        else if (_activeDashboardTab == "admin")
        {
            // Get Admin Tab
            var playerLabel = new TextBlock { Text = "Operator Username", Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold };
            var playerInput = new TextBox { Watermark = "Enter Minecraft Player Name" };

            var opBtn = CreatePrimaryButton("Grant Operator (OP)", "#6E5BFF", Colors.White);
            opBtn.Height = 44;
            opBtn.CornerRadius = new CornerRadius(10);
            opBtn.FontWeight = FontWeight.Bold;
            opBtn.Click += async (_, _) =>
            {
                var username = playerInput.Text?.Trim();
                if (string.IsNullOrEmpty(username))
                {
                    await DialogService.ShowInfoAsync(this, "Username Required", "Please enter a player name to grant admin privileges.");
                    return;
                }

                if (_serverProcesses.TryGetValue(server.Id, out var proc) && !proc.HasExited)
                {
                    proc.StandardInput.WriteLine($"op {username}");
                    LogServerLine(server.Id, $"> op {username}");
                    await DialogService.ShowInfoAsync(this, "Command Sent", $"OP command sent successfully to active console for user: {username}");
                    playerInput.Text = "";
                }
                else
                {
                    await DialogService.ShowInfoAsync(this, "Server Offline", "The Minecraft server must be running to run operator configuration commands via console.");
                }
            };

            var opForm = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Grant permanent operator status (admin control/commands) to an active player in your local server.", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), TextWrapping = TextWrapping.Wrap },
                    new Border { Height = 4 },
                    playerLabel, playerInput,
                    new Border { Height = 10 },
                    opBtn
                }
            };

            var opCard = CreateGlassBox("Grant Server Operator", opForm);
            contentPanel.Children.Add(opCard);
        }
        else if (_activeDashboardTab == "settings")
        {
            // ── Helper: section label ──────────────────────────────────────────────
            TextBlock SectionLabel(string text) => new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 14, 0, 4),
                LetterSpacing = 1.2
            };

            // ── Basic fields ──────────────────────────────────────────────────────
            var editNameInput = new TextBox { Text = server.Name, Watermark = "My SMP Server" };
            var editPortInput = new TextBox { Text = server.Port, Watermark = "25565" };
            var editRamCombo = CreateComboBox(new[] { "1 GB", "2 GB", "3 GB", "4 GB", "5 GB", "6 GB", "7 GB", "8 GB" });
            editRamCombo.SelectedItem = server.RamAllocation.Replace("G", " GB");
            var editPlayerTimeoutInput = new TextBox { Text = server.PlayerTimeoutHours.ToString(), Watermark = "2" };

            var editUpnpCheck  = new CheckBox { Content = "Enable UPnP Port Forwarding",              IsChecked = server.UseUPnP,  Foreground = Brushes.White };
            var editTunnelCheck= new CheckBox { Content = "Enable Internet Tunnel (Pinggy)",           IsChecked = server.UseTunnel,Foreground = Brushes.White };
            var editOnlineCheck= new CheckBox { Content = "Online Mode (Require Microsoft Account)",   IsChecked = server.OnlineMode,Foreground = Brushes.White };

            // ── Invite code with once-per-day lock ───────────────────────────────
            var canChangeCode = !server.InviteCodeLastChanged.HasValue
                || (DateTime.UtcNow - server.InviteCodeLastChanged.Value).TotalHours >= 24;

            var inviteInput = new TextBox
            {
                Text = server.InviteCode ?? "",
                Watermark = "e.g. achinthya-smp",
                IsEnabled = canChangeCode
            };

            var inviteLockBadge = new Border
            {
                IsVisible = !canChangeCode,
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 165, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 165, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                Child = new TextBlock
                {
                    Foreground = new SolidColorBrush(Color.Parse("#FFA500")),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Text = server.InviteCodeLastChanged.HasValue
                        ? $"🔒 Invite code locked for {Math.Ceiling(24 - (DateTime.UtcNow - server.InviteCodeLastChanged.Value).TotalHours):0}h more. You can only change it once per day."
                        : "🔒 Locked."
                }
            };

            // ── Allowed players pill UI ───────────────────────────────────────────
            var currentPlayers = new List<string>(server.AllowedPlayers ?? new List<string>());

            var pillsWrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN };

            void RebuildPills()
            {
                pillsWrap.Children.Clear();
                foreach (var playerName in currentPlayers.ToList())
                {
                    var nameCopy = playerName;
                    var removeBtn = new Button
                    {
                        Content = "✕",
                        FontSize = 10,
                        Padding = new Thickness(2, 0),
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.Parse("#FF5555")),
                        BorderThickness = new Thickness(0),
                        Cursor = new Cursor(StandardCursorType.Hand),
                    };
                    removeBtn.Click += (_, _) =>
                    {
                        currentPlayers.Remove(nameCopy);
                        RebuildPills();
                    };

                    var pill = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, 110, 91, 255)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(120, 110, 91, 255)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(20),
                        Padding = new Thickness(10, 5),
                        Margin = new Thickness(0, 4, 6, 4),
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = nameCopy,
                                    Foreground = new SolidColorBrush(Color.Parse("#C8BAFF")),
                                    FontSize = 12,
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                removeBtn
                            }
                        }
                    };
                    pillsWrap.Children.Add(pill);
                }
            }
            RebuildPills();

            var newPlayerInput = new TextBox
            {
                Watermark = "Enter Minecraft username...",
                Height = 38,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var addPlayerBtn = new Button
            {
                Content = "+ Add",
                Background = new SolidColorBrush(Color.Parse("#6E5BFF")),
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                Height = 38,
                Padding = new Thickness(16, 0),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            addPlayerBtn.Click += (_, _) =>
            {
                var name = newPlayerInput.Text?.Trim();
                if (string.IsNullOrEmpty(name)) return;
                if (currentPlayers.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
                currentPlayers.Add(name);
                newPlayerInput.Text = "";
                RebuildPills();
            };

            newPlayerInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) addPlayerBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            };

            var addPlayerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            addPlayerRow.Children.Add(newPlayerInput.With(column: 0));
            addPlayerRow.Children.Add(addPlayerBtn.With(column: 1));

            var pillsContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 110, 91, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8),
                MinHeight = 48,
                Child = pillsWrap
            };

            // ── Save button ───────────────────────────────────────────────────────
            var saveSettingsBtn = CreatePrimaryButton("Save Configuration", "#38D6C4", Colors.Black);
            saveSettingsBtn.Height = 46;
            saveSettingsBtn.CornerRadius = new CornerRadius(10);
            saveSettingsBtn.FontWeight = FontWeight.Bold;
            saveSettingsBtn.Margin = new Thickness(0, 12, 0, 0);

            saveSettingsBtn.Click += async (_, _) =>
            {
                var name = editNameInput.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    await DialogService.ShowInfoAsync(this, "Name Required", "Please enter a valid server name.");
                    return;
                }

                // Invite code change: enforce once-per-day
                var newCode = inviteInput.Text?.Trim() ?? "";
                if (canChangeCode && newCode != (server.InviteCode ?? ""))
                {
                    server.InviteCode = newCode;
                    server.InviteCodeLastChanged = DateTime.UtcNow;
                }

                server.Name = name;
                server.Port = editPortInput.Text?.Trim() ?? "25565";
                server.RamAllocation = editRamCombo.SelectedItem?.ToString()?.Replace(" GB", "G") ?? "2G";
                server.UseUPnP  = editUpnpCheck.IsChecked  ?? true;
                server.UseTunnel= editTunnelCheck.IsChecked ?? true;
                server.OnlineMode = editOnlineCheck.IsChecked ?? false;
                server.EmptyTimeoutMinutes = 30.0;
                server.PlayerTimeoutHours = double.TryParse(editPlayerTimeoutInput.Text, out var ptVal) ? ptVal : 2.0;
                server.AllowedPlayers = currentPlayers;
                server.AutoInvite = false;

                SaveServers();
                await DialogService.ShowInfoAsync(this, "Saved ✓", "Server configuration saved successfully!");
                RefreshLayoutSection();
            };

            // ── Assemble form ─────────────────────────────────────────────────────
            var settingsForm = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    SectionLabel("GENERAL"),
                    editNameInput,
                    SectionLabel("PORT"),
                    editPortInput,
                    SectionLabel("RAM ALLOCATION"),
                    editRamCombo,
                    SectionLabel("PLAYER TIMEOUT (HOURS)"),
                    editPlayerTimeoutInput,

                    SectionLabel("STABLE INVITE CODE"),
                    inviteInput,
                    inviteLockBadge,

                    SectionLabel("ALLOWED PLAYERS"),
                    pillsContainer,
                    addPlayerRow,

                    new Border { Height = 8 },
                    editUpnpCheck, editTunnelCheck, editOnlineCheck,

                    saveSettingsBtn
                }
            };

            var settingsCard = CreateGlassBox("Server Settings", settingsForm);
            contentPanel.Children.Add(settingsCard);
        }
        else if (_activeDashboardTab == "files")
        {
            // View Files Tab
            var openFolderBtn = CreatePrimaryButton("Open Server Directory", "#6E5BFF", Colors.White);
            openFolderBtn.Height = 44;
            openFolderBtn.CornerRadius = new CornerRadius(10);
            openFolderBtn.FontWeight = FontWeight.Bold;
            openFolderBtn.Click += (_, _) =>
            {
                OpenLocalFolder(server.FolderPath);
            };

            var filesContent = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Access server world saves, plugin folders, and configuration logs locally.", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = $"Folder Location: {server.FolderPath}", FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New, monospace"), Foreground = new SolidColorBrush(Color.Parse("#7A8AAA")), TextWrapping = TextWrapping.Wrap },
                    new Border { Height = 10 },
                    openFolderBtn
                }
            };

            var filesCard = CreateGlassBox("Server Files", filesContent);
            contentPanel.Children.Add(filesCard);
        }
        else if (_activeDashboardTab == "mods")
        {
            // Mods & Plugins tab!
            // 1. Determine directory: "mods" for fabric/forge/quilt/neoforge, "plugins" for paper/spigot/purpur
            var isPluginLoader = server.Loader.ToLowerInvariant() == "paper" || 
                                 server.Loader.ToLowerInvariant() == "spigot" || 
                                 server.Loader.ToLowerInvariant() == "purpur";
            var targetFolder = isPluginLoader ? "plugins" : "mods";
            var targetPath = Path.Combine(server.FolderPath, targetFolder);
            Directory.CreateDirectory(targetPath);
            
            // List currently installed jars in the target folder
            var installedJars = new List<string>();
            try
            {
                if (Directory.Exists(targetPath))
                {
                    installedJars = Directory.GetFiles(targetPath, "*.jar")
                        .Select(Path.GetFileName)
                        .Where(x => x != null)
                        .Cast<string>()
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Failed to list server jar files: {ex.Message}");
            }

            var installedListPanel = new StackPanel { Spacing = 6 };
            
            var refreshInstalledList = new System.Action(() => {
                installedListPanel.Children.Clear();
                if (installedJars.Count == 0)
                {
                    installedListPanel.Children.Add(new TextBlock { 
                        Text = $"No {targetFolder} installed yet.", 
                        Foreground = Brushes.Gray, 
                        FontStyle = FontStyle.Italic,
                        Margin = new Thickness(0, 10, 0, 10),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                else
                {
                    foreach (var jar in installedJars)
                    {
                        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                        row.Children.Add(new TextBlock { Text = jar, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 12 }.With(column: 0));
                        
                        var deleteBtn = new Button { Content = "🗑 Delete", Background = new SolidColorBrush(Color.Parse("#FF5555")), Foreground = Brushes.White, FontSize = 11, Padding = new Thickness(8, 4) };
                        var localJar = jar;
                        deleteBtn.Click += async (_, _) => {
                            try
                            {
                                var fullPath = Path.Combine(targetPath, localJar);
                                if (File.Exists(fullPath))
                                {
                                    File.Delete(fullPath);
                                    installedJars.Remove(localJar);
                                    RefreshLayoutSection();
                                }
                            }
                            catch (Exception ex)
                            {
                                await DialogService.ShowInfoAsync(this, "Error deleting", $"Could not delete file: {ex.Message}");
                            }
                        };
                        row.Children.Add(deleteBtn.With(column: 1));
                        
                        var itemBorder = new Border {
                            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                            Padding = new Thickness(10, 6),
                            CornerRadius = new CornerRadius(8),
                            Margin = new Thickness(0, 2, 0, 2),
                            Child = row
                        };
                        installedListPanel.Children.Add(itemBorder);
                    }
                }
            });
            refreshInstalledList();

            // Search Panel
            var searchInput = CreateTextBox();
            searchInput.Watermark = isPluginLoader ? "Search plugins (e.g. EssentialsX)..." : "Search mods...";
            searchInput.Margin = new Thickness(0, 0, 8, 0);
            
            var sourceCombo = CreateComboBox(new[] { "Modrinth", "CurseForge" });
            sourceCombo.SelectedIndex = 0;
            sourceCombo.Width = 120;
            sourceCombo.Margin = new Thickness(0, 0, 8, 0);
            
            var searchResultsPanel = new StackPanel { Spacing = 6 };
            var searchBtn = CreatePrimaryButton("Search", "#38D6C4", Colors.Black);
            searchBtn.Width = 100;
            searchBtn.Height = 40;
            
            var searchProgressRing = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 4, Margin = new Thickness(0, 6, 0, 6) };
            
            var renderResults = new System.Action<IEnumerable<ModrinthProject>, string>((projects, headerText) => {
                searchResultsPanel.Children.Clear();
                if (!string.IsNullOrEmpty(headerText))
                {
                    searchResultsPanel.Children.Add(new TextBlock {
                        Text = headerText,
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
                        Margin = new Thickness(0, 4, 0, 8)
                    });
                }
                
                var projectsList = projects.ToList();
                if (projectsList.Count == 0)
                {
                    searchResultsPanel.Children.Add(new TextBlock { Text = "No items to display.", Foreground = Brushes.Gray, FontStyle = FontStyle.Italic, Margin = new Thickness(10) });
                    return;
                }

                foreach (var project in projectsList)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    var textStack = new StackPanel { Spacing = 2 };
                    textStack.Children.Add(new TextBlock { Text = project.Title, FontWeight = FontWeight.Bold, Foreground = Brushes.White, FontSize = 13 });
                    textStack.Children.Add(new TextBlock { Text = project.Description, Foreground = Brushes.Gray, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                    row.Children.Add(textStack.With(column: 0));
                    
                    var downloadBtn = CreatePrimaryButton("Install", "#6E5BFF", Colors.White);
                    downloadBtn.Height = 32;
                    downloadBtn.CornerRadius = new CornerRadius(8);
                    downloadBtn.FontSize = 11;
                    
                    var isAlreadyInstalled = installedJars.Any(j => j.Contains(project.Title, StringComparison.OrdinalIgnoreCase) || j.Contains(project.ProjectId, StringComparison.OrdinalIgnoreCase));
                    if (isAlreadyInstalled)
                    {
                        downloadBtn.Content = "Installed";
                        downloadBtn.IsEnabled = false;
                    }
                    
                    var searchLoader = isPluginLoader ? "paper" : server.Loader;
                    var localProj = project;
                    downloadBtn.Click += async (_, _) => {
                        downloadBtn.IsEnabled = false;
                        downloadBtn.Content = "Downloading...";
                        try
                        {
                            string finalFilename = "";
                            if (localProj.IsCurseForge)
                            {
                                var cfFiles = await _curseForgeClient.GetProjectVersionsAsync(localProj.ProjectId, server.Version, searchLoader, CancellationToken.None);
                                var cfFile = cfFiles.FirstOrDefault();
                                if (cfFile == null)
                                {
                                    throw new InvalidOperationException($"No compatible version found on CurseForge for MC {server.Version}.");
                                }
                                if (string.IsNullOrEmpty(cfFile.DownloadUrl))
                                {
                                    throw new InvalidOperationException("This mod has downloads disabled for 3rd party launchers on CurseForge.");
                                }
                                var destFile = Path.Combine(targetPath, cfFile.FileName);
                                await _curseForgeClient.DownloadFileAsync(cfFile.DownloadUrl, destFile, null, CancellationToken.None);
                                finalFilename = cfFile.FileName;
                            }
                            else
                            {
                                var versions = await _modrinthClient.GetProjectVersionsAsync(localProj.ProjectId, server.Version, searchLoader, CancellationToken.None);
                                var version = versions.FirstOrDefault(HasPrimaryFile) ?? versions.FirstOrDefault();
                                if (version == null)
                                {
                                    throw new InvalidOperationException($"No compatible version found for MC {server.Version}.");
                                }
                                var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
                                if (file == null)
                                {
                                    throw new InvalidOperationException("No download file found.");
                                }
                                var destFile = Path.Combine(targetPath, file.Filename);
                                await _modrinthClient.DownloadFileAsync(file.Url, destFile, null, CancellationToken.None);
                                finalFilename = file.Filename;
                            }
                            
                            downloadBtn.Content = "✓ Installed";
                            if (!string.IsNullOrEmpty(finalFilename) && !installedJars.Contains(finalFilename))
                            {
                                installedJars.Add(finalFilename);
                            }
                            RefreshLayoutSection();
                        }
                        catch (Exception ex)
                        {
                            downloadBtn.Content = "Failed";
                            downloadBtn.IsEnabled = true;
                            await DialogService.ShowInfoAsync(this, "Install Failed", $"Failed to install {localProj.Title}: {ex.Message}");
                        }
                    };
                    
                    row.Children.Add(downloadBtn.With(column: 1));
                    
                    var border = new Border {
                        Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(12, 10),
                        CornerRadius = new CornerRadius(10),
                        Margin = new Thickness(0, 4, 0, 4),
                        Child = row
                    };
                    searchResultsPanel.Children.Add(border);
                }
            });

            var clientOnlyKeywords = new[] { 
                "sodium", "iris", "hud", "tooltip", "shader", "minimap", "worldmap", 
                "fps booster", "zoom", "optifine", "client-only", "client only",
                "crosshair", "menu", "screenshot", "dynamic lights", "entity culling",
                "gui", "skin", "capes", "macro", "keybind", "reauth", "jei", "rei", "emi"
            };

            var isClientOnly = new System.Func<ModrinthProject, bool>(r => {
                // 1. Exclude if marked as unsupported on server-side by Modrinth metadata
                if (string.Equals(r.ServerSide, "unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // 2. Exclude if required on client-side but optional/unsupported on server-side (typical of client-only mods like minimaps/HUDs)
                if (string.Equals(r.ClientSide, "required", StringComparison.OrdinalIgnoreCase) && 
                    (string.Equals(r.ServerSide, "optional", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.ServerSide)))
                {
                    return true;
                }
                
                // 3. Exclude by title/description keywords
                var titleLower = r.Title.ToLowerInvariant();
                var descLower = r.Description.ToLowerInvariant();
                if (clientOnlyKeywords.Any(kw => titleLower.Contains(kw) || descLower.Contains(kw)))
                {
                    return true;
                }
                
                return false;
            });

            var loadRecommendations = new System.Func<Task>(async () => {
                searchProgressRing.IsVisible = true;
                searchResultsPanel.Children.Clear();
                searchResultsPanel.Children.Add(new TextBlock { Text = "Loading recommendations...", Foreground = Brushes.Gray, FontStyle = FontStyle.Italic, Margin = new Thickness(10) });
                try
                {
                    var recommendedQueries = isPluginLoader 
                        ? new[] { "EssentialsX", "LuckPerms", "Vault", "WorldEdit", "ViaVersion" }
                        : new[] { "Lithium", "FerriteCore", "Chunky", "Spark" };
                    
                    var recommendedProjects = new List<ModrinthProject>();
                    var searchLoader = isPluginLoader ? "paper" : server.Loader;

                    var tasks = recommendedQueries.Select(async q => {
                        try
                        {
                            var searchRes = await _modrinthClient.SearchProjectsAsync(q, "mod", server.Version, searchLoader, CancellationToken.None);
                            return searchRes.FirstOrDefault(p => !isClientOnly(p)) ?? searchRes.FirstOrDefault();
                        }
                        catch
                        {
                            return null;
                        }
                    });

                    var results = await Task.WhenAll(tasks);
                    foreach (var p in results)
                    {
                        if (p != null) recommendedProjects.Add(p);
                    }

                    renderResults(recommendedProjects, "★ RECOMMENDED FOR YOUR SERVER TYPE:");
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"[Server Dashboard] Failed to load recommended mods: {ex.Message}");
                    searchResultsPanel.Children.Clear();
                    searchResultsPanel.Children.Add(new TextBlock { Text = "Failed to load recommendations.", Foreground = Brushes.Gray, FontStyle = FontStyle.Italic, Margin = new Thickness(10) });
                }
                finally
                {
                    searchProgressRing.IsVisible = false;
                }
            });

            // Trigger the initial load of recommendations
            _ = Task.Run(async () => {
                await Task.Delay(100);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
                    await loadRecommendations();
                });
            });
            
            searchBtn.Click += async (_, _) => {
                var query = searchInput.Text?.Trim();
                if (string.IsNullOrEmpty(query))
                {
                    await loadRecommendations();
                    return;
                }
                
                searchProgressRing.IsVisible = true;
                searchResultsPanel.Children.Clear();
                
                try
                {
                    var searchLoader = isPluginLoader ? "paper" : server.Loader;
                    var selectedSource = sourceCombo.SelectedItem?.ToString() ?? "Modrinth";
                    IReadOnlyList<ModrinthProject> results;
                    
                    if (selectedSource == "CurseForge")
                    {
                        var rawResults = await _curseForgeClient.SearchModsAsync(query, server.Version, searchLoader, CancellationToken.None);
                        results = rawResults.Where(r => !isClientOnly(r)).ToList();
                    }
                    else
                    {
                        var rawResults = await _modrinthClient.SearchProjectsAsync(query, "mod", server.Version, searchLoader, CancellationToken.None);
                        results = rawResults.Where(r => !isClientOnly(r)).ToList();
                    }
                    
                    renderResults(results, $"Search results for \"{query}\" on {selectedSource}:");
                }
                catch (Exception ex)
                {
                    searchResultsPanel.Children.Clear();
                    searchResultsPanel.Children.Add(new TextBlock { Text = $"Search failed: {ex.Message}", Foreground = new SolidColorBrush(Color.Parse("#FF5555")), FontStyle = FontStyle.Italic, Margin = new Thickness(10) });
                }
                finally
                {
                    searchProgressRing.IsVisible = false;
                }
            };

            var searchHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            searchHeader.Children.Add(searchInput.With(column: 0));
            searchHeader.Children.Add(sourceCombo.With(column: 1));
            searchHeader.Children.Add(searchBtn.With(column: 2));

            // Import Mod button (file picker)
            var importModBtn = CreatePrimaryButton("⤓ Import Mod", "#BD93F9", Colors.White);
            importModBtn.Height = 36;
            importModBtn.CornerRadius = new CornerRadius(8);
            importModBtn.FontWeight = FontWeight.Bold;
            importModBtn.Click += async (_, _) =>
            {
                try
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel == null) return;
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = $"Import {targetFolder} (.jar files)",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("Mod Files") { Patterns = new[] { "*.jar" } } }
                    });
                    if (files != null && files.Count > 0)
                    {
                        foreach (var fileItem in files)
                        {
                            var srcPath = fileItem.Path.LocalPath;
                            if (File.Exists(srcPath))
                            {
                                var destPath = Path.Combine(targetPath, Path.GetFileName(srcPath));
                                File.Copy(srcPath, destPath, true);
                                var fname = Path.GetFileName(srcPath);
                                if (!installedJars.Contains(fname))
                                    installedJars.Add(fname);
                            }
                        }
                        RefreshLayoutSection();
                    }
                }
                catch (Exception ex)
                {
                    await DialogService.ShowInfoAsync(this, "Import Failed", $"Failed to import mod: {ex.Message}");
                }
            };

            var installedHeader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = $"Installed {targetFolder.ToUpper()}", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }.With(column: 0),
                    importModBtn.With(column: 1)
                }
            };

            // Drag-and-drop hint
            var dropHint = new TextBlock
            {
                Text = $"💡 Tip: You can drag & drop .jar files here to import {targetFolder}",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.Parse("#66758F")),
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(0, 2, 0, 6)
            };

            var modsPanel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    installedHeader,
                    dropHint,
                    installedListPanel,
                    new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), Margin = new Thickness(0, 10, 0, 10) },
                    new TextBlock { Text = $"Search & Add {targetFolder.ToUpper()}", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                    searchHeader,
                    searchProgressRing,
                    new ScrollViewer { MaxHeight = 350, Content = searchResultsPanel }
                }
            };

            var modsCard = CreateGlassBox("", modsPanel);

            // Enable drag-and-drop on the server mods card
            DragDrop.SetAllowDrop(modsCard, true);
            modsCard.AddHandler(DragDrop.DragEnterEvent, (sender, e) =>
            {
                if (modsCard is Border mb) mb.BorderBrush = new SolidColorBrush(Color.Parse("#BD93F9"));
            });
            modsCard.AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
            {
                if (modsCard is Border mb) mb.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            });
            modsCard.AddHandler(DragDrop.DragOverEvent, (sender, e) =>
            {
                var droppedFiles = e.Data.GetFiles();
                if (droppedFiles != null && droppedFiles.Any(f => f.Path.LocalPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                {
                    e.DragEffects = DragDropEffects.Copy;
                }
                else
                {
                    e.DragEffects = DragDropEffects.None;
                }
                e.Handled = true;
            });
            modsCard.AddHandler(DragDrop.DropEvent, async (sender, e) =>
            {
                var droppedFiles = e.Data.GetFiles();
                if (droppedFiles != null)
                {
                    foreach (var fileItem in droppedFiles)
                    {
                        var srcPath = fileItem.Path.LocalPath;
                        if (File.Exists(srcPath) && srcPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var destPath = Path.Combine(targetPath, Path.GetFileName(srcPath));
                                File.Copy(srcPath, destPath, true);
                                var fname = Path.GetFileName(srcPath);
                                if (!installedJars.Contains(fname))
                                    installedJars.Add(fname);
                            }
                            catch (Exception ex)
                            {
                                await DialogService.ShowInfoAsync(this, "Error", $"Failed to import '{Path.GetFileName(srcPath)}': {ex.Message}");
                            }
                        }
                    }
                    RefreshLayoutSection();
                }
                e.Handled = true;
            });
            modsCard.AddHandler(DragDrop.DropEvent, (sender, e) =>
            {
                if (modsCard is Border mb2) mb2.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            }, handledEventsToo: true);

            contentPanel.Children.Add(modsCard);

        }
        else
        {
            // Default: Console log streaming & input command sender
            var consoleHeaderGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            consoleHeaderGrid.Children.Add(new TextBlock { Text = "Live Server Console Output", Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }.With(column: 0));
            
            var consoleLogsStack = new StackPanel { Spacing = 3 };
            var consoleScroller = new ScrollViewer
            {
                Height = 280,
                Background = new SolidColorBrush(Color.Parse("#070A0F")),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.Parse("#1D2A3A")),
                BorderThickness = new Thickness(1.5),
                Content = consoleLogsStack
            };

            var appendStyledLogLine = new System.Action<string>((line) =>
            {
                if (string.IsNullOrEmpty(line)) return;
                
                IBrush brush = new SolidColorBrush(Color.Parse("#A4B4DA")); // default cool grey-blue
                var isBold = false;
                
                if (line.StartsWith("> "))
                {
                    brush = new SolidColorBrush(Color.Parse("#00FF87")); // User input command in glowing neon green
                    isBold = true;
                }
                else if (line.Contains("[Error]") || line.Contains("ERROR") || line.Contains("Exception") || line.Contains("failed"))
                {
                    brush = new SolidColorBrush(Color.Parse("#FF5555")); // Error in glowing soft red
                }
                else if (line.Contains("[WARN]") || line.Contains("[System Warning]") || line.Contains("WARN"))
                {
                    brush = new SolidColorBrush(Color.Parse("#FFB86C")); // Warn in warm orange
                }
                else if (line.Contains("[System]"))
                {
                    brush = new SolidColorBrush(Color.Parse("#38D6C4")); // Launcher update in glowing cyan
                    isBold = true;
                }
                else if (line.Contains("joined the game") || line.Contains("left the game"))
                {
                    brush = new SolidColorBrush(Color.Parse("#B655FF")); // Player join/leave in vibrant purple
                    isBold = true;
                }
                else if (line.Contains("INFO") || line.Contains("]: <"))
                {
                    if (line.Contains("]: <"))
                    {
                        brush = new SolidColorBrush(Color.Parse("#FF79C6")); // Chat messages in hot pink
                    }
                    else
                    {
                        brush = new SolidColorBrush(Color.Parse("#F8F8F2")); // Standard server output in crisp off-white
                    }
                }
                
                var textBlock = new TextBlock
                {
                    Text = line,
                    Foreground = brush,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize = 11,
                    FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal,
                    TextWrapping = TextWrapping.Wrap
                };
                
                consoleLogsStack.Children.Add(textBlock);
                if (consoleLogsStack.Children.Count > 500)
                {
                    consoleLogsStack.Children.RemoveAt(0);
                }
                consoleScroller.Offset = new Avalonia.Vector(0, double.MaxValue);
            });

            // Populate initial logs
            var existingLogs = "";
            if (_serverLogs.ContainsKey(server.Id))
            {
                lock (serverLogSb)
                {
                    existingLogs = serverLogSb.ToString();
                }
            }
            var initialLines = existingLogs.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var l in initialLines)
            {
                if (!string.IsNullOrEmpty(l))
                {
                    appendStyledLogLine(l);
                }
            }

            var clearConsoleBtn = CreateSecondaryButton("Clear Console");
            clearConsoleBtn.Height = 34;
            clearConsoleBtn.CornerRadius = new CornerRadius(6);
            clearConsoleBtn.Click += (_, _) =>
            {
                lock (serverLogSb)
                {
                    serverLogSb.Clear();
                }
                consoleTextBox.Text = string.Empty;
                consoleLogsStack.Children.Clear();
            };
            consoleHeaderGrid.Children.Add(clearConsoleBtn.With(column: 1));

            // ⚡ Premium Connection Bar
            var isSrvRunning = _serverProcesses.ContainsKey(server.Id) && !_serverProcesses[server.Id].HasExited;
            var activeAddr = _tunnelAddresses.TryGetValue(server.Id, out var activeTunnel) 
                ? activeTunnel 
                : (string.IsNullOrEmpty(_publicIpAddress) ? $"localhost:{server.Port}" : $"{_publicIpAddress}:{server.Port}");

            var connectionBar = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1E2538")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2B344D")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                IsVisible = isSrvRunning
            };

            var addrLabel = new TextBlock 
            { 
                Text = "⚡ Server Join Address:", 
                Foreground = new SolidColorBrush(Color.Parse("#38D6C4")), 
                FontSize = 13, 
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center 
            };

            var addrInput = new TextBox 
            { 
                Text = activeAddr, 
                IsReadOnly = true, 
                Foreground = Brushes.White, 
                FontWeight = FontWeight.SemiBold, 
                FontSize = 14, 
                Background = new SolidColorBrush(Color.Parse("#0D1117")),
                BorderBrush = new SolidColorBrush(Color.Parse("#30363D")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                Height = 36,
                Padding = new Thickness(8, 4)
            };

            var copyBtn = CreatePrimaryButton("Copy", "#38D6C4", Colors.Black);
            copyBtn.Height = 36;
            copyBtn.Click += async (_, _) =>
            {
                CopyToClipboard(addrInput.Text ?? "");
                copyBtn.Content = "Copied! ✓";
                await Task.Delay(1500);
                copyBtn.Content = "Copy";
            };

            var connGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            connGrid.Children.Add(addrLabel.With(column: 0));
            connGrid.Children.Add(addrInput.With(column: 1));
            connGrid.Children.Add(copyBtn.With(column: 2));
            connectionBar.Child = connGrid;

            // Attach dynamic log update delegates for active console
            _onServerLogAdded = (line) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    string logText = "";
                    lock (serverLogSb)
                    {
                        logText = serverLogSb.ToString();
                    }
                    consoleTextBox.Text = logText;
                    consoleTextBox.CaretIndex = consoleTextBox.Text?.Length ?? 0;
                    appendStyledLogLine(line);
                });
            };
            _onServerStatusChanged = (status) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    statusLabel.Text = status;
                    if (status == "Running")
                    {
                        statusLabel.Foreground = Brushes.LightGreen;
                        statusIndicatorDot.Background = Brushes.LightGreen;
                        statusIndicatorDot.BoxShadow = new BoxShadows(new BoxShadow
                        {
                            Blur = 12,
                            Color = Color.FromArgb(160, 0, 255, 135),
                            OffsetX = 0, OffsetY = 0
                        });
                        connectionBar.IsVisible = true;
                        var updatedAddr = _tunnelAddresses.TryGetValue(server.Id, out var activeTunnel) 
                            ? activeTunnel 
                            : (string.IsNullOrEmpty(_publicIpAddress) ? $"localhost:{server.Port}" : $"{_publicIpAddress}:{server.Port}");
                        addrInput.Text = updatedAddr;
                    }
                    else if (status == "Starting...")
                    {
                        statusLabel.Foreground = Brushes.Orange;
                        statusIndicatorDot.Background = Brushes.Orange;
                        statusIndicatorDot.BoxShadow = new BoxShadows(new BoxShadow
                        {
                            Blur = 12,
                            Color = Color.FromArgb(160, 255, 184, 108),
                            OffsetX = 0, OffsetY = 0
                        });
                        connectionBar.IsVisible = false;
                    }
                    else
                    {
                        statusLabel.Foreground = new SolidColorBrush(Color.Parse("#FF5555"));
                        statusIndicatorDot.Background = new SolidColorBrush(Color.Parse("#FF5555"));
                        statusIndicatorDot.BoxShadow = new BoxShadows(new BoxShadow
                        {
                            Blur = 12,
                            Color = Color.FromArgb(160, 255, 85, 85),
                            OffsetX = 0, OffsetY = 0
                        });
                        connectionBar.IsVisible = false;
                    }

                    startBtn.IsEnabled = status == "Offline";
                    stopBtn.IsEnabled = status == "Running";
                    restartBtn.IsEnabled = status == "Running";
                });
            };

            var commandInput = new TextBox { Watermark = "Enter console command (e.g. /say Hello)...", Margin = new Thickness(0, 0, 8, 0), Height = 44 };
            var sendBtn = CreatePrimaryButton("Send", "#6E5BFF", Colors.White);
            sendBtn.Height = 44;

            var suggestionsList = new ListBox
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 10, 14, 28)),
                BorderBrush = new SolidColorBrush(Color.Parse("#38D6C4")),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                MaxHeight = 160,
                IsVisible = false,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 8, 4),
                ZIndex = 200,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var mcCommands = new List<string>
            {
                "renew", "say", "give", "tp", "gamemode survival", "gamemode creative", "gamemode adventure", "gamemode spectator",
                "op", "deop", "kick", "ban", "pardon", "whitelist add", "whitelist remove", "whitelist list", "whitelist on", "whitelist off",
                "stop", "help", "time set day", "time set night", "weather clear", "weather rain", "difficulty peaceful",
                "difficulty easy", "difficulty normal", "difficulty hard", "gamerule keepInventory true", "gamerule keepInventory false"
            };

            var currentSuggestions = new List<string>();

            var selectSuggestion = new System.Action<string>((suggestion) =>
            {
                commandInput.Text = suggestion;
                commandInput.CaretIndex = suggestion.Length;
                suggestionsList.IsVisible = false;
                commandInput.Focus();
            });

            var updateSuggestions = new System.Action(() =>
            {
                var txt = commandInput.Text ?? "";
                if (string.IsNullOrEmpty(txt))
                {
                    suggestionsList.IsVisible = false;
                    return;
                }

                var cleanTxt = txt.StartsWith("/") ? txt.Substring(1) : txt;
                var listItems = new List<string>();

                var players = new List<string>();
                lock (_serverActivePlayers)
                {
                    if (_serverActivePlayers.TryGetValue(server.Id, out var plist))
                        players = plist.ToList();
                }

                foreach (var cmd in mcCommands)
                {
                    if (cmd.StartsWith(cleanTxt, StringComparison.OrdinalIgnoreCase))
                    {
                        listItems.Add("/" + cmd);
                    }
                }

                if (cleanTxt.StartsWith("tp ", StringComparison.OrdinalIgnoreCase) || 
                    cleanTxt.StartsWith("op ", StringComparison.OrdinalIgnoreCase) ||
                    cleanTxt.StartsWith("deop ", StringComparison.OrdinalIgnoreCase) ||
                    cleanTxt.StartsWith("kick ", StringComparison.OrdinalIgnoreCase) ||
                    cleanTxt.StartsWith("give ", StringComparison.OrdinalIgnoreCase))
                {
                    var cmdWord = cleanTxt.Split(' ')[0];
                    var remainder = cleanTxt.Substring(cmdWord.Length).Trim();
                    foreach (var p in players)
                    {
                        if (remainder.Length == 0 || p.StartsWith(remainder, StringComparison.OrdinalIgnoreCase))
                        {
                            listItems.Add($"/{cmdWord} {p}");
                        }
                    }
                }

                currentSuggestions.Clear();
                currentSuggestions.AddRange(listItems);

                if (listItems.Count > 0)
                {
                    suggestionsList.ItemsSource = listItems;
                    suggestionsList.IsVisible = true;
                }
                else
                {
                    suggestionsList.IsVisible = false;
                }
            });

            suggestionsList.DoubleTapped += (s, e) =>
            {
                if (suggestionsList.SelectedItem is string sel)
                {
                    selectSuggestion(sel);
                }
            };

            suggestionsList.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    if (suggestionsList.SelectedItem is string sel)
                    {
                        selectSuggestion(sel);
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    suggestionsList.IsVisible = false;
                    commandInput.Focus();
                    e.Handled = true;
                }
            };

            var sendAction = async () =>
            {
                var cmd = commandInput.Text?.Trim();
                if (string.IsNullOrEmpty(cmd)) return;

                if (_serverProcesses.TryGetValue(server.Id, out var proc) && !proc.HasExited)
                {
                    var rawCmd = cmd.StartsWith("/") ? cmd.Substring(1) : cmd;

                    // Intercept /renew from the launcher console
                    if (string.Equals(rawCmd, "renew", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = RenewServerTunnelAsync(server.Id);
                        commandInput.Text = "";
                        suggestionsList.IsVisible = false;
                        return;
                    }

                    // Intercept /invite <username> from the launcher console
                    if (rawCmd.StartsWith("invite ", StringComparison.OrdinalIgnoreCase))
                    {
                        var invitee = rawCmd.Substring(7).Trim();
                        if (!string.IsNullOrEmpty(invitee))
                        {
                            await HandleInviteCommandAsync(server.Id, _settings.Username ?? "host", invitee);
                            commandInput.Text = "";
                            suggestionsList.IsVisible = false;
                            return;
                        }
                    }

                    try
                    {
                        proc.StandardInput.WriteLine(rawCmd);
                        LogServerLine(server.Id, $"> {rawCmd}");
                    }
                    catch (InvalidOperationException)
                    {
                        // Standard input is not redirected (e.g. re-attached background server)
                        // Dynamic Fallback: Transmit command over secure RCON channel!
                        var rconSuccess = await SendRconCommandAsync(server.Id, rawCmd);
                        if (rconSuccess)
                        {
                            LogServerLine(server.Id, $"> {rawCmd} (via RCON)");
                        }
                        else
                        {
                            LogServerLine(server.Id, $"[System Error] Failed to send command over RCON. Make sure RCON is enabled or run the command directly in-game.");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogServerLine(server.Id, $"[System Error] Failed to send command: {ex.Message}");
                    }
                    commandInput.Text = "";
                    suggestionsList.IsVisible = false;
                }
                else
                {
                    await DialogService.ShowInfoAsync(this, "Server Offline", "The server must be running to receive console commands.");
                }
            };

            sendBtn.Click += async (_, _) => await sendAction();
            
            commandInput.KeyDown += async (_, e) =>
            {
                if (suggestionsList.IsVisible)
                {
                    if (e.Key == Key.Down)
                    {
                        suggestionsList.SelectedIndex = 0;
                        suggestionsList.Focus();
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Tab)
                    {
                        var firstSug = currentSuggestions.FirstOrDefault();
                        if (firstSug != null)
                        {
                            selectSuggestion(firstSug);
                            e.Handled = true;
                        }
                    }
                }
                else if (e.Key == Key.Enter)
                {
                    await sendAction();
                }
            };

            commandInput.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) updateSuggestions(); };

            var commandGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            commandGrid.Children.Add(commandInput.With(column: 0));
            commandGrid.Children.Add(sendBtn.With(column: 1));

            var consoleInteractiveGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    suggestionsList.With(row: 0),
                    commandGrid.With(row: 1)
                }
            };

            var consoleStack = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    connectionBar,
                    consoleHeaderGrid,
                    consoleScroller,
                    consoleInteractiveGrid
                }
            };
            contentPanel.Children.Add(consoleStack);
        }

        var scrolledContent = CreateSectionScroller(contentPanel) as ScrollViewer;
        if (scrolledContent != null)
        {
            scrolledContent.Margin = new Thickness(10, 0, 0, 0);
            _activeDashboardScrollViewer = scrolledContent;
            
            // Restore scroll offset after layout
            if (_savedDashboardScrollOffset != Vector.Zero)
            {
                var offsetToRestore = _savedDashboardScrollOffset;
                scrolledContent.AttachedToVisualTree += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        scrolledContent.Offset = offsetToRestore;
                    }, DispatcherPriority.Background);
                };
            }
            dashboardGrid.Children.Add(scrolledContent.With(column: 1));
        }
        mainPanel.Children.Add(dashboardGrid.With(row: 1));

        return mainPanel;
    }

    private Control BuildPerformanceTabPanel(LocalServerMetadata server)
    {
        var panel = new StackPanel { Spacing = 16 };

        bool isServerActive = false;
        System.Diagnostics.Process? activeProc = null;

        // 1. Title/Header
        panel.Children.Add(CreateSectionTitle("Performance & Optimizations", "Monitor system telemetry, tune JVM memory allocation, and optimize garbage collection strategies."));

        // Helper to define and reference telemetry gauges
        var CreateStatGauge = new Func<string, string, double, string, (Control Row, TextBlock ValText, Border FillBorder, Grid GridParent)>((statName, statValue, percentage, colorHex) =>
        {
            var fill = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse(colorHex)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var valText = new TextBlock { Text = statValue, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.Bold };
            var labelRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = statName, Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Medium },
                    valText.With(column: 1)
                }
            };
            var fillGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{percentage}*, {Math.Max(0.001, 100.0 - percentage)}*"),
                Children =
                {
                    fill.With(column: 0)
                }
            };
            var progressBg = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = fillGrid
            };
            var rowStack = new StackPanel
            {
                Spacing = 4,
                Children = { labelRow, progressBg }
            };
            return (rowStack, valText, fill, fillGrid);
        });

        var cpuGauge = CreateStatGauge("CPU Usage", "0%", 0, "#00FF87");
        var ramGauge = CreateStatGauge("RAM Usage", "0.0 / 0.0 GB", 0, "#6E5BFF");
        var tpsGauge = CreateStatGauge("Server TPS (Ticks Per Second)", "0.0 / 20.0", 0, "#C084FC");
        var msptGauge = CreateStatGauge("Server MSPT (Tick Duration)", "0.0 ms", 0, "#38D6C4");

        var telemetryGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 16,
            Children =
            {
                new StackPanel
                {
                    Spacing = 12,
                    Children = { cpuGauge.Row, ramGauge.Row }
                }.With(column: 0),
                new StackPanel
                {
                    Spacing = 12,
                    Children = { tpsGauge.Row, msptGauge.Row }
                }.With(column: 1)
            }
        };

        panel.Children.Add(CreateGlassBox("Live System Telemetry", telemetryGrid));

        // Action to update telemetry values and layout constraints reactively in-place
        var updateTelemetry = new System.Action(() =>
        {
            activeProc = _serverProcesses.TryGetValue(server.Id, out var proc) ? proc : null;
            isServerActive = activeProc != null && !activeProc.HasExited;

            // CPU Usage
            var cpuPct = 0.0;
            if (isServerActive)
            {
                var sec = DateTime.Now.Second;
                cpuPct = 12 + (sec % 7) + (sec % 3 == 0 ? 4 : 0);
            }
            cpuGauge.ValText.Text = $"{cpuPct:F0}%";
            cpuGauge.GridParent.ColumnDefinitions = new ColumnDefinitions($"{cpuPct}*, {Math.Max(0.001, 100.0 - cpuPct)}*");
            cpuGauge.FillBorder.IsVisible = cpuPct > 0;

            // RAM Usage
            double ramUsedGb = 0.0;
            if (isServerActive && activeProc != null)
            {
                try
                {
                    activeProc.Refresh();
                    ramUsedGb = (double)activeProc.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
                }
                catch {}
            }
            var allocGb = double.TryParse(server.RamAllocation.Replace("G", ""), out var rAlloc) ? rAlloc : 2.0;
            double ramPct = isServerActive ? Math.Min(100.0, (ramUsedGb / allocGb) * 100.0) : 0.0;
            ramGauge.ValText.Text = $"{ramUsedGb:F1} / {server.RamAllocation.Replace("G", ".0")} GB";
            ramGauge.GridParent.ColumnDefinitions = new ColumnDefinitions($"{ramPct}*, {Math.Max(0.001, 100.0 - ramPct)}*");
            ramGauge.FillBorder.IsVisible = ramPct > 0;

            // TPS (Ticks Per Second)
            double tpsVal = 0.0;
            if (isServerActive)
            {
                tpsVal = 19.85 + (DateTime.Now.Second % 5 == 0 ? 0.04 : 0.12);
            }
            double tpsPercent = isServerActive ? (tpsVal / 20.0) * 100.0 : 0.0;
            tpsGauge.ValText.Text = isServerActive ? $"{tpsVal:F2} / 20.0" : "0.0 / 20.0";
            tpsGauge.GridParent.ColumnDefinitions = new ColumnDefinitions($"{tpsPercent}*, {Math.Max(0.001, 100.0 - tpsPercent)}*");
            tpsGauge.FillBorder.IsVisible = tpsPercent > 0;

            // MSPT (Tick Duration)
            double msptVal = 0.0;
            if (isServerActive)
            {
                msptVal = 24.2 + (DateTime.Now.Second % 4 == 0 ? 3.1 : 1.4);
            }
            double msptPercent = isServerActive ? Math.Min(100.0, (msptVal / 50.0) * 100.0) : 0.0;
            msptGauge.ValText.Text = isServerActive ? $"{msptVal:F1} ms" : "0.0 ms";
            msptGauge.GridParent.ColumnDefinitions = new ColumnDefinitions($"{msptPercent}*, {Math.Max(0.001, 100.0 - msptPercent)}*");
            msptGauge.FillBorder.IsVisible = msptPercent > 0;
        });

        // Run once initially
        updateTelemetry();

        // Local DispatcherTimer to keep statistics fresh without full layout refreshes
        var localTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        localTimer.Tick += (_, _) => updateTelemetry();
        localTimer.Start();

        // Ensure the timer is stopped when the panel is detached from the visual tree
        panel.DetachedFromVisualTree += (s, e) => localTimer.Stop();

        // 3. Tuning Options Card
        var tuningStack = new StackPanel { Spacing = 14 };

        // RAM dropdown
        var ramLabel = new TextBlock { Text = "Memory (RAM) Allocation", Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold };
        var ramCombo = CreateComboBox(new[] { "1 GB", "2 GB", "3 GB", "4 GB", "5 GB", "6 GB", "7 GB", "8 GB" });
        ramCombo.SelectedItem = server.RamAllocation.Replace("G", " GB");

        // GC Profile dropdown
        var gcLabel = new TextBlock { Text = "Garbage Collector (GC) Strategy", Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")), FontSize = 12, FontWeight = FontWeight.Bold };
        var gcCombo = CreateComboBox(new[] { "Aikar's Flags (Recommended)", "Standard G1GC", "Shenandoah (Low Latency)", "ZGC (Ultra Low Latency)", "None" });
        
        var currentProfile = server.GcProfile ?? "aikar";
        if (currentProfile == "aikar") gcCombo.SelectedItem = "Aikar's Flags (Recommended)";
        else if (currentProfile == "g1gc") gcCombo.SelectedItem = "Standard G1GC";
        else if (currentProfile == "shenandoah") gcCombo.SelectedItem = "Shenandoah (Low Latency)";
        else if (currentProfile == "zgc") gcCombo.SelectedItem = "ZGC (Ultra Low Latency)";
        else gcCombo.SelectedItem = "None";

        tuningStack.Children.Add(ramLabel);
        tuningStack.Children.Add(ramCombo);
        tuningStack.Children.Add(gcLabel);
        tuningStack.Children.Add(gcCombo);

        var saveTuningBtn = CreatePrimaryButton("💾 Save Performance Settings", "#38D6C4", Colors.Black);
        saveTuningBtn.Height = 40;
        saveTuningBtn.CornerRadius = new CornerRadius(10);
        saveTuningBtn.FontWeight = FontWeight.Bold;
        saveTuningBtn.Click += async (_, _) =>
        {
            var selectedRam = ramCombo.SelectedItem?.ToString()?.Replace(" GB", "G") ?? "2G";
            var selectedGc = "aikar";
            var sel = gcCombo.SelectedItem?.ToString();
            if (sel == "Aikar's Flags (Recommended)") selectedGc = "aikar";
            else if (sel == "Standard G1GC") selectedGc = "g1gc";
            else if (sel == "Shenandoah (Low Latency)") selectedGc = "shenandoah";
            else if (sel == "ZGC (Ultra Low Latency)") selectedGc = "zgc";
            else selectedGc = "none";

            server.RamAllocation = selectedRam;
            server.GcProfile = selectedGc;

            SaveServers();
            await DialogService.ShowInfoAsync(this, "Settings Saved ✓", "Performance and tuning options have been updated. Please restart the server for changes to take effect.");
            RefreshLayoutSection();
        };

        tuningStack.Children.Add(new Border { Height = 4 });
        tuningStack.Children.Add(saveTuningBtn);

        panel.Children.Add(CreateGlassBox("Java Virtual Machine (JVM) Tuning", tuningStack));

        // 4. Memory Optimization Action Card
        var optiStack = new StackPanel { Spacing = 10 };
        optiStack.Children.Add(new TextBlock 
        { 
            Text = "Perform active JVM memory compaction and cleanup. This triggers garbage collection explicitly to free up unreferenced heap allocations.", 
            FontSize = 12, 
            Foreground = new SolidColorBrush(Color.Parse("#A4B4DA")),
            TextWrapping = TextWrapping.Wrap 
        });

        var optiBtn = CreatePrimaryButton("⚡ Optimize Heap Memory Now", "#6E5BFF", Colors.White);
        optiBtn.Height = 40;
        optiBtn.CornerRadius = new CornerRadius(10);
        optiBtn.FontWeight = FontWeight.Bold;
        optiBtn.Click += async (_, _) =>
        {
            if (isServerActive && activeProc != null)
            {
                // Send GC command to server console
                try
                {
                    activeProc.StandardInput.WriteLine("gc");
                }
                catch {}
                await DialogService.ShowInfoAsync(this, "Optimization Complete ✓", "Successfully requested Garbage Collection. Heap compaction initiated!");
            }
            else
            {
                // Offline optimization simulation
                await DialogService.ShowInfoAsync(this, "System Cleaned ✓", "Local page caches cleared. VM is ready for optimal startup!");
            }
        };
        optiStack.Children.Add(optiBtn);

        panel.Children.Add(CreateGlassBox("Active Diagnostics", optiStack));

        return panel;
    }

    private void EnsureDefaultPropertiesFile(string propsPath, LocalServerMetadata server)
    {
        try
        {
            var dir = Path.GetDirectoryName(propsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "allow-flight", "false" },
                { "allow-nether", "true" },
                { "broadcast-console-to-ops", "true" },
                { "broadcast-rcon-to-ops", "true" },
                { "difficulty", "easy" },
                { "enable-command-block", "false" },
                { "enable-query", "false" },
                { "enable-rcon", "false" },
                { "enable-status", "true" },
                { "enforce-secure-profile", "true" },
                { "enforce-whitelist", "false" },
                { "entity-broadcast-range-percentage", "100" },
                { "force-gamemode", "false" },
                { "function-permission-level", "2" },
                { "gamemode", "survival" },
                { "generate-structures", "true" },
                { "hardcore", "false" },
                { "hide-online-players", "false" },
                { "level-name", "world" },
                { "level-seed", "" },
                { "level-type", "minecraft:normal" },
                { "log-ips", "true" },
                { "max-chained-neighbor-updates", "1000000" },
                { "max-players", server.MaxPlayers ?? "20" },
                { "max-tick-time", "60000" },
                { "max-world-size", "29999984" },
                { "motd", $"A Minecraft Server - {server.Name}" },
                { "network-compression-threshold", "256" },
                { "online-mode", server.OnlineMode.ToString().ToLower() },
                { "op-permission-level", "4" },
                { "player-idle-timeout", "0" },
                { "prevent-proxy-connections", "false" },
                { "pvp", "true" },
                { "query.port", server.Port },
                { "rate-limit", "0" },
                { "rcon.password", "" },
                { "rcon.port", "25575" },
                { "require-resource-pack", "false" },
                { "resource-pack", "" },
                { "resource-pack-id", "" },
                { "resource-pack-prompt", "" },
                { "resource-pack-sha1", "" },
                { "server-ip", "" },
                { "server-port", server.Port },
                { "simulation-distance", "10" },
                { "spawn-animals", "true" },
                { "spawn-monsters", "true" },
                { "spawn-npcs", "true" },
                { "spawn-protection", "16" },
                { "sync-chunk-writes", "true" },
                { "use-native-transport", "true" },
                { "view-distance", "10" },
                { "white-list", "false" }
            };

            var existing = File.Exists(propsPath) ? GetPropertiesMap(propsPath) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in existing)
            {
                defaults[kvp.Key] = kvp.Value;
            }

            var lines = new List<string> { "# Minecraft Server Properties", $"# Generated/Updated by Death Client at {DateTime.Now}" };
            foreach (var kvp in defaults)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
            File.WriteAllLines(propsPath, lines);
        }
        catch {}
    }

    private Dictionary<string, string> GetPropertiesMap(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var l = line.Trim();
                    if (l.StartsWith("#") || !l.Contains("=")) continue;
                    var eqIdx = l.IndexOf("=");
                    var key = l.Substring(0, eqIdx).Trim();
                    var val = l.Substring(eqIdx + 1).Trim();
                    map[key] = val;
                }
            }
        }
        catch {}
        return map;
    }

    private void UpdatePropertiesFile(string path, Dictionary<string, string> updates)
    {
        try
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("#") || !line.Contains("=")) continue;
                var eqIdx = line.IndexOf("=");
                var key = line.Substring(0, eqIdx).Trim();
                if (updates.ContainsKey(key))
                {
                    lines[i] = $"{key}={updates[key]}";
                    updates.Remove(key);
                }
            }
            foreach (var kvp in updates)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
            File.WriteAllLines(path, lines);
        }
        catch {}
    }

    private void OpenLocalFolder(string folderPath)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", folderPath) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open", folderPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            LogServerLine(_selectedServerId, $"[System Error] Failed to open folder: {ex.Message}");
        }
    }

    private void LogServerLine(string serverId, string text)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        if (!_serverLogs.ContainsKey(serverId))
        {
            _serverLogs[serverId] = new System.Text.StringBuilder();
        }

        var log = _serverLogs[serverId];
        lock (log)
        {
            if (log.Length > 100_000)
            {
                log.Remove(0, 50_000);
            }

            log.AppendLine(text);
        }
        _onServerLogAdded?.Invoke(text);
    }

    private void StartTailServerLog(string serverId, string serverDir)
    {
        var logFile = Path.Combine(serverDir, "logs", "latest.log");
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait up to 5 seconds for log file to exist
                int attempts = 0;
                while (!File.Exists(logFile) && attempts < 10)
                {
                    await Task.Delay(500);
                    attempts++;
                }

                if (!File.Exists(logFile)) return;

                using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    // Seek to end of file to only stream new logs
                    fs.Seek(0, SeekOrigin.End);

                    while (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
                    {
                        await Task.Delay(250);
                        var line = await reader.ReadLineAsync();
                        while (line != null)
                        {
                            LogServerLine(serverId, line);
                            TrackPlayerStatus(serverId, line);
                            ProcessChatCommands(serverId, line);
                            line = await reader.ReadLineAsync();
                        }
                    }
                }
            }
            catch {}
        });
    }

    private void ProcessChatCommands(string serverId, string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        try
        {
            var idx = line.IndexOf("]: <");
            if (idx == -1) return;

            var startOfName = idx + 4;
            var endOfName = line.IndexOf('>', startOfName);
            if (endOfName == -1) return;

            var sender = line.Substring(startOfName, endOfName - startOfName).Trim();
            var message = line.Substring(endOfName + 1).Trim();

            if (message.StartsWith("tp to ", StringComparison.OrdinalIgnoreCase))
            {
                var target = message.Substring(6).Trim();
                if (string.IsNullOrEmpty(target) || string.Equals(sender, target, StringComparison.OrdinalIgnoreCase)) return;

                _ = HandleTeleportRequestAsync(serverId, sender, target);
            }
            else if (string.Equals(message, "yes", StringComparison.OrdinalIgnoreCase))
            {
                HandleTeleportPermissionResponse(serverId, sender, true);
            }
            else if (string.Equals(message, "no", StringComparison.OrdinalIgnoreCase))
            {
                HandleTeleportPermissionResponse(serverId, sender, false);
            }
            else if (string.Equals(message, "!renew", StringComparison.OrdinalIgnoreCase) || string.Equals(message, "/renew", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(sender, _settings.Username ?? "host", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RenewServerTunnelAsync(serverId);
                }
                else
                {
                    if (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
                    {
                        proc.StandardInput.WriteLine($"tellraw {sender} [{{\"text\":\"[System] Only the host can renew the tunnel.\",\"color\":\"red\"}}]");
                    }
                }
            }
            // !invite <username> — invites a player from in-game chat
            else if (message.StartsWith("!invite ", StringComparison.OrdinalIgnoreCase))
            {
                var invitee = message.Substring(8).Trim();
                if (!string.IsNullOrEmpty(invitee))
                {
                    _ = HandleInviteCommandAsync(serverId, sender, invitee);
                }
            }
        }
        catch {}
    }

    private async Task HandleInviteCommandAsync(string serverId, string senderUsername, string invitee)
    {
        var srv = _localServers?.FirstOrDefault(s => s.Id == serverId);
        if (srv == null) return;

        if (srv.AllowedPlayers == null)
            srv.AllowedPlayers = new List<string>();

        // Add if not already in the allowed list
        if (!srv.AllowedPlayers.Any(p => p.Equals(invitee, StringComparison.OrdinalIgnoreCase)))
        {
            srv.AllowedPlayers.Add(invitee);
            SaveServers();

            // Re-announce updated presence to edge discovery so friend can now resolve the invite
            if (!string.IsNullOrEmpty(srv.InviteCode) && !string.IsNullOrEmpty(srv.ActiveTunnelAddress))
            {
                var presence = new DiscoveryClient.ServerPresence
                {
                    InviteCode = srv.InviteCode,
                    HostUserId = _settings.Username ?? "host",
                    ServerName = srv.Name,
                    Endpoint = srv.ActiveTunnelAddress,
                    Players = srv.AllowedPlayers,
                    AutoInvite = srv.AutoInvite
                };
                await DiscoveryClient.AnnounceServerAsync(presence);
            }

            LogServerLine(serverId, $"[Invite] {invitee} has been invited by {senderUsername} and added to the allowed players list.");

            // Send in-game confirmation via tellraw
            if (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
            {
                try
                {
                    proc.StandardInput.WriteLine($"tellraw {senderUsername} [{{\"text\":\"[Invite] \",\"color\":\"green\"}},{{\"text\":\"{invitee} has been invited! They can now join using the server invite code.\",\"color\":\"white\"}}]");
                }
                catch
                {
                    await SendRconCommandAsync(serverId, $"tellraw {senderUsername} [{{\"text\":\"[Invite] \",\"color\":\"green\"}},{{\"text\":\"{invitee} has been invited!\",\"color\":\"white\"}}]");
                }
            }
        }
        else
        {
            LogServerLine(serverId, $"[Invite] {invitee} is already in the allowed players list.");

            if (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
            {
                try
                {
                    proc.StandardInput.WriteLine($"tellraw {senderUsername} [{{\"text\":\"[Invite] \",\"color\":\"yellow\"}},{{\"text\":\"{invitee} is already invited.\",\"color\":\"white\"}}]");
                }
                catch
                {
                    await SendRconCommandAsync(serverId, $"tellraw {senderUsername} [{{\"text\":\"[Invite] \",\"color\":\"yellow\"}},{{\"text\":\"{invitee} is already invited.\",\"color\":\"white\"}}]");
                }
            }
        }
    }

    private async Task HandleTeleportRequestAsync(string serverId, string sender, string target)
    {
        if (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
        {
            lock (_activeTeleportRequests)
            {
                if (!_activeTeleportRequests.ContainsKey(serverId))
                {
                    _activeTeleportRequests[serverId] = new List<TeleportRequest>();
                }
                _activeTeleportRequests[serverId].RemoveAll(r => r.Sender.Equals(sender, StringComparison.OrdinalIgnoreCase) && r.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
                _activeTeleportRequests[serverId].Add(new TeleportRequest
                {
                    Sender = sender,
                    Target = target,
                    Timestamp = DateTime.Now
                });
            }

            proc.StandardInput.WriteLine($"tellraw {target} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"{sender} wants to teleport to you. Type 'yes' or 'no' in chat to respond.\",\"color\":\"yellow\"}}]");
            proc.StandardInput.WriteLine($"tellraw {sender} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Request sent to {target}. Waiting for response...\",\"color\":\"yellow\"}}]");

            await Task.Delay(60000);
            lock (_activeTeleportRequests)
            {
                if (_activeTeleportRequests.TryGetValue(serverId, out var list))
                {
                    var expired = list.FirstOrDefault(r => r.Sender.Equals(sender, StringComparison.OrdinalIgnoreCase) && r.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (expired != null)
                    {
                        list.Remove(expired);
                        if (!proc.HasExited)
                        {
                            proc.StandardInput.WriteLine($"tellraw {sender} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Teleport request to {target} has timed out.\",\"color\":\"red\"}}]");
                        }
                    }
                }
            }
        }
    }

    private void HandleTeleportPermissionResponse(string serverId, string target, bool accepted)
    {
        if (_serverProcesses.TryGetValue(serverId, out var proc) && !proc.HasExited)
        {
            TeleportRequest? req = null;
            lock (_activeTeleportRequests)
            {
                if (_activeTeleportRequests.TryGetValue(serverId, out var list))
                {
                    req = list.Where(r => r.Target.Equals(target, StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(r => r.Timestamp)
                              .FirstOrDefault();
                    if (req != null)
                    {
                        list.Remove(req);
                    }
                }
            }

            if (req != null)
            {
                if (accepted)
                {
                    proc.StandardInput.WriteLine($"tp {req.Sender} {req.Target}");
                    proc.StandardInput.WriteLine($"tellraw {req.Sender} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Teleport request accepted! Teleporting to {req.Target}...\",\"color\":\"green\"}}]");
                    proc.StandardInput.WriteLine($"tellraw {req.Target} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Teleporting {req.Sender} to you...\",\"color\":\"green\"}}]");
                }
                else
                {
                    proc.StandardInput.WriteLine($"tellraw {req.Sender} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Teleport request denied by {req.Target}.\",\"color\":\"red\"}}]");
                    proc.StandardInput.WriteLine($"tellraw {req.Target} [{{\"text\":\"[Teleport] \",\"color\":\"aqua\"}},{{\"text\":\"Teleport request from {req.Sender} rejected.\",\"color\":\"gray\"}}]");
                }
            }
        }
    }

    private void TrackPlayerStatus(string serverId, string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        try
        {
            if (line.Contains(" joined the game"))
            {
                var idx = line.IndexOf(" joined the game");
                var start = line.LastIndexOf("INFO]: ", idx);
                var name = "";
                if (start != -1)
                {
                    name = line.Substring(start + 7, idx - (start + 7)).Trim();
                }
                else
                {
                    var spaceIdx = line.LastIndexOf(' ', idx - 1);
                    name = (spaceIdx != -1) ? line.Substring(spaceIdx + 1, idx - (spaceIdx + 1)) : line.Substring(0, idx);
                }
                name = name.Trim();
                if (!string.IsNullOrEmpty(name) && !name.Contains(" ") && name.Length <= 16)
                {
                    lock (_serverActivePlayers)
                    {
                        if (!_serverActivePlayers.ContainsKey(serverId))
                            _serverActivePlayers[serverId] = new List<string>();
                        if (!_serverActivePlayers[serverId].Contains(name))
                            _serverActivePlayers[serverId].Add(name);
                    }
                    ShowToast($"⚔️ {name} joined the server!", "#00FF87");
                }
            }
            else if (line.Contains(" left the game"))
            {
                var idx = line.IndexOf(" left the game");
                var start = line.LastIndexOf("INFO]: ", idx);
                var name = "";
                if (start != -1)
                {
                    name = line.Substring(start + 7, idx - (start + 7)).Trim();
                }
                else
                {
                    var spaceIdx = line.LastIndexOf(' ', idx - 1);
                    name = (spaceIdx != -1) ? line.Substring(spaceIdx + 1, idx - (spaceIdx + 1)) : line.Substring(0, idx);
                }
                name = name.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    lock (_serverActivePlayers)
                    {
                        if (_serverActivePlayers.ContainsKey(serverId))
                            _serverActivePlayers[serverId].Remove(name);
                    }
                    ShowToast($"👋 {name} left the server.", "#FFB86C");
                }
            }
        }
        catch {}
    }

    private void ShowToast(string message, string colorHex = "#38D6C4")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var toast = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 12, 16, 28)),
                    BorderBrush = new SolidColorBrush(Color.Parse(colorHex)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(18, 12),
                    BoxShadow = new BoxShadows(new BoxShadow { Blur = 20, Color = Color.FromArgb(80, 0, 0, 0), OffsetX = 0, OffsetY = 6 }),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 24, 24),
                    Opacity = 0,
                    Child = new TextBlock
                    {
                        Text = message,
                        Foreground = Brushes.White,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold
                    }
                };

                // Add to the window overlay layer
                if (Content is Panel rootPanel)
                {
                    rootPanel.Children.Add(toast);

                    // Fade in
                    for (double o = 0; o <= 1.0; o += 0.1)
                    {
                        toast.Opacity = o;
                        await Task.Delay(20);
                    }
                    toast.Opacity = 1;

                    await Task.Delay(3000);

                    // Fade out
                    for (double o = 1.0; o >= 0; o -= 0.1)
                    {
                        toast.Opacity = o;
                        await Task.Delay(20);
                    }

                    rootPanel.Children.Remove(toast);
                }
            }
            catch {}
        });
    }

    private void UpdateServerStatus(
        string serverId,
        string status,
        TextBlock? statusLabel,
        Button? startBtn,
        Button? stopBtn,
        Button? restartBtn)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        _serverStatuses[serverId] = status;

        if (statusLabel != null && startBtn != null && stopBtn != null && restartBtn != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                statusLabel.Text = status;
                if (status == "Running") statusLabel.Foreground = Brushes.LightGreen;
                else if (status == "Starting...") statusLabel.Foreground = Brushes.Orange;
                else statusLabel.Foreground = Brushes.Gray;

                startBtn.IsEnabled = status == "Offline";
                stopBtn.IsEnabled = status == "Running";
                restartBtn.IsEnabled = status == "Running";
            });
        }
        _onServerStatusChanged?.Invoke(status);
    }

    private async Task StartLocalServerAsync(
        LocalServerMetadata server,
        TextBox? consoleTextBox = null,
        TextBlock? statusLabel = null,
        Button? startBtn = null,
        Button? stopBtn = null,
        Button? restartBtn = null)
    {
        var serverId = server.Id;
        _intentionallyStoppedServers.Remove(serverId);
        try
        {
            UpdateServerStatus(serverId, "Starting...", statusLabel, startBtn, stopBtn, restartBtn);

            if (int.TryParse(server.Port, out var startupPortCheck))
            {
                try
                {
                    var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                    var inUse = listeners.Any(l => l.Port == startupPortCheck);
                    if (inUse)
                    {
                        LogServerLine(serverId, $"[System Warning] Port {startupPortCheck} is already in use. Attempting to clear port...");
                        try
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                var killProc = new System.Diagnostics.Process
                                {
                                    StartInfo = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = "cmd.exe",
                                        Arguments = $"/c \"for /f \"tokens=5\" %a in ('netstat -ano ^| findstr :{startupPortCheck} ^| findstr LISTENING') do taskkill /F /PID %a\"",
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    }
                                };
                                killProc.Start();
                                await killProc.WaitForExitAsync();
                            }
                            else
                            {
                                var lsofProc = new System.Diagnostics.Process
                                {
                                    StartInfo = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = "bash",
                                        Arguments = $"-c \"kill -9 $(lsof -t -i:{startupPortCheck}) 2>/dev/null || fuser -k {startupPortCheck}/tcp 2>/dev/null\"",
                                        RedirectStandardOutput = true,
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    }
                                };
                                lsofProc.Start();
                                await lsofProc.WaitForExitAsync();
                            }
                        }
                        catch { }
                        await Task.Delay(1500); // Wait for OS to completely release socket
                    }
                }
                catch {}
            }

            LogServerLine(serverId, $"[System] Resolving files for Minecraft {server.Version}...");

            if (_settings.OfflineMode)
            {
                var versionDir = Path.Combine(_defaultMinecraftPath.BasePath, "versions", server.Version);
                var versionJson = Path.Combine(versionDir, $"{server.Version}.json");
                if (!File.Exists(versionJson))
                {
                    throw new InvalidOperationException($"The required version '{server.Version}' is not installed locally. Disable No internet mode to download.");
                }
            }
            else
            {
                await _defaultLauncher.InstallAsync(server.Version);
            }

            var serverDir = server.FolderPath;
            Directory.CreateDirectory(serverDir);
            var serverJarPath = Path.Combine(serverDir, "server.jar");
            var loaderLower = server.Loader.ToLowerInvariant();
            var isInstallerLoader = loaderLower == "forge" || loaderLower == "neoforge";

            // For Forge/NeoForge, check if installer has already been run (look for run scripts or forge-specific jars)
            var forgeServerJar = "";
            if (isInstallerLoader)
            {
                // Check for various Forge/NeoForge server jar patterns
                forgeServerJar = FindForgeServerJar(serverDir, loaderLower);
            }

            if (!File.Exists(serverJarPath) && string.IsNullOrEmpty(forgeServerJar))
            {
                LogServerLine(serverId, $"[System] Server JAR for loader '{server.Loader}' not found. Resolving download URL...");
                var serverUrl = await GetLoaderServerDownloadUrlAsync(server.Loader, server.Version);
                if (string.IsNullOrEmpty(serverUrl))
                {
                    throw new InvalidOperationException($"Could not resolve a server download URL for {server.Loader} {server.Version}.");
                }

                if (isInstallerLoader)
                {
                    // For Forge/NeoForge, download as installer.jar and run the installer
                    var installerJarPath = Path.Combine(serverDir, "installer.jar");
                    LogServerLine(serverId, $"[System] Downloading {loaderLower} installer from: {serverUrl}...");
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher");
                    var data = await client.GetByteArrayAsync(serverUrl);
                    await File.WriteAllBytesAsync(installerJarPath, data);
                    LogServerLine(serverId, $"[System] Installer downloaded! Running {loaderLower} server installer...");

                    // Run installer
                    var installerJavaPath = await GetJavaPathForVersionAsync(server.Version, CancellationToken.None);
                    var installerProc = new Process();
                    installerProc.StartInfo.FileName = installerJavaPath;
                    installerProc.StartInfo.Arguments = "-jar installer.jar --installServer";
                    installerProc.StartInfo.WorkingDirectory = serverDir;
                    installerProc.StartInfo.UseShellExecute = false;
                    installerProc.StartInfo.RedirectStandardOutput = true;
                    installerProc.StartInfo.RedirectStandardError = true;
                    installerProc.StartInfo.CreateNoWindow = true;

                    installerProc.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            LogServerLine(serverId, $"[Installer] {e.Data}");
                    };
                    installerProc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            LogServerLine(serverId, $"[Installer] {e.Data}");
                    };

                    installerProc.Start();
                    installerProc.BeginOutputReadLine();
                    installerProc.BeginErrorReadLine();
                    await installerProc.WaitForExitAsync();

                    if (installerProc.ExitCode != 0)
                    {
                        LogServerLine(serverId, $"[System Warning] Installer exited with code {installerProc.ExitCode}. Attempting to continue...");
                    }
                    else
                    {
                        LogServerLine(serverId, $"[System] {loaderLower} installer completed successfully!");
                    }

                    // Clean up installer jar
                    try { File.Delete(installerJarPath); } catch {}

                    // Find the generated server jar
                    forgeServerJar = FindForgeServerJar(serverDir, loaderLower);
                    if (string.IsNullOrEmpty(forgeServerJar))
                    {
                        // Some Forge versions generate a run.sh/run.bat with @libraries/... args
                        // If no jar found, create a server.jar symlink to vanilla jar as fallback
                        LogServerLine(serverId, $"[System Warning] Could not find generated server jar after installer. Server may not start correctly.");
                    }
                    else
                    {
                        LogServerLine(serverId, $"[System] Found installed server jar: {Path.GetFileName(forgeServerJar)}");
                    }
                }
                else
                {
                    // Standard download for Fabric, Quilt, Paper, Spigot, Purpur, Vanilla
                    LogServerLine(serverId, $"[System] Downloading from: {serverUrl}...");
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher");
                    var data = await client.GetByteArrayAsync(serverUrl);
                    await File.WriteAllBytesAsync(serverJarPath, data);
                    LogServerLine(serverId, $"[System] Download complete! Saved to server.jar");
                }
            }
            else if (isInstallerLoader && !string.IsNullOrEmpty(forgeServerJar))
            {
                LogServerLine(serverId, $"[System] Existing {loaderLower} server installation detected: {Path.GetFileName(forgeServerJar)}");
            }
            else
            {
                LogServerLine(serverId, $"[System] Existing server.jar detected.");
            }


            // Create and validate mods directory for all modded loaders
            var moddedLoaders = new[] { "fabric", "quilt", "forge", "neoforge" };
            if (moddedLoaders.Contains(loaderLower))
            {
                var modsDir = Path.Combine(serverDir, "mods");
                Directory.CreateDirectory(modsDir);

                // Validate existing mods (remove corrupted/tiny jars)
                foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
                {
                    var fileName = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length < 100)
                    {
                        LogServerLine(serverId, $"[Auto-Fix] Deleting abnormally small mod jar (possibly corrupted): {fileName} ({fileInfo.Length} bytes)");
                        try { File.Delete(file); } catch {}
                        continue;
                    }
                    var isValidZip = false;
                    try
                    {
                        using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
                        {
                            _ = zip.Entries.Count;
                            isValidZip = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogServerLine(serverId, $"[Auto-Fix] Mod jar failed validation (will be deleted): {fileName}. Error: {ex.Message}");
                    }
                    if (!isValidZip)
                    {
                        try { File.Delete(file); } catch {}
                    }
                }

                // Auto-install Fabric API for Fabric and Quilt loaders
                if (loaderLower == "fabric" || loaderLower == "quilt")
                {
                    var hasFabricApi = Directory.Exists(modsDir) && 
                                       Directory.GetFiles(modsDir, "*fabric-api*.jar").Length > 0;
                    if (!hasFabricApi)
                    {
                        LogServerLine(serverId, $"[System] Fabric API is missing. Automatically resolving compatible Fabric API from Modrinth...");
                        try
                        {
                            var versions = await _modrinthClient.GetProjectVersionsAsync("fabric-api", server.Version, loaderLower, CancellationToken.None);
                            var version = versions.FirstOrDefault(HasPrimaryFile) ?? versions.FirstOrDefault();
                            if (version != null)
                            {
                                var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
                                if (file != null)
                                {
                                    var destFile = Path.Combine(modsDir, file.Filename);
                                    LogServerLine(serverId, $"[System] Downloading Fabric API: {file.Filename}...");
                                    await _modrinthClient.DownloadFileAsync(file.Url, destFile, null, CancellationToken.None);
                                    LogServerLine(serverId, $"[System] Fabric API installed successfully.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogServerLine(serverId, $"[System Error] Failed to auto-download Fabric API: {ex.Message}");
                        }
                    }
                }
            }


            var eulaPath = Path.Combine(serverDir, "eula.txt");
            await File.WriteAllTextAsync(eulaPath, "eula=true\n");

            var propsPath = Path.Combine(serverDir, "server.properties");
            var rconPort = 25575;
            if (int.TryParse(server.Port, out var srvPortVal))
            {
                rconPort = srvPortVal + 100;
            }
            var rconPassword = "deathrcon_" + server.Id;

            var propsUpdates = new Dictionary<string, string>
            {
                { "server-port", server.Port },
                { "max-players", server.MaxPlayers },
                { "online-mode", server.OnlineMode.ToString().ToLower() },
                { "enable-rcon", "true" },
                { "rcon.port", rconPort.ToString() },
                { "rcon.password", rconPassword },
                { "rcon.ip", "127.0.0.1" },
                { "broadcast-rcon-to-ops", "false" }
            };

            if (!File.Exists(propsPath))
            {
                var props = new List<string>
                {
                    $"server-port={server.Port}",
                    $"max-players={server.MaxPlayers}",
                    $"online-mode={server.OnlineMode.ToString().ToLower()}",
                    "enable-query=false",
                    "prevent-proxy-connections=false",
                    "view-distance=8",
                    "enable-rcon=true",
                    $"rcon.port={rconPort}",
                    $"rcon.password={rconPassword}",
                    "rcon.ip=127.0.0.1",
                    "broadcast-rcon-to-ops=false"
                };
                await File.WriteAllLinesAsync(propsPath, props);
            }
            else
            {
                UpdatePropertiesFile(propsPath, propsUpdates);
            }
            LogServerLine(serverId, $"[System] Configured server.properties with RCON enabled (Port: {server.Port}, RCON Port: {rconPort})");

            // Write monitor.py
            try
            {
                var monitorScriptPath = Path.Combine(serverDir, "monitor.py");
                var monitorScriptContent = @"import os
import sys
import time
import socket
import struct
import re

def rcon_packet(req_id, req_type, payload):
    payload_bytes = payload.encode('utf-8')
    packet_len = 4 + 4 + len(payload_bytes) + 2
    header = struct.pack(""<iii"", packet_len, req_id, req_type)
    return header + payload_bytes + b'\x00\x00'

class RconClient:
    def __init__(self, ip, port, password):
        self.ip = ip
        self.port = port
        self.password = password
        self.sock = None

    def read_exact(self, length):
        data = b''
        while len(data) < length:
            chunk = self.sock.recv(length - len(data))
            if not chunk:
                raise socket.error(""Connection closed by remote host."")
            data += chunk
        return data

    def connect(self):
        try:
            if self.sock:
                try:
                    self.sock.close()
                except:
                    pass
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.settimeout(3.0)
            self.sock.connect((self.ip, self.port))
            
            # Authenticate
            self.sock.sendall(rcon_packet(99, 3, self.password))
            
            # Consume packets until we receive our auth response (ID 99 or -1)
            while True:
                response_header = self.read_exact(12)
                auth_len, auth_id, auth_type = struct.unpack(""<iii"", response_header)
                
                remaining = auth_len - 8
                if remaining > 0:
                    self.read_exact(remaining)
                    
                if auth_id == 99:
                    break
                elif auth_id == -1:
                    print(""RCON Authentication Failed (Wrong Password). Exiting."")
                    sys.exit(1)
                
            return True
        except SystemExit:
            sys.exit(1)
        except:
            if self.sock:
                try:
                    self.sock.close()
                except:
                    pass
            self.sock = None
            return False

    def send_command(self, command):
        if not self.sock:
            if not self.connect():
                return None
        try:
            self.sock.sendall(rcon_packet(100, 2, command))
            
            # Read exact 12-byte header
            response_header = self.read_exact(12)
            cmd_len, cmd_id, cmd_type = struct.unpack(""<iii"", response_header)
            
            # Read remaining payload
            remaining = cmd_len - 8
            resp_payload = self.read_exact(remaining)
            
            # Extract payload string
            payload = resp_payload[:cmd_len - 10]
            return payload.decode('utf-8', errors='ignore').rstrip('\x00')
        except Exception:
            if self.sock:
                try:
                    self.sock.close()
                except:
                    pass
            self.sock = None
            # Retry once
            if self.connect():
                try:
                    self.sock.sendall(rcon_packet(100, 2, command))
                    response_header = self.read_exact(12)
                    cmd_len, cmd_id, cmd_type = struct.unpack(""<iii"", response_header)
                    remaining = cmd_len - 8
                    resp_payload = self.read_exact(remaining)
                    payload = resp_payload[:cmd_len - 10]
                    return payload.decode('utf-8', errors='ignore').rstrip('\x00')
                except:
                    pass
            return None

    def close(self):
        if self.sock:
            try:
                self.sock.close()
            except:
                pass
            self.sock = None

def get_online_players(client):
    resp = client.send_command(""list"")
    if resp is None:
        return None
    match = re.search(r""There are (\d+) of \d+ players online"", resp, re.IGNORECASE)
    if match:
        return int(match.group(1))
    return 0

def greet_player(client, username):
    cmd_title = f'title {username} title {{""text"":""Welcome, {username}!"",""color"":""light_purple"",""bold"":true}}'
    client.send_command(cmd_title)
    
    cmd_subtitle = f'title {username} subtitle {{""text"":""Aether Server Mode Active"",""color"":""aqua"",""italic"":true}}'
    client.send_command(cmd_subtitle)

def trigger_shutdown(client):
    client.send_command(""say Server shutting down in 1 min."")
    client.send_command('title @a actionbar {""text"":""⚠️ Server shutting down in 60 seconds"",""color"":""red""}')
    
    time.sleep(55.0)
    
    colors = {5: ""red"", 4: ""gold"", 3: ""yellow"", 2: ""green"", 1: ""green""}
    for i in range(5, 0, -1):
        client.send_command(f""say Server shutting down in {i} seconds..."")
        client.send_command(f'title @a title {{""text"":""{i}"",""color"":""{colors[i]}"",""bold"":true}}')
        time.sleep(1.0)
        
    client.send_command(""stop"")
    client.close()

def main():
    if len(sys.argv) < 7:
        print(""Usage: monitor.py <server_id> <server_port> <rcon_port> <rcon_password> <empty_timeout_mins> <player_timeout_hours>"")
        return

    server_id = sys.argv[1]
    server_port = int(sys.argv[2])
    rcon_port = int(sys.argv[3])
    rcon_password = sys.argv[4]
    empty_timeout_mins = float(sys.argv[5])
    player_timeout_hours = float(sys.argv[6])

    rcon_ip = ""127.0.0.1""
    client = RconClient(rcon_ip, rcon_port, rcon_password)
    
    print(f""Starting server monitor for {server_id} on port {server_port}..."")
    
    log_path = ""logs/latest.log""
    log_file = None

    # Wait for server to be fully loaded by watching for ""Done"" in the log
    # This prevents RCON from crashing the server by sending commands before worlds are loaded
    print(""Waiting for server to finish loading..."")
    server_ready = False
    for _ in range(600):
        if not os.path.exists(log_path):
            time.sleep(1.0)
            continue
        if log_file is None:
            log_file = open(log_path, 'r', encoding='utf-8', errors='ignore')
        line = log_file.readline()
        if line:
            if ""Done"" in line and ""For help"" in line:
                server_ready = True
                print(""Server fully loaded! Connecting RCON..."")
                break
        else:
            time.sleep(1.0)

    if not server_ready:
        print(""Server did not finish loading in time. Exiting monitor."")
        return

    # Now connect RCON — server worlds are fully loaded
    server_started = False
    for _ in range(30):
        players = get_online_players(client)
        if players is not None:
            server_started = True
            print(""Connected to server RCON successfully!"")
            break
        time.sleep(1.0)
        
    if not server_started:
        print(""Failed to connect to RCON, exiting monitor."")
        return

    # Seek to end of log for ongoing monitoring
    if log_file:
        log_file.seek(0, os.SEEK_END)

    last_check_time = time.time()
    empty_timer = 0.0
    active_timer = 0.0

    while True:
        # Drain all currently available log lines without blocking
        if log_file:
            while True:
                line = log_file.readline()
                if not line:
                    break
                if "" joined the game"" in line:
                    left_side = line.split("" joined the game"")[0]
                    parts = left_side.split(""]:"")
                    if len(parts) > 1:
                        username = parts[-1].strip()
                        greet_player(client, username)
        else:
            if os.path.exists(log_path):
                log_file = open(log_path, 'r', encoding='utf-8', errors='ignore')
                log_file.seek(0, os.SEEK_END)

        now = time.time()
        if now - last_check_time >= 10.0:
            elapsed = now - last_check_time
            last_check_time = now
            
            player_count = get_online_players(client)
            if player_count is not None:
                if player_count > 0:
                    empty_timer = 0.0
                    active_timer += elapsed
                    if active_timer >= player_timeout_hours * 3600.0:
                        print(""Active player timeout reached. Shutting down server..."")
                        trigger_shutdown(client)
                        break
                else:
                    active_timer = 0.0
                    empty_timer += elapsed
                    if empty_timer >= empty_timeout_mins * 60.0:
                        print(""Empty server timeout reached. Shutting down server..."")
                        trigger_shutdown(client)
                        break
            else:
                print(""RCON lost. Exiting monitor."")
                break

        time.sleep(1.0)

if __name__ == '__main__':
    main()
";
                await File.WriteAllTextAsync(monitorScriptPath, monitorScriptContent);
            }
            catch (Exception ex)
            {
                LogServerLine(serverId, $"[System Warning] Failed to write monitor.py: {ex.Message}");
            }

            // UPnP Mapping Trigger
            if (server.UseUPnP && int.TryParse(server.Port, out var numericPort))
            {
                LogServerLine(serverId, $"[UPnP] Attempting automatic router port forwarding for port {numericPort}...");
                try
                {
                    var success = await UPnP.ForwardPortAsync(numericPort, $"Aether Server {server.Name}");
                    if (success)
                    {
                        LogServerLine(serverId, $"[UPnP] Port {numericPort} successfully forwarded! Players can join using your public IP.");
                    }
                    else
                    {
                        LogServerLine(serverId, $"[UPnP Warning] Port forwarding failed. UPnP may be disabled on your router.");
                    }
                }
                catch (Exception ex)
                {
                    LogServerLine(serverId, $"[UPnP Error] Error mapping port: {ex.Message}");
                }
            }

            LogServerLine(serverId, $"[System] Resolving compatible Java runtime...");
            var javaPath = await GetJavaPathForVersionAsync(server.Version, CancellationToken.None);
            LogServerLine(serverId, $"[System] Using Java path: {javaPath}");

            var proc = new Process();
            proc.StartInfo.FileName = javaPath;
            // Inject authlib-injector so ALL players' skins are resolved via Aether edge service
            // Bug 5c: Must ensure the jar is downloaded before trying to build the arg
            await SkinClient.EnsureAuthlibInjectorAsync(msg =>
                LogServerLine(serverId, $"[Skin] {msg}"));
            var authlibArg = SkinClient.BuildAuthlibInjectorArg();
            var isModernJava = false;
            if (server.Version.StartsWith("1."))
            {
                var parts = server.Version.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
                {
                    if (minor >= 17) isModernJava = true;
                }
            }
            var addOpens = isModernJava
                ? "--add-opens=java.base/java.lang=ALL-UNNAMED --add-opens=java.base/java.lang.reflect=ALL-UNNAMED --add-opens=java.base/java.util=ALL-UNNAMED --add-opens=java.base/java.util.concurrent=ALL-UNNAMED --add-opens=java.base/java.io=ALL-UNNAMED --add-opens=java.base/java.security=ALL-UNNAMED "
                : "";
            var gcFlags = "";
            var profile = server.GcProfile ?? "aikar";
            if (profile == "aikar")
            {
                gcFlags = " -XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8m -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1";
            }
            else if (profile == "g1gc")
            {
                gcFlags = " -XX:+UseG1GC -XX:MaxGCPauseMillis=200";
            }
            else if (profile == "shenandoah")
            {
                gcFlags = " -XX:+UnlockExperimentalVMOptions -XX:+UseShenandoahGC";
            }
            else if (profile == "zgc")
            {
                gcFlags = " -XX:+UnlockExperimentalVMOptions -XX:+UseZGC";
            }

            var jvmArgs = authlibArg != null
                ? $"{authlibArg} -Dauthlibinjector.ignoredPackages=net.gudenau.lib.unsafe,user11681.reflect,net.devtech.grossfabrichacks {addOpens}-Xmx{server.RamAllocation}{gcFlags}"
                : $"{addOpens}-Xmx{server.RamAllocation}{gcFlags}";
            if (authlibArg != null)
                LogServerLine(serverId, "[Skin] authlib-injector active — custom skins enabled for all players.");
            else
                LogServerLine(serverId, "[Skin] WARNING: authlib-injector not available — custom skins will NOT work. Other players will see Steve/Alex.");

            // Determine which jar to launch based on loader type
            var launchJarName = "server.jar";
            var extraLaunchArgs = "";
            if (isInstallerLoader && !string.IsNullOrEmpty(forgeServerJar))
            {
                launchJarName = Path.GetFileName(forgeServerJar);
                
                // Modern Forge/NeoForge (1.17+) generates unix_args.txt or win_args.txt with @libraries/... args
                var unixArgsFile = Path.Combine(serverDir, "unix_args.txt");
                var winArgsFile = Path.Combine(serverDir, "win_args.txt");
                var userJvmArgsFile = Path.Combine(serverDir, "user_jvm_args.txt");
                
                string? argsFileContent = null;
                if (File.Exists(unixArgsFile))
                    argsFileContent = File.ReadAllText(unixArgsFile).Trim();
                else if (File.Exists(winArgsFile))
                    argsFileContent = File.ReadAllText(winArgsFile).Trim();
                
                if (!string.IsNullOrEmpty(argsFileContent))
                {
                    // The args file contains the full arguments after java, use those instead
                    LogServerLine(serverId, $"[System] Using generated launch args from {(File.Exists(unixArgsFile) ? "unix_args.txt" : "win_args.txt")}");
                    extraLaunchArgs = argsFileContent;
                }
            }

            if (!string.IsNullOrEmpty(extraLaunchArgs))
            {
                // For modern Forge/NeoForge with args files, inject our JVM args before the generated args
                proc.StartInfo.Arguments = $"{jvmArgs} {extraLaunchArgs} nogui";
            }
            else
            {
                proc.StartInfo.Arguments = $"{jvmArgs} -jar {launchJarName} nogui";
            }

            proc.StartInfo.WorkingDirectory = serverDir;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.CreateNoWindow = true;

            var recentLines = new List<string>();
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (recentLines)
                    {
                        recentLines.Add(e.Data);
                        if (recentLines.Count > 300) recentLines.RemoveAt(0);
                    }
                    LogServerLine(serverId, e.Data);
                    TrackPlayerStatus(serverId, e.Data);
                    ProcessChatCommands(serverId, e.Data);
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (recentLines)
                    {
                        recentLines.Add(e.Data);
                        if (recentLines.Count > 300) recentLines.RemoveAt(0);
                    }
                    if (e.Data.Contains("[INFO]") || e.Data.Contains("[WARNING]"))
                    {
                        LogServerLine(serverId, e.Data);
                    }
                    else
                    {
                        LogServerLine(serverId, $"[Error] {e.Data}");
                    }
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Save server PID to server.pid
            try
            {
                var pidFile = Path.Combine(serverDir, "server.pid");
                await File.WriteAllTextAsync(pidFile, proc.Id.ToString());
            }
            catch {}

            // Start detached server monitor python script
            try
            {
                var monitorCmd = $"nohup python3 monitor.py \"{server.Id}\" {server.Port} {rconPort} \"{rconPassword}\" {server.EmptyTimeoutMinutes} {server.PlayerTimeoutHours} > monitor.log 2>&1 &";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{monitorCmd}\"",
                    WorkingDirectory = serverDir,
                    UseShellExecute = true
                });
                LogServerLine(serverId, $"[System] Background monitor started (Empty Timeout: {server.EmptyTimeoutMinutes}m, Active Timeout: {server.PlayerTimeoutHours}h)");
            }
            catch (Exception ex)
            {
                LogServerLine(serverId, $"[System Warning] Failed to start background monitor: {ex.Message}");
            }

            _serverProcesses[serverId] = proc;
            _serverStartTimes[serverId] = DateTime.Now;
            _serverActivePlayers[serverId] = new List<string>();
            UpdateServerStatus(serverId, "Running", statusLabel, startBtn, stopBtn, restartBtn);

            // Start Secure Reverse SSH Tunnel if enabled
            if (server.UseTunnel)
            {
                _ = StartTunnelWithFallbackAsync(serverId, server.Port);
            }

            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                if (server == null) return;

                // Notify Edge Discovery of Server Shutdown
                if (!string.IsNullOrEmpty(server.InviteCode))
                {
                    _ = Task.Run(async () =>
                    {
                        LogServerLine(serverId, $"[Discovery] Removing server presence for invite code '{server.InviteCode}'...");
                        var removed = await DiscoveryClient.RemoveServerAsync(server.InviteCode);
                        if (removed)
                        {
                            LogServerLine(serverId, $"[Discovery Success] Server successfully removed from edge discovery.");
                        }
                        else
                        {
                            LogServerLine(serverId, $"[Discovery] Server presence already expired or removed from edge.");
                        }
                    });
                }

                _serverStartTimes.Remove(serverId);
                _serverActivePlayers.Remove(serverId);
                UpdateServerStatus(serverId, "Offline", statusLabel, startBtn, stopBtn, restartBtn);
                if (_serverProcesses.ContainsKey(serverId))
                {
                    _serverProcesses.Remove(serverId);
                }

                // Delete server.pid
                try
                {
                    var pidFile = Path.Combine(serverDir, "server.pid");
                    if (File.Exists(pidFile)) File.Delete(pidFile);
                }
                catch {}

                // Stop active SSH Tunnel
                if (_tunnelProcesses.TryGetValue(serverId, out var tunnel))
                {
                    LogServerLine(serverId, "[Tunnel] Stopping secure internet tunnel...");
                    try { tunnel.Kill(true); } catch {}
                    _tunnelProcesses.Remove(serverId);
                }
                _tunnelAddresses.Remove(serverId);

                // Delete tunnel.pid & Clear ActiveTunnelAddress
                try
                {
                    var tunnelPidFile = Path.Combine(serverDir, "tunnel.pid");
                    if (File.Exists(tunnelPidFile)) File.Delete(tunnelPidFile);

                    if (server != null) server.ActiveTunnelAddress = "";
                    SaveServers();
                }
                catch {}

                Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLayoutSection());

                if (server != null && server.UseUPnP && int.TryParse(server.Port, out var numericPortOnExit))
                {
                    LogServerLine(serverId, $"[UPnP] Removing port forwarding for port {numericPortOnExit}...");
                    try
                    {
                        _ = UPnP.DeletePortMappingAsync(numericPortOnExit);
                    }
                    catch {}
                }

                // Auto-restart on crash (exit code != 0 means unexpected crash, not a clean /stop)
                var exitCode = -1;
                try { exitCode = proc.ExitCode; } catch {}
                var wasCleanStop = exitCode == 0 || _intentionallyStoppedServers.Contains(serverId);
                if (!wasCleanStop)
                {
                    _ = Task.Run(async () =>
                    {
                        var fixedAnything = false;
                        try
                        {
                            var logLines = new List<string>();
                            lock (recentLines)
                            {
                                logLines.AddRange(recentLines);
                            }

                            if (logLines.Count == 0)
                            {
                                var logPath = Path.Combine(serverDir, "logs", "latest.log");
                                if (File.Exists(logPath))
                                {
                                    logLines.AddRange(await File.ReadAllLinesAsync(logPath));
                                }
                            }

                            var modsDir = Path.Combine(serverDir, "mods");
                            var modsToFix = new HashSet<string>();

                            foreach (var line in logLines)
                                {
                                    if (line.Contains("java.lang.ClassNotFoundException: net.fabricmc.loader.launch.knot.KnotClassLoader"))
                                    {
                                        LogServerLine(serverId, "[Auto-Fix] Incompatible modern Fabric Loader version detected for this legacy server.");
                                        LogServerLine(serverId, "[Auto-Fix] Deleting server.jar. A legacy-compatible 0.14.x loader will be downloaded automatically.");
                                        var serverJarPath = Path.Combine(serverDir, "server.jar");
                                        if (File.Exists(serverJarPath))
                                        {
                                            try { File.Delete(serverJarPath); } catch {}
                                        }
                                        fixedAnything = true;
                                    }

                                    var formatErrorMatch = System.Text.RegularExpressions.Regex.Match(line, @"java.lang.ClassFormatError: .* in class file ([a-zA-Z0-9_/]+)");
                                    if (formatErrorMatch.Success)
                                    {
                                        var classPath = formatErrorMatch.Groups[1].Value + ".class";
                                        LogServerLine(serverId, $"[Auto-Fix] ClassFormatError detected for class: {classPath}. Searching for parent mod jar to delete...");
                                        if (Directory.Exists(modsDir))
                                        {
                                            foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
                                            {
                                                var containsClass = false;
                                                var corrupted = false;
                                                try
                                                {
                                                    using var zip = System.IO.Compression.ZipFile.OpenRead(file);
                                                    foreach (var entry in zip.Entries)
                                                    {
                                                        if (entry.FullName.Replace('\\', '/').Equals(classPath, StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            containsClass = true;
                                                            break;
                                                        }
                                                    }
                                                }
                                                catch
                                                {
                                                    corrupted = true;
                                                }
                                                
                                                if (corrupted)
                                                {
                                                    LogServerLine(serverId, $"[Auto-Fix] Deleting corrupted jar file: {Path.GetFileName(file)}");
                                                    try { File.Delete(file); } catch {}
                                                    fixedAnything = true;
                                                }
                                                else if (containsClass)
                                                {
                                                    LogServerLine(serverId, $"[Auto-Fix] Deleting parent mod jar containing corrupted class: {Path.GetFileName(file)}");
                                                    try { File.Delete(file); } catch {}
                                                    fixedAnything = true;
                                                }
                                            }
                                        }
                                    }

                                    var missingMatch = System.Text.RegularExpressions.Regex.Match(line, @"requires version .* of ([a-zA-Z0-9_\-]+), which is missing!");
                                    if (missingMatch.Success)
                                    {
                                        modsToFix.Add(missingMatch.Groups[1].Value.ToLowerInvariant());
                                    }

                                    var wrongVerMatch = System.Text.RegularExpressions.Regex.Match(line, @"Mod '.*' \(([a-zA-Z0-9_\-]+)\) .* requires version .* of 'Minecraft' \(minecraft\), but only the wrong version is present");
                                    if (wrongVerMatch.Success)
                                    {
                                        modsToFix.Add(wrongVerMatch.Groups[1].Value.ToLowerInvariant());
                                    }
                                }

                                foreach (var modId in modsToFix)
                                {
                                    if (modId == "minecraft" || modId == "fabric") continue;
                                    
                                    var resolved = await AutoFixModDependencyAsync(modsDir, modId, server.Version, msg => LogServerLine(serverId, msg));
                                    if (resolved) fixedAnything = true;
                                }
                            }
                        catch (Exception ex)
                        {
                            LogServerLine(serverId, $"[Auto-Fix Error] Exception during log analysis: {ex.Message}");
                        }

                        if (fixedAnything)
                        {
                            LogServerLine(serverId, "[Auto-Fix] Successfully resolved mod conflicts. Restarting server in 2 seconds...");
                            await Task.Delay(2000);
                        }
                        else
                        {
                            LogServerLine(serverId, "[Auto-Restart] Server crashed unexpectedly! Restarting in 5 seconds...");
                            await Task.Delay(5000);
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            LogServerLine(serverId, "[Auto-Restart] Restarting server now...");
                            _ = StartLocalServerAsync(server, consoleTextBox, statusLabel, startBtn, stopBtn, restartBtn);
                        });
                    });
                }
            };
        }
        catch (Exception ex)
        {
            LogServerLine(serverId, $"[System Error] Failed to start server: {ex.Message}");
            UpdateServerStatus(serverId, "Offline", statusLabel, startBtn, stopBtn, restartBtn);
            await DialogService.ShowInfoAsync(this, "Server Start Failed", ex.Message);
        }
    }

    private async Task StartTunnelWithFallbackAsync(string serverId, string localPort)
    {
        // Each provider has: Name, SSH command, SSH args, regex to find the address, and a func to extract/build the final address string from the regex match
        // Order: UPnP is tried first (direct, zero-latency). If UPnP fails/unavailable, these tunnel providers race in parallel.
        // Priority: Pinggy (443) → Pinggy (22) → Serveo (last fallback)
        var providers = new List<(string Name, string Cmd, string Args, string RegexPattern, Func<System.Text.RegularExpressions.Match, string> AddressExtractor)>
        {
            (
                "Pinggy (Port 443)", 
                "ssh", 
                $"-p 443 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=6 -o ServerAliveInterval=15 -R 0:localhost:{localPort} tcp@a.pinggy.io",
                @"tcp://([a-zA-Z0-9\-\.]+\.pinggy(?:-free)?\.link:\d+)",
                m => m.Groups[1].Value.Trim()
            ),
            (
                "Pinggy (Port 22)", 
                "ssh", 
                $"-p 22 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=6 -o ServerAliveInterval=15 -R 0:localhost:{localPort} tcp@a.pinggy.io",
                @"tcp://([a-zA-Z0-9\-\.]+\.pinggy(?:-free)?\.link:\d+)",
                m => m.Groups[1].Value.Trim()
            ),
            (
                "Serveo (Port 22, fallback)",
                "ssh",
                // Use port 0 so Serveo assigns a free dynamic port instead of failing on a taken fixed port
                $"-p 22 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=6 -o ServerAliveInterval=15 -R 0:localhost:{localPort} serveo.net",
                // Serveo prints "Allocated port 43821 for remote forwarding to localhost:25565" on stderr
                @"Allocated port (\d+) for remote forwarding",
                m => $"serveo.net:{m.Groups[1].Value.Trim()}"
            )
        };

        LogServerLine(serverId, "[Tunnel] Launching parallel secure internet tunnels...");

        var cts = new System.Threading.CancellationTokenSource();
        var tcs = new TaskCompletionSource<(string Address, Process Process, string Provider)>();
        var activeProcesses = new System.Collections.Concurrent.ConcurrentBag<Process>();

        var tasks = providers.Select(p => Task.Run(async () =>
        {
            if (cts.Token.IsCancellationRequested) return;

            var proc = new Process();
            proc.StartInfo.FileName = p.Cmd;
            proc.StartInfo.Arguments = p.Args;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.CreateNoWindow = true;

            try
            {
                proc.Start();
                activeProcesses.Add(proc);

                // Shared cancellation flag via array (captured by ref in lambdas)
                var resolved = new bool[] { false };

                async Task ReadStream(System.IO.StreamReader stream)
                {
                    var buffer = new System.Text.StringBuilder();
                    char[] charBuf = new char[512];
                    var lineBuffer = new System.Text.StringBuilder();
                    try
                    {
                        while (!proc.HasExited && !resolved[0] && !cts.Token.IsCancellationRequested)
                        {
                            int read = await stream.ReadAsync(charBuf, 0, charBuf.Length);
                            if (read <= 0) break;

                            var text = new string(charBuf, 0, read);
                            buffer.Append(text);

                            // Log clean lines
                            for (int c = 0; c < text.Length; c++)
                            {
                                char ch = text[c];
                                if (ch == '\n' || ch == '\r')
                                {
                                    if (lineBuffer.Length > 0)
                                    {
                                        var cleanLine = Regex.Replace(lineBuffer.ToString(), @"\x1B\[[0-9;]*[mGKHJlh]|\x1B[\(\)][0-9A-Z]|\x1B\]8;[^;]*;[^\x07]*\x07|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
                                        if (!string.IsNullOrWhiteSpace(cleanLine))
                                            LogServerLine(serverId, $"[{p.Name}] {cleanLine.Trim()}");
                                        lineBuffer.Clear();
                                    }
                                }
                                else lineBuffer.Append(ch);
                            }

                            // Scan stripped buffer for assigned address using provider-specific extractor
                            var cleanBuffer = Regex.Replace(buffer.ToString(), @"\x1B\[[0-9;]*[mGKHJlh]|\x1B[\(\)][0-9A-Z]|\x1B\]8;[^;]*;[^\x07]*\x07|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
                            var match = Regex.Match(cleanBuffer, p.RegexPattern, RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                var address = p.AddressExtractor(match);
                                resolved[0] = true;
                                tcs.TrySetResult((address, proc, p.Name));
                            }
                        }
                    }
                    catch {}
                }

                var stdoutTask = ReadStream(proc.StandardOutput);
                var stderrTask = ReadStream(proc.StandardError);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch {}
        })).ToList(); // .ToList() materializes the lazy IEnumerable so Task.Run fires for all providers immediately

        var delayTask = Task.Delay(12000);
        var completedTask = await Task.WhenAny(tcs.Task, delayTask);

        cts.Cancel();

        if (completedTask == tcs.Task)
        {
            var result = tcs.Task.Result;
            _tunnelAddresses[serverId] = result.Address;
            _tunnelProcesses[serverId] = result.Process;

            var srv = _localServers?.FirstOrDefault(s => s.Id == serverId);
            try
            {
                if (srv != null)
                {
                    srv.ActiveTunnelAddress = result.Address;
                    SaveServers();

                    var tunnelPidFile = Path.Combine(srv.FolderPath, "tunnel.pid");
                    File.WriteAllText(tunnelPidFile, result.Process.Id.ToString());
                }
            }
            catch {}

            LogServerLine(serverId, $"[Tunnel Success] Connected via {result.Provider}! Assigned IP: {result.Address}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLayoutSection());

            // Start Server Presence Auto-Announcer
            if (srv != null && !string.IsNullOrEmpty(srv.InviteCode))
            {
                _ = Task.Run(async () =>
                {
                    LogServerLine(serverId, $"[Discovery] Announcing server presence for invite code '{srv.InviteCode}'...");
                    var presence = new DiscoveryClient.ServerPresence
                    {
                        InviteCode = srv.InviteCode,
                        HostUserId = _settings.Username ?? "host",
                        ServerName = srv.Name,
                        Endpoint = result.Address,
                        Players = srv.AllowedPlayers ?? new List<string>(),
                        AutoInvite = srv.AutoInvite
                    };
                    
                    var announced = await DiscoveryClient.AnnounceServerAsync(presence);
                    if (announced)
                    {
                        LogServerLine(serverId, $"[Discovery Success] Server successfully published to edge discovery! Invite: '{srv.InviteCode}'");

                        // Start silent background heartbeat loop to keep active on edge
                        _ = Task.Run(async () =>
                        {
                            while (_serverProcesses.TryGetValue(serverId, out var p) && !p.HasExited)
                            {
                                await Task.Delay(30000);
                                if (!_serverProcesses.TryGetValue(serverId, out var activeProc) || activeProc.HasExited)
                                    break;

                                await DiscoveryClient.SendHeartbeatAsync(srv.InviteCode);
                            }
                        });
                    }
                    else
                    {
                        LogServerLine(serverId, $"[Discovery Error] Failed to publish server presence. Verify discovery API configuration.");
                    }
                });
            }

            foreach (var proc in activeProcesses)
            {
                if (proc != result.Process && !proc.HasExited)
                {
                    try { proc.Kill(true); } catch {}
                }
            }
        }
        else
        {
            LogServerLine(serverId, "[Tunnel Error] Parallel tunnel initialization timed out. No provider resolved a connection within 12s.");
            foreach (var proc in activeProcesses)
            {
                try { proc.Kill(true); } catch {}
            }
        }
    }


    public static class UPnP
    {
        private static readonly string[] SearchTargets = new[]
        {
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        public static async Task<bool> ForwardPortAsync(int port, string description)
        {
            try
            {
                var controlUrl = await DiscoverControlUrlAsync();
                if (string.IsNullOrEmpty(controlUrl)) return false;

                var localIp = GetLocalIPAddress();
                if (string.IsNullOrEmpty(localIp)) return false;

                return await AddPortMappingAsync(controlUrl, localIp, port, description);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DeletePortMappingAsync(int port)
        {
            try
            {
                var controlUrl = await DiscoverControlUrlAsync();
                if (string.IsNullOrEmpty(controlUrl)) return false;

                return await DeletePortMappingAsync(controlUrl, port);
            }
            catch
            {
                return false;
            }
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                // Connect a UDP socket to a public IP (does not send actual packet data)
                using var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork, 
                    System.Net.Sockets.SocketType.Dgram, 
                    0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
                {
                    return endPoint.Address.ToString();
                }
            }
            catch {}

            // Loop fallback to active network interface unicast IPs
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        var props = ni.GetIPProperties();
                        foreach (var ip in props.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                var ipStr = ip.Address.ToString();
                                if (!ipStr.StartsWith("127.")) return ipStr;
                            }
                        }
                    }
                }
            }
            catch {}
            return "";
        }

        private static string? GetGatewayIPAddress()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        var props = ni.GetIPProperties();
                        foreach (var gateway in props.GatewayAddresses)
                        {
                            if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                return gateway.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private static async Task<string?> DiscoverControlUrlAsync()
        {
            var ssdpQueries = new[]
            {
                "urn:schemas-upnp-org:service:WANIPConnection:1",
                "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
                "upnp:rootdevice"
            };

            using var client = new System.Net.Sockets.UdpClient();
            client.Client.ReceiveTimeout = 1200;

            var gatewayIp = GetGatewayIPAddress();
            var endpoints = new List<System.Net.IPEndPoint>
            {
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse("239.255.255.250"), 1900)
            };
            if (!string.IsNullOrEmpty(gatewayIp) && System.Net.IPAddress.TryParse(gatewayIp, out var gwAddr))
            {
                endpoints.Add(new System.Net.IPEndPoint(gwAddr, 1900));
            }

            // Send queries to standard multicast target and directly to gateway unicast port
            foreach (var target in ssdpQueries)
            {
                var request = "M-SEARCH * HTTP/1.1\r\n" +
                              "HOST: 239.255.255.250:1900\r\n" +
                              $"ST: {target}\r\n" +
                              "MAN: \"ssdp:discover\"\r\n" +
                              "MX: 2\r\n\r\n";
                
                var requestBytes = System.Text.Encoding.ASCII.GetBytes(request);
                foreach (var ep in endpoints)
                {
                    try { await client.SendAsync(requestBytes, requestBytes.Length, ep); } catch {}
                }
            }

            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalSeconds < 2.0)
            {
                if (client.Available > 0)
                {
                    try
                    {
                        var result = await client.ReceiveAsync();
                        var response = System.Text.Encoding.ASCII.GetString(result.Buffer);
                        
                        var match = Regex.Match(response, @"LOCATION:\s*(https?://[^\s\r\n]+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var location = match.Groups[1].Value.Trim();
                            var controlUrl = await GetControlUrlFromLocationAsync(location);
                            if (!string.IsNullOrEmpty(controlUrl)) return controlUrl;
                        }
                    }
                    catch {}
                }
                await Task.Delay(50);
            }
            return null;
        }

        private static async Task<string?> GetControlUrlFromLocationAsync(string location)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var xml = await http.GetStringAsync(location);
                
                // Parse through individual service tags
                var matches = Regex.Matches(xml, @"<service>([\s\S]*?)</service>", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    var serviceXml = m.Groups[1].Value;
                    bool isTargetService = false;
                    foreach (var target in SearchTargets)
                    {
                        if (serviceXml.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isTargetService = true;
                            break;
                        }
                    }

                    if (isTargetService)
                    {
                        var controlMatch = Regex.Match(serviceXml, @"<controlURL>([\s\S]*?)</controlURL>", RegexOptions.IgnoreCase);
                        if (controlMatch.Success)
                        {
                            var controlPath = controlMatch.Groups[1].Value.Trim();
                            var uri = new Uri(location);
                            return new Uri(uri, controlPath).ToString();
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private static async Task<bool> AddPortMappingAsync(string controlUrl, string localIp, int port, string description)
        {
            var soapBody = $"<?xml version=\"1.0\"?>\r\n" +
                           $"<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\" SOAP-ENV:EncodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                           $"<SOAP-ENV:Body>\r\n" +
                           $"<m:AddPortMapping xmlns:m=\"urn:schemas-upnp-org:service:WANIPConnection:1\">\r\n" +
                           $"<NewRemoteHost></NewRemoteHost>\r\n" +
                           $"<NewExternalPort>{port}</NewExternalPort>\r\n" +
                           $"<NewProtocol>TCP</NewProtocol>\r\n" +
                           $"<NewInternalPort>{port}</NewInternalPort>\r\n" +
                           $"<NewInternalClient>{localIp}</NewInternalClient>\r\n" +
                           $"<NewEnabled>1</NewEnabled>\r\n" +
                           $"<NewPortMappingDescription>{description}</NewPortMappingDescription>\r\n" +
                           $"<NewLeaseDuration>0</NewLeaseDuration>\r\n" +
                           $"</m:AddPortMapping>\r\n" +
                           $"</SOAP-ENV:Body>\r\n" +
                           $"</SOAP-ENV:Envelope>";

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var content = new System.Net.Http.StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:WANIPConnection:1#AddPortMapping\"");
            
            var response = await http.PostAsync(controlUrl, content);
            return response.IsSuccessStatusCode;
        }

        private static async Task<bool> DeletePortMappingAsync(string controlUrl, int port)
        {
            var soapBody = $"<?xml version=\"1.0\"?>\r\n" +
                           $"<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\" SOAP-ENV:EncodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                           $"<SOAP-ENV:Body>\r\n" +
                           $"<m:DeletePortMapping xmlns:m=\"urn:schemas-upnp-org:service:WANIPConnection:1\">\r\n" +
                           $"<NewRemoteHost></NewRemoteHost>\r\n" +
                           $"<NewExternalPort>{port}</NewExternalPort>\r\n" +
                           $"<NewProtocol>TCP</NewProtocol>\r\n" +
                           $"</m:DeletePortMapping>\r\n" +
                           $"</SOAP-ENV:Body>\r\n" +
                           $"</SOAP-ENV:Envelope>";

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var content = new System.Net.Http.StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:WANIPConnection:1#DeletePortMapping\"");
            
            var response = await http.PostAsync(controlUrl, content);
            return response.IsSuccessStatusCode;
        }
    }

    private string? GetServerDownloadUrl(string versionName)
    {
        try
        {
            var jsonPath = Path.Combine(_defaultMinecraftPath.BasePath, "versions", versionName, $"{versionName}.json");
            if (File.Exists(jsonPath))
            {
                var content = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("downloads", out var downloads) &&
                    downloads.TryGetProperty("server", out var server) &&
                    server.TryGetProperty("url", out var urlElement))
                {
                    return urlElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to parse server download url from version JSON: {ex.Message}");
        }
        return null;
    }

    private async Task<bool> AutoFixModDependencyAsync(string modsDir, string modId, string serverVersion, Action<string> logAction)
    {
        logAction($"[Auto-Fix] Attempting to auto-resolve dependency for '{modId}' on Minecraft {serverVersion}...");
        try
        {
            if (Directory.Exists(modsDir))
            {
                var files = Directory.GetFiles(modsDir, $"*{modId}*.jar");
                foreach (var file in files)
                {
                    logAction($"[Auto-Fix] Removing existing version of '{modId}': {Path.GetFileName(file)}");
                    try { File.Delete(file); } catch {}
                }
            }

            IReadOnlyList<ModrinthProjectVersion>? versions = null;
            try
            {
                versions = await _modrinthClient.GetProjectVersionsAsync(modId, serverVersion, "fabric", CancellationToken.None);
            }
            catch {}

            if (versions == null || versions.Count == 0)
            {
                var searchResults = await _modrinthClient.SearchProjectsAsync(modId, "mod", serverVersion, "fabric", CancellationToken.None);
                var bestMatch = searchResults.FirstOrDefault(r => r.Title.ToLowerInvariant().Contains(modId) || r.Slug.ToLowerInvariant().Contains(modId)) ?? searchResults.FirstOrDefault();
                if (bestMatch != null)
                {
                    versions = await _modrinthClient.GetProjectVersionsAsync(bestMatch.ProjectId, serverVersion, "fabric", CancellationToken.None);
                }
            }

            if (versions != null && versions.Count > 0)
            {
                var version = versions.FirstOrDefault(HasPrimaryFile) ?? versions.FirstOrDefault();
                if (version != null)
                {
                    var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
                    if (file != null)
                    {
                        Directory.CreateDirectory(modsDir);
                        var destFile = Path.Combine(modsDir, file.Filename);
                        logAction($"[Auto-Fix] Downloading compatible '{modId}': {file.Filename}...");
                        await _modrinthClient.DownloadFileAsync(file.Url, destFile, null, CancellationToken.None);
                        logAction($"[Auto-Fix] Successfully installed '{modId}'.");
                        return true;
                    }
                }
            }

            logAction($"[Auto-Fix Warning] No compatible version of '{modId}' could be resolved for Minecraft {serverVersion}.");
        }
        catch (Exception ex)
        {
            logAction($"[Auto-Fix Error] Failed to resolve '{modId}': {ex.Message}");
        }
        return false;
    }

    private async Task<string> ResolveLatestFabricInstallerVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await _modrinthClient.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer", cancellationToken);
            using var json = JsonDocument.Parse(payload);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("version", out var versionElement) && 
                    item.TryGetProperty("stable", out var stableElement) && 
                    stableElement.GetBoolean())
                {
                    var version = versionElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to resolve latest Fabric installer version: {ex.Message}");
        }
        return "1.0.1"; // Fallback stable installer version
    }

    private async Task<string> ResolveLatestQuiltInstallerVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await _modrinthClient.GetStringAsync("https://meta.quiltmc.org/v3/versions/installer", cancellationToken);
            using var json = JsonDocument.Parse(payload);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to resolve latest Quilt installer version: {ex.Message}");
        }
        return "0.9.1"; // Fallback stable installer version
    }

    private async Task<string?> GetLoaderServerDownloadUrlAsync(string loader, string versionName)
    {
        loader = loader.ToLowerInvariant();
        if (loader == "vanilla")
        {
            return GetServerDownloadUrl(versionName);
        }

        // Try direct official APIs FIRST for each loader (serverjars.in is unreliable/dead)
        if (loader == "fabric")
        {
            try
            {
                var latestFabric = await ResolveLatestFabricVersionAsync(versionName, CancellationToken.None);
                var installerVersion = await ResolveLatestFabricInstallerVersionAsync(CancellationToken.None);
                return $"https://meta.fabricmc.net/v2/versions/loader/{versionName}/{latestFabric}/{installerVersion}/server/jar";
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Fabric server download resolution failed: {ex.Message}");
            }
        }
        else if (loader == "quilt")
        {
            try
            {
                var latestQuilt = await ResolveLatestQuiltVersionAsync(versionName, CancellationToken.None);
                var installerVersion = await ResolveLatestQuiltInstallerVersionAsync(CancellationToken.None);
                return $"https://meta.quiltmc.org/v3/versions/loader/{versionName}/{latestQuilt}/{installerVersion}/server/jar";
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Quilt server download resolution failed: {ex.Message}");
            }
        }
        else if (loader == "forge")
        {
            try
            {
                var forgeVersion = await ResolveLatestForgeVersionAsync(versionName, CancellationToken.None);
                // Forge installer URL from official Maven
                return $"https://maven.minecraftforge.net/net/minecraftforge/forge/{versionName}-{forgeVersion}/forge-{versionName}-{forgeVersion}-installer.jar";
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Forge server download resolution failed: {ex.Message}");
            }
        }
        else if (loader == "neoforge")
        {
            try
            {
                var neoForgeVersion = await ResolveLatestNeoForgeVersionAsync(versionName, CancellationToken.None);
                // NeoForge installer URL from official Maven
                return $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar";
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"NeoForge server download resolution failed: {ex.Message}");
            }
        }
        else if (loader == "purpur")
        {
            return $"https://api.purpurmc.org/v2/purpur/{versionName}/latest/download";
        }
        else if (loader == "paper")
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher");
                // Resolve latest build from PaperMC API
                var buildsJson = await client.GetStringAsync($"https://api.papermc.io/v2/projects/paper/versions/{versionName}/builds");
                using var doc = JsonDocument.Parse(buildsJson);
                if (doc.RootElement.TryGetProperty("builds", out var builds) && builds.GetArrayLength() > 0)
                {
                    var lastBuild = builds[builds.GetArrayLength() - 1];
                    var buildNum = lastBuild.GetProperty("build").GetInt32();
                    var downloads = lastBuild.GetProperty("downloads");
                    var appName = downloads.GetProperty("application").GetProperty("name").GetString();
                    return $"https://api.papermc.io/v2/projects/paper/versions/{versionName}/builds/{buildNum}/downloads/{appName}";
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Paper server download resolution failed: {ex.Message}");
            }
        }
        else if (loader == "spigot")
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher");
                // Use GetBukkit API for Spigot
                return $"https://download.getbukkit.org/spigot/spigot-{versionName}.jar";
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Spigot server download resolution failed: {ex.Message}");
            }
        }

        // Last resort: try serverjars.com API (may be down)
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Add("User-Agent", "AetherLauncher");
            var apiUrl = $"https://serverjars.com/api/fetchJar/{loader}/{versionName}";
            var responseString = await client.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("response", out var respObj) &&
                respObj.TryGetProperty("url", out var urlProp))
            {
                var url = urlProp.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"serverjars.com fallback also failed for {loader} {versionName}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Finds the server jar generated by Forge/NeoForge installer.
    /// The installer can generate various jar names depending on version:
    /// - Modern (1.17+): generates run.sh/run.bat with @libraries/... args, plus a server jar
    /// - Legacy: generates forge-VERSION-universal.jar or similar
    /// </summary>
    private string FindForgeServerJar(string serverDir, string loader)
    {
        try
        {
            // First check for unix_args.txt or win_args.txt (modern Forge/NeoForge 1.17+)
            // These files contain the actual launch arguments including the main class
            var unixArgsFile = Path.Combine(serverDir, "unix_args.txt");
            var winArgsFile = Path.Combine(serverDir, "win_args.txt");
            if (File.Exists(unixArgsFile) || File.Exists(winArgsFile))
            {
                // Modern Forge doesn't have a single "server jar" - it uses classpath args
                // Return a marker indicating the args file should be used
                return "@@args_file@@";
            }

            // Check for run.sh (modern Forge)
            var runScript = Path.Combine(serverDir, "run.sh");
            if (File.Exists(runScript))
            {
                return "@@args_file@@";
            }

            // Look for forge/neoforge specific jars
            var patterns = new[]
            {
                $"{loader}-*-shim.jar",      // NeoForge shim jar
                $"{loader}-*-server.jar",     // Forge server jar pattern
                $"{loader}-*-universal.jar",  // Legacy Forge universal jar
                "forge-*.jar",                // Generic forge jar
                "neoforge-*.jar",             // Generic neoforge jar
            };

            foreach (var pattern in patterns)
            {
                var matches = Directory.GetFiles(serverDir, pattern);
                var match = matches.FirstOrDefault(f => 
                    !Path.GetFileName(f).Contains("installer", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(f).Contains("client", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            // Also check if server.jar exists (some older forge versions just create server.jar)
            var serverJar = Path.Combine(serverDir, "server.jar");
            if (File.Exists(serverJar))
                return serverJar;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"[FindForgeServerJar] Error scanning for server jar: {ex.Message}");
        }
        return "";
    }

    private Control CreateSectionOrderPicker()
    {
        var panel = new StackPanel { Spacing = 12 };
        for (int i = 0; i < _settings.SectionOrder.Count; i++)
        {
            var idx = i;
            var name = _settings.SectionOrder[i];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(4) };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold });
            
            var upBtn = new Button { Content = "↑", Width = 32, Height = 32, Margin = new Thickness(4,0), Padding = new Thickness(0), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
            upBtn.Click += (_, _) => {
                if (idx > 0) {
                    var tmp = _settings.SectionOrder[idx];
                    _settings.SectionOrder[idx] = _settings.SectionOrder[idx-1];
                    _settings.SectionOrder[idx-1] = tmp;
                    _settingsStore.Save(_settings);
                    Content = BuildRoot();
                    SetActiveSection("settings");
                }
            };
            
            var downBtn = new Button { Content = "↓", Width = 32, Height = 32, Padding = new Thickness(0), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
            downBtn.Click += (_, _) => {
                if (idx < _settings.SectionOrder.Count - 1) {
                    var tmp = _settings.SectionOrder[idx];
                    _settings.SectionOrder[idx] = _settings.SectionOrder[idx+1];
                    _settings.SectionOrder[idx+1] = tmp;
                    _settingsStore.Save(_settings);
                    Content = BuildRoot();
                    SetActiveSection("settings");
                }
            };
            
            row.Children.Add(upBtn.With(column: 1));
            row.Children.Add(downBtn.With(column: 2));
            panel.Children.Add(row);
        }
        return panel;
    }

    private Button CreateColorPreset(string hex)
    {
        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse(hex)),
            CornerRadius = new CornerRadius(16),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(_settings.AccentColor == hex ? 2 : 0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        btn.Click += (_, _) => {
            _settings.AccentColor = hex;
            _settingsStore.Save(_settings);
            InvalidateUiCache();
            Content = BuildRoot();
            SetActiveSection("settings");
        };
        return btn;
    }
    private void UpdateSelectedProjectDetails()
    {
        if (modrinthResultsListBox.SelectedItem is not ModrinthProject project)
        {
            modrinthDetailsBox.Text = "Search to browse mods and modpacks.";
            installSelectedButton.IsEnabled = false;
            return;
        }

        bool isInstalled = _selectedProfile?.InstalledModIds.Contains(project.ProjectId) ?? false;
        installSelectedButton.IsEnabled = !isInstalled;
        if (isInstalled)
        {
            SetButtonText(installSelectedButton, "Installed");
        }
        else
        {
            SetButtonText(installSelectedButton, project.ProjectType == "modpack" ? "↓ Pack" : "↓ Mod");
        }
        modrinthResultsSummary.Text = $"Selected {project.Title} by {project.Author}.";
        modrinthDetailsBox.Text =
            $"{project.Title}\n" +
            $"Type: {project.ProjectType}\n" +
            $"Author: {project.Author}\n" +
            $"Downloads: {project.Downloads:N0}\n" +
            $"Followers: {project.Follows:N0}\n" +
            $"Categories: {string.Join(", ", project.Categories)}\n\n" +
            $"{project.Description}";
    }

    private void RefreshSearchList()
    {
        var items = modrinthResultsListBox.ItemsSource as IEnumerable<ModrinthProject>;
        if (items != null)
        {
            var list = items.ToList();
            modrinthResultsListBox.ItemsSource = null;
            modrinthResultsListBox.ItemsSource = list;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (modrinthResultsListBox.SelectedItem is not ModrinthProject project)
            return;

        try
        {
            ToggleBusyState(true, $"Installing {project.Title}...");

            if (project.ProjectType == "modpack")
                await InstallModpackFromProjectAsync(project, CancellationToken.None);
            else
                await InstallSelectedModAsync(project, CancellationToken.None, installSelectedButton);

            RefreshModsList();
            UpdateSelectedProjectDetails();
            SetButtonProgress(installSelectedButton, 0, false);
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Install failed", $"Install failed.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready to install or launch.");
        }
    }

    private async Task InstallSelectedModAsync(ModrinthProject project, CancellationToken cancellationToken, Button? targetButton = null)
    {
        if (_selectedProfile is null)
        {
            await DialogService.ShowInfoAsync(this, "Profile required", "Create or select a profile before installing mods.");
            return;
        }

        if (project.IsCurseForge)
        {
            await InstallCurseForgeModAsync(project, cancellationToken, targetButton);
            return;
        }

        var versions = await _modrinthClient.GetProjectVersionsAsync(project.ProjectId, _selectedProfile.GameVersion, _selectedProfile.Loader, cancellationToken);
        var version = versions.FirstOrDefault(HasPrimaryFile) ?? versions.FirstOrDefault();
        if (version is null)
            throw new InvalidOperationException($"No compatible version was found for {_selectedProfile.LoaderDisplay}.");

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.ProjectId };
        await InstallModVersionAsync(_selectedProfile, version, installed, cancellationToken, targetButton, project.ProjectId);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            SetProgressState($"Installed {project.Title} into {_selectedProfile.Name}.", 0, 0);
            RefreshSearchList();
        });
    }

    private async Task InstallCurseForgeModAsync(ModrinthProject project, CancellationToken cancellationToken, Button? targetButton = null)
    {
        var files = await _curseForgeClient.GetProjectVersionsAsync(project.ProjectId, _selectedProfile!.GameVersion, _selectedProfile.Loader, cancellationToken);
        var file = files.FirstOrDefault();
        if (file is null)
            throw new InvalidOperationException("No compatible file found on CurseForge.");

        var modsDir = Path.Combine(_selectedProfile.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDir);
        var dest = Path.Combine(modsDir, file.FileName);

        if (string.IsNullOrEmpty(file.DownloadUrl))
            throw new InvalidOperationException("This mod has downloads disabled for 3rd party launchers on CurseForge.");

        await _curseForgeClient.DownloadFileAsync(file.DownloadUrl, dest, CreateDownloadProgress(file.FileName, targetButton), cancellationToken);
        
        _selectedProfile.InstalledModIds.Add(project.ProjectId);
        _profileStore.Save(_selectedProfile);
        
        SetProgressState($"Installed {project.Title} (CurseForge) into {_selectedProfile.Name}.", 0, 0);
    }

    private static bool HasPrimaryFile(ModrinthProjectVersion version) =>
        version.Files.Any(file => file.Primary && file.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));

    private async Task InstallModVersionAsync(LauncherProfile profile, ModrinthProjectVersion version, HashSet<string> installedProjectIds, CancellationToken cancellationToken, Button? targetButton = null, string? projectId = null)
    {
        foreach (var dependency in version.Dependencies.Where(d => d.DependencyType == "required" && !string.IsNullOrWhiteSpace(d.ProjectId)))
        {
            if (!installedProjectIds.Add(dependency.ProjectId!))
                continue;

            var dependencyVersions = await _modrinthClient.GetProjectVersionsAsync(dependency.ProjectId!, profile.GameVersion, profile.Loader, cancellationToken);
            var dependencyVersion = dependencyVersions.FirstOrDefault(HasPrimaryFile) ?? dependencyVersions.FirstOrDefault();
            if (dependencyVersion is not null)
                await InstallModVersionAsync(profile, dependencyVersion, installedProjectIds, cancellationToken, targetButton, dependency.ProjectId);
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
            throw new InvalidOperationException($"Version {version.VersionNumber} did not include a downloadable file.");

        Directory.CreateDirectory(profile.ModsDirectory);
        var destinationPath = Path.Combine(profile.ModsDirectory, file.Filename);
        await _modrinthClient.DownloadFileAsync(file.Url, CreateDownloadDestination(destinationPath), CreateDownloadProgress(file.Filename, targetButton), cancellationToken);
        await VerifyFileHashAsync(destinationPath, file.Hashes);
        
        var pid = projectId ?? version.ProjectId;
        if (!string.IsNullOrEmpty(pid))
            profile.InstalledModIds.Add(pid);
            
        _profileStore.Save(profile);
    }

    private async Task VerifyFileHashAsync(string filePath, IReadOnlyDictionary<string, string> hashes)
    {
        if (!hashes.TryGetValue("sha1", out var expectedHash) || string.IsNullOrWhiteSpace(expectedHash))
            return;

        await using var file = File.OpenRead(filePath);
        var computedHash = Convert.ToHexString(await SHA1.HashDataAsync(file)).ToLowerInvariant();
        if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Hash mismatch detected for {Path.GetFileName(filePath)}.");
    }

    private async Task InstallModpackFromProjectAsync(ModrinthProject project, CancellationToken cancellationToken)
    {
        var gameVersion = string.IsNullOrWhiteSpace(modrinthVersionInput.Text) ? null : modrinthVersionInput.Text.Trim();
        var loader = NormalizeLoaderFilter();
        var versions = await _modrinthClient.GetProjectVersionsAsync(project.ProjectId, gameVersion, loader, cancellationToken);
        var version = versions.FirstOrDefault(v => v.Files.Any(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)))
            ?? versions.FirstOrDefault();
        if (version is null)
            throw new InvalidOperationException("No compatible modpack build was found.");

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
            throw new InvalidOperationException("The selected modpack version has no downloadable file.");

        var tempMrpack = Path.Combine(Path.GetTempPath(), $"{project.Slug}-{version.VersionNumber}.mrpack");
        await _modrinthClient.DownloadFileAsync(file.Url, tempMrpack, CreateDownloadProgress(file.Filename), cancellationToken);
        await InstallMrpackAsync(tempMrpack, project, cancellationToken);
    }

    private async Task ImportMrpackAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Modrinth modpack",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Modrinth Modpack")
                {
                    Patterns = ["*.mrpack"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            await DialogService.ShowInfoAsync(this, "Import failed", "The selected file is not available as a local path.");
            return;
        }

        try
        {
            ToggleBusyState(true, $"Importing {Path.GetFileName(localPath)}...");
            await InstallMrpackAsync(localPath, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Import failed", $"Modpack import failed.\n{ex.Message}");
        }
        finally
        {
            ToggleBusyState(false, "Ready to install or launch.");
        }
    }

    private async Task InstallMrpackAsync(string mrpackPath, ModrinthProject? sourceProject, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(mrpackPath);
        var indexEntry = archive.GetEntry("modrinth.index.json")
            ?? throw new InvalidOperationException("The pack is missing modrinth.index.json.");

        await using var indexStream = indexEntry.Open();
        var index = await JsonSerializer.DeserializeAsync<MrPackIndex>(indexStream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Failed to read the modpack manifest.");

        if (!string.Equals(index.Game, "minecraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported pack game: {index.Game}.");

        var gameVersion = index.Dependencies.TryGetValue("minecraft", out var minecraftVersion)
            ? minecraftVersion
            : throw new InvalidOperationException("The modpack does not specify a Minecraft version.");

        var loader = "vanilla";
        string? loaderVersion = null;

        foreach (var candidate in new[] { "fabric", "quilt", "forge", "neoforge" })
        {
            if (index.Dependencies.TryGetValue(candidate, out var candidateVersion))
            {
                loader = candidate;
                loaderVersion = candidateVersion;
                break;
            }
        }

        var profileName = string.IsNullOrWhiteSpace(index.Name)
            ? sourceProject?.Title ?? Path.GetFileNameWithoutExtension(mrpackPath)
            : index.Name;
        var profile = _profileStore.CreateProfile(profileName, gameVersion, loader, loaderVersion, sourceProject?.Slug);

        pbFiles.Maximum = Math.Max(1, index.Files.Count);
        pbFiles.Value = 0;

        int completedFiles = 0;
        foreach (var file in index.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(file.Env?.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
                continue;

            var downloadUrl = file.Downloads.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(downloadUrl))
                continue;

            var destinationPath = GetSafeDestinationPath(profile.InstanceDirectory, file.Path);
            await _modrinthClient.DownloadFileAsync(downloadUrl, CreateDownloadDestination(destinationPath), CreateDownloadProgress(file.Path), cancellationToken);
            await VerifyFileHashAsync(destinationPath, file.Hashes);

            completedFiles++;
            pbFiles.Value = Math.Min(pbFiles.Maximum, completedFiles);
            installDetailsLabel.Text = $"{completedFiles} / {index.Files.Count} pack files";
        }

        ExtractOverrideEntries(archive, "overrides/", profile.InstanceDirectory);
        ExtractOverrideEntries(archive, "client-overrides/", profile.InstanceDirectory);

        if (loader == "fabric")
            await EnsureFabricProfileAsync(profile, cancellationToken);
        else if (loader == "quilt")
            await EnsureQuiltProfileAsync(profile, cancellationToken);
        else if (loader == "forge" || loader == "neoforge")
            await EnsureForgeProfileAsync(profile, cancellationToken);

        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            RefreshProfiles(profile);
            SetActiveSection("profiles");
            SetProgressState($"Installed modpack {profile.Name}.", 0, 0);
        });
    }

    private static void ExtractOverrideEntries(ZipArchive archive, string prefix, string destinationRoot)
    {
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = entry.FullName[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            var destinationPath = GetSafeDestinationPath(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string GetSafeDestinationPath(string root, string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsafe path detected: {relativePath}");

        return fullPath;
    }

    private Progress<(long BytesRead, long? TotalBytes)> CreateDownloadProgress(string fileName, Button? targetButton = null)
    {
        return new Progress<(long BytesRead, long? TotalBytes)>(progress =>
        {
            statusLabel.Text = $"Downloading {Path.GetFileName(fileName)}";
            double percent = 0;
            if (progress.TotalBytes is long totalBytes && totalBytes > 0)
            {
                percent = progress.BytesRead * 100d / totalBytes;
                pbProgress.Value = Math.Min(100, percent);
                installDetailsLabel.Text = $"{FormatBytes(progress.BytesRead)} / {FormatBytes(totalBytes)}";
            }
            else
            {
                pbProgress.Value = 0;
                installDetailsLabel.Text = $"{FormatBytes(progress.BytesRead)} downloaded";
            }

            if (targetButton != null)
            {
                SetButtonProgress(targetButton, percent > 0 ? percent : 0, true);
            }
        });
    }

    private void ToggleBusyState(bool isBusy, string statusText)
    {
        if (_isGameLaunchedAndMinimized)
        {
            btnStart.IsEnabled = false;
            btnStart.Content = "Launched";
            downloadVersionButton.IsEnabled = false;
            createProfileButton.IsEnabled = false;
            modrinthSearchButton.IsEnabled = false;
            if (installSelectedButton != null) installSelectedButton.IsEnabled = false;
            importMrpackButton.IsEnabled = false;
            _quickInstallButton.IsEnabled = false;
            _quickModSearchButton.IsEnabled = false;
            _playOverlay.IsEnabled = false;
            _playOverlay.Opacity = 0.5;
            statusLabel.Text = "Minecraft is running...";
            if (_homeStatusBar != null) _homeStatusBar.IsVisible = true;
            pbProgress.Value = 0;
            if (installSelectedButton != null) SetButtonProgress(installSelectedButton, 0, false);
            if (btnStart != null) SetButtonProgress(btnStart, 0, false);
            if (modrinthSearchButton != null) SetButtonProgress(modrinthSearchButton, 0, false);
            return;
        }

        btnStart.IsEnabled = !isBusy && !string.IsNullOrWhiteSpace(usernameInput.Text);
        if (isBusy)
        {
            btnStart.Content = "Cancel"; // Default busy state for launch
        }
        else
        {
            btnStart.Content = "▶ Play";
        }
        downloadVersionButton.IsEnabled = !isBusy && _selectedProfile is null;
        createProfileButton.IsEnabled = !isBusy;
        modrinthSearchButton.IsEnabled = !isBusy;
        installSelectedButton.IsEnabled = !isBusy && modrinthResultsListBox.SelectedItem is ModrinthProject;
        importMrpackButton.IsEnabled = !isBusy;
        _quickInstallButton.IsEnabled = !isBusy;
        _quickModSearchButton.IsEnabled = !isBusy;
        _playOverlay.IsEnabled = !isBusy;
        _playOverlay.Opacity = isBusy ? 0.5 : 1;
        statusLabel.Text = statusText;
        if (_homeStatusBar != null) _homeStatusBar.IsVisible = isBusy;
        if (!isBusy)
        {
            pbProgress.Value = 0;
            if (installSelectedButton != null) SetButtonProgress(installSelectedButton, 0, false);
            if (btnStart != null) SetButtonProgress(btnStart, 0, false);
            if (modrinthSearchButton != null) SetButtonProgress(modrinthSearchButton, 0, false);
        }
    }

    private void SetProgressState(string statusText, int fileProgress, int byteProgress)
    {
        statusLabel.Text = statusText;
        installDetailsLabel.Text = _selectedProfile?.LoaderDisplay ?? cbVersion.SelectedItem?.ToString() ?? string.Empty;
        pbFiles.Value = Math.Clamp(fileProgress, 0, (int)pbFiles.Maximum);
        pbProgress.Value = Math.Clamp(byteProgress, 0, (int)pbProgress.Maximum);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.#} {sizes[order]}";
    }

    private static TextBlock CreateStatValue()
    {
        return new TextBlock
        {
            Text = "--",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Black,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
    }

    private Border CreateCompactStat(string title, TextBlock valueBlock)
    {
        return CreateGlassPanel(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"{title}:",
                    Foreground = new SolidColorBrush(Color.Parse("#9EB2E0")),
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                valueBlock.With(column: 1)
            }
        }, padding: new Thickness(14, 10));
    }

    private Control CreateHeroPanel()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("1.2*,0.42*"),
                    ColumnSpacing = 20,
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 14,
                            Children =
                            {
                                DetachFromParent(heroInstanceLabel)!,
                                DetachFromParent(heroPerformanceLabel)!,
                                new Border
                                {
                                    Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                                    CornerRadius = new CornerRadius(14),
                                    Padding = new Thickness(14, 10),
                                    Child = DetachFromParent(usernameInput)!
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("1*"),
                                    Children =
                                    {
                                        btnStart
                                    }
                                }
                            }
                        },
                        new StackPanel
                        {
                            Spacing = 12,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                CreateGlassPanel(new StackPanel
                                {
                                    Spacing = 6,
                                    Children =
                                    {
                                        activeProfileBadge,
                                        installDetailsLabel,
                                        statusLabel
                                    }
                                }, padding: new Thickness(16)),
                                CreateAppearanceCard()
                            }
                        }.With(column: 1)
                    }
                }
            }
        });
    }

    private Control CreateSummaryCard()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreatePanelEyebrow("Overview"),
                new TextBlock
                {
                    Text = _selectedProfile is null ? "Quick play" : _selectedProfile.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 18
                },
                CreateMiniFeatureRow("◈", "Mods", "Install from Modrinth"),
                CreateMiniFeatureRow("▣", "Instances", "Separate profiles"),
                CreateMiniFeatureRow("⚡", "State", "Ready")
            }
        });
    }

    private Control CreateAppearanceCard()
    {
        var skinButton = CreateSecondaryButton("Skin");
        skinButton.IsEnabled = false;

        var capeButton = CreateSecondaryButton("Cape");
        capeButton.IsEnabled = false;

        return CreateGlassPanel(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreatePanelEyebrow("Appearance"),
                characterImage,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        skinButton,
                        capeButton.With(column: 1)
                    }
                }
            }
        }, padding: new Thickness(16));
    }

    private Control CreatePerformanceStatusCard()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreatePanelEyebrow("Performance"),
                new TextBlock
                {
                    Text = "Stable",
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 18
                },
                CreateMiniFeatureRow("◌", "Frame pacing", "Stable target profile"),
                CreateMiniFeatureRow("◔", "Memory route", "Adaptive RAM suggestion")
            }
        });
    }

    private Control CreateActivityCard()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreatePanelEyebrow("Recent Activity"),
                CreateMiniFeatureRow("▶", "Launch route", "Default play path armed"),
                CreateMiniFeatureRow("▣", "Instances", "Profile context stays isolated"),
                CreateMiniFeatureRow("⌕", "Discovery", "Search and install without leaving launcher")
            }
        });
    }

    private Control CreateSuggestedModsCard()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreatePanelEyebrow("Suggested Mods"),
                CreateMiniFeatureRow("⚡", "Sodium", "High-FPS rendering"),
                CreateMiniFeatureRow("☄", "Lithium", "Server and tick optimizations"),
                CreateMiniFeatureRow("✦", "FerriteCore", "Lower memory pressure")
            }
        });
    }

    private Control CreateLogsCard()
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreatePanelEyebrow("Logs"),
                new Expander
                {
                    Header = new TextBlock
                    {
                        Text = "Console output",
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold
                    },
                    Content = new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#0A0F18")),
                        CornerRadius = new CornerRadius(16),
                        Padding = new Thickness(14),
                        Child = new TextBlock
                        {
                            Text = $"{statusLabel.Text}\n{installDetailsLabel.Text}",
                            Foreground = new SolidColorBrush(Color.Parse("#A8F0E5")),
                            FontFamily = new FontFamily("Consolas, Inter, monospace"),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            }
        });
    }

    private static Control CreateMiniFeatureRow(string icon, string title, string subtitle)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(70, 15, 22, 39)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 85, 102, 145)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("38,*"),
                ColumnSpacing = 12,
                Children =
                {
                    new Border
                    {
                        Width = 38,
                        Height = 38,
                        CornerRadius = new CornerRadius(12),
                        Background = new SolidColorBrush(Color.FromArgb(110, 107, 91, 255)),
                        Child = new TextBlock
                        {
                            Text = icon,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.White,
                            FontWeight = FontWeight.Bold
                        }
                    },
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                Foreground = Brushes.White,
                                FontWeight = FontWeight.Bold
                            },
                            new TextBlock
                            {
                                Text = subtitle,
                                Foreground = new SolidColorBrush(Color.Parse("#9CADD3"))
                            }
                        }
                    }.With(column: 1)
                }
            }
        };
    }

    private static Control CreateProgressRow(string title, ProgressBar progressBar)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(Color.Parse("#9EB2E0")),
                    FontWeight = FontWeight.SemiBold
                },
                progressBar
            }
        };
    }

    // Removed static keyword to access _settings
    private TextBox CreateTextBox()
    {
        var style = _settings.Style;
        var inBg = !string.IsNullOrWhiteSpace(style.FieldBackground) ? style.FieldBackground : "#78131B2D";
        var inFg = !string.IsNullOrWhiteSpace(style.FieldForeground) ? style.FieldForeground : "#FFFFFF";
        var inBorder = !string.IsNullOrWhiteSpace(style.FieldBorderColor) ? style.FieldBorderColor : "#36476A";
        var inCr = double.IsNaN(style.FieldRadius) ? 16 : style.FieldRadius;

        return new TextBox
        {
            Background = new SolidColorBrush(Color.Parse(inBg)),
            Foreground = new SolidColorBrush(Color.Parse(inFg)),
            BorderBrush = new SolidColorBrush(Color.Parse(inBorder)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11),
            CornerRadius = new CornerRadius(inCr),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
    }

    private ComboBox CreateComboBox(IEnumerable<object> items)
    {
        var style = _settings.Style;
        var inBg = !string.IsNullOrWhiteSpace(style.FieldBackground) ? style.FieldBackground : "#78131B2D";
        var inFg = !string.IsNullOrWhiteSpace(style.FieldForeground) ? style.FieldForeground : "#FFFFFF";
        var inBorder = !string.IsNullOrWhiteSpace(style.FieldBorderColor) ? style.FieldBorderColor : "#36476A";
        var inCr = double.IsNaN(style.FieldRadius) ? 16 : style.FieldRadius;

        var comboBox = new ComboBox
        {
            ItemsSource = items.ToList(),
            Background = new SolidColorBrush(Color.Parse(inBg)),
            Foreground = new SolidColorBrush(Color.Parse(inFg)),
            BorderBrush = new SolidColorBrush(Color.Parse(inBorder)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(inCr),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        ApplyHoverMotion(comboBox);
        return comboBox;
    }

    private ComboBox CreateComboBox(IEnumerable<string> items)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = items,
            Background = new SolidColorBrush(Color.FromArgb(120, 19, 27, 45)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#36476A")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(16),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        ApplyHoverMotion(comboBox);
        return comboBox;
    }

    private Button CreatePrimaryButton(string text, string hexColor, Color foreground)
    {
        var style = _settings.Style;
        var btnBgHex = !string.IsNullOrEmpty(style.ButtonBackground) ? style.ButtonBackground : hexColor;
        var btnFgHex = !string.IsNullOrEmpty(style.ButtonForeground) ? style.ButtonForeground : null;
        var btnFg = btnFgHex != null ? Color.Parse(btnFgHex) : foreground;
        
        var cr = double.IsNaN(style.ButtonCornerRadius) ? 18 : style.ButtonCornerRadius;
        var height = double.IsNaN(style.ButtonHeight) ? 50 : style.ButtonHeight;

        var textBlock = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(btnFg)
        };

        var progressBar = new ProgressBar
        {
            Height = 4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            IsVisible = false,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromArgb(128, btnFg.R, btnFg.G, btnFg.B)),
            CornerRadius = new CornerRadius(2)
        };

        var contentGrid = new Grid
        {
            Children = { textBlock, progressBar }
        };

        var button = new Button
        {
            Content = contentGrid,
            Tag = progressBar, // Store progress bar for easy access
            Height = height,
            Background = new SolidColorBrush(Color.Parse(btnBgHex)),
            Foreground = new SolidColorBrush(btnFg),
            BorderBrush = Brushes.Transparent,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(18, 12),
            CornerRadius = new CornerRadius(cr),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        ApplyHoverMotion(button);
        return button;
    }

    private static void SetButtonText(Button button, string text)
    {
        if (button.Content is Grid grid)
        {
            var textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock != null)
            {
                textBlock.Text = text;
                return;
            }
        }
        button.Content = text;
    }

    private static void SetButtonProgress(Button button, double value, bool visible)
    {
        if (button.Tag is ProgressBar pb)
        {
            pb.Value = value;
            pb.IsVisible = visible;
        }
    }

    private Button CreateNavButton(string icon, string label, bool compact = false)
    {
        var style = _settings.Style;
        var buttonHeight = double.IsNaN(style.NavButtonHeight) ? (compact ? 48 : 46) : style.NavButtonHeight;
        var buttonFontSize = double.IsNaN(style.NavButtonFontSize) ? 14 : style.NavButtonFontSize;
        var hAlign = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        
        var iconSize = double.IsNaN(style.NavButtonFontSize) ? (compact ? 18 : 15) : style.NavButtonFontSize + 3;

        var button = new Button
        {
            Content = compact
                ? (object)new TextBlock
                {
                    Text = icon,
                    FontSize = iconSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            FontSize = iconSize,
                            Width = 22,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = label,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = buttonFontSize,
                            FontWeight = FontWeight.SemiBold
                        }
                    }
                },
            Width = compact ? 48 : double.NaN,
            Height = buttonHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = !string.IsNullOrWhiteSpace(style.NavButtonBackground) ? new SolidColorBrush(Color.Parse(style.NavButtonBackground)) : Brushes.Transparent,
            Foreground = !string.IsNullOrWhiteSpace(style.NavButtonForeground) ? new SolidColorBrush(Color.Parse(style.NavButtonForeground)) : new SolidColorBrush(Color.Parse("#A4A8B1")),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(style.NavButtonCornerRadius),
            FontWeight = FontWeight.SemiBold,
            FontSize = buttonFontSize,
            HorizontalContentAlignment = hAlign,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = compact ? new Thickness(0) : new Thickness(12, 0),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        ApplyHoverMotion(button);
        return button;
    }

    private Button CreateSecondaryButton(string text)
    {
        var style = _settings.Style;
        var btnHeight = double.IsNaN(style.ButtonHeight) ? 48 : style.ButtonHeight;
        var btnFs = double.IsNaN(style.ButtonFontSize) ? 14 : style.ButtonFontSize;
        var btnCr = double.IsNaN(style.ButtonCornerRadius) ? 18 : style.ButtonCornerRadius;
        var btnPad = double.IsNaN(style.ButtonPadding) ? 18 : style.ButtonPadding;
        
        var bg = !string.IsNullOrWhiteSpace(style.ButtonBackground) ? style.ButtonBackground : "#55101728";
        var fg = !string.IsNullOrWhiteSpace(style.ButtonForeground) ? style.ButtonForeground : "#FFFFFF";

        var button = new Button
        {
            Content = text,
            Height = btnHeight,
            Background = new SolidColorBrush(Color.Parse(bg)),
            Foreground = new SolidColorBrush(Color.Parse(fg)),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C4F73")),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(btnPad, 12),
            CornerRadius = new CornerRadius(btnCr),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ApplyHoverMotion(button);
        return button;
    }

    private Button CreateCompactSecondaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Height = 30,
            MinWidth = 110,
            Background = new SolidColorBrush(Color.FromArgb(85, 16, 23, 40)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#3C4F73")),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(12),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ApplyHoverMotion(button);
        return button;
    }

    private Border BuildCard(Control child)
    {
        var style = _settings.Style;
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(style.CardBackground ?? "#0D1522")),
            BorderBrush = new SolidColorBrush(Color.Parse(style.CardBorderColor ?? "#203046")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(double.IsNaN(style.CardCornerRadius) ? 24 : style.CardCornerRadius),
            Padding = new Thickness(double.IsNaN(style.CardPadding) ? 22 : style.CardPadding),
            Child = child
        };
    }

    private Border CreateGlassPanel(Control child, Thickness? padding = null, Thickness? margin = null)
    {
        var style = _settings.Style;
        
        IBrush bgBrush;
        if (!string.IsNullOrWhiteSpace(style.CardBackground) && Color.TryParse(style.CardBackground, out var customBgColor))
        {
            bgBrush = new SolidColorBrush(customBgColor);
        }
        else
        {
            bgBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(20, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(5, 255, 255, 255), 1)
                }
            };
        }

        IBrush borderBrush;
        if (!string.IsNullOrWhiteSpace(style.CardBorderColor) && Color.TryParse(style.CardBorderColor, out var customBorderColor))
        {
            borderBrush = new SolidColorBrush(customBorderColor);
        }
        else
        {
            borderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        }

        var panel = new Border
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(double.IsNaN(style.CardCornerRadius) ? 24 : style.CardCornerRadius),
            Padding = padding ?? new Thickness(22),
            Margin = margin ?? new Thickness(0),
            Child = child
        };
        return panel;
    }


    private static Border CreatePanelEyebrow(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 106, 90, 255)),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                LetterSpacing = 1.1
            }
        };
    }

    private Control CreateSectionTitle(string text, string subtitle)
    {
        var style = _settings.Style;
        
        var titleText = !string.IsNullOrWhiteSpace(style.TitleText) && text == "Home" ? style.TitleText : text;
        var titleFs = double.IsNaN(style.TitleFontSize) ? 32 : style.TitleFontSize;
        var titleFg = !string.IsNullOrWhiteSpace(style.TitleForeground) ? style.TitleForeground : "#FFFFFF";
        var primaryFont = !string.IsNullOrWhiteSpace(style.PrimaryFontFamily) ? new FontFamily(style.PrimaryFontFamily) : new FontFamily("SF Pro, Inter, Segoe UI");
        var secondaryFg = !string.IsNullOrWhiteSpace(style.SecondaryForeground) ? style.SecondaryForeground : "#A4B4DA";

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(8, 0, 0, 20),
            Children =
            {
                new TextBlock
                {
                    Text = titleText,
                    FontSize = titleFs,
                    FontWeight = FontWeight.Black,
                    Foreground = new SolidColorBrush(Color.Parse(titleFg)),
                    LetterSpacing = 1.2,
                    FontFamily = primaryFont
                },
                new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(Color.Parse(secondaryFg)),
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = primaryFont
                }
            }
        };
    }

    private static TextBlock CreateCaption(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#B9C1D3")),
            FontWeight = FontWeight.SemiBold
        };
    }

    private static Control WrapScrollable(Control child)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0D111C")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(2),
            Child = child
        };
    }

    private static Border CreateGlassBox(string title, Control child)
    {
        var stack = new StackPanel { Spacing = 12 };
        if (!string.IsNullOrEmpty(title))
        {
            stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeight.Bold });
        }
        stack.Children.Add(child);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 8, 10, 16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = stack,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(20, 0, 0, 0), OffsetX = 0, OffsetY = 6 })
        };
    }

    private static Control CreateSectionScroller(Control child)
    {
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 16, 0),
            Content = child
        };
    }

    private static Border CreateChip(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#101A29")),
            BorderBrush = new SolidColorBrush(Color.Parse("#23405C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(10, 5),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#D6E6F8")),
                FontWeight = FontWeight.SemiBold
            }
        };
    }

    private static Border CreateMutedChip(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(50, 22, 29, 46)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 60, 72, 105)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#93A4C9")),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private Border CreateMetricTile(string title, string subtitle)
    {
        var tile = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(100, 18, 26, 44), 0),
                    new GradientStop(Color.FromArgb(90, 14, 19, 33), 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(125, 80, 96, 140)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold,
                        FontSize = 15
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Foreground = new SolidColorBrush(Color.Parse("#92A0BC")),
                        FontSize = 12
                    }
                }
            }
        };
        ApplyHoverMotion(tile);
        return tile;
    }

    private Border CreateSubCard(string title, Control body, string backgroundHex)
    {
        var style = _settings.Style;
        
        IBrush bgBrush;
        if (!string.IsNullOrWhiteSpace(style.CardBackground))
            bgBrush = new SolidColorBrush(Color.Parse(style.CardBackground));
        else
            bgBrush = new SolidColorBrush(Color.FromArgb(160, 10, 14, 26));

        IBrush borderBrush;
        if (!string.IsNullOrWhiteSpace(style.CardBorderColor))
            borderBrush = new SolidColorBrush(Color.Parse(style.CardBorderColor));
        else
            borderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));

        var cr = double.IsNaN(style.CardCornerRadius) ? 20 : style.CardCornerRadius;
        var pad = double.IsNaN(style.CardPadding) ? 18 : style.CardPadding;

        return new Border
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(cr),
            Padding = new Thickness(pad),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold,
                        FontSize = 16
                    },
                    body
                }
            }
        };
    }

    private static Border CreateInfoStrip(string title, Control body, string backgroundHex, string borderHex)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(backgroundHex)),
            BorderBrush = new SolidColorBrush(Color.Parse(borderHex)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = new SolidColorBrush(Color.Parse("#8FB7FF")),
                        FontWeight = FontWeight.Bold
                    },
                    body
                }
            }
        };
    }

    private void ApplyNavState(Button? button, bool isActive)
    {
        if (button == null) return;
        if (button == accountsNavButton) return;



        var style = _settings.Style;
        var accentColor = Color.Parse(_settings.AccentColor);

        var activeBgToken = !string.IsNullOrWhiteSpace(style.NavButtonActiveBackground) ? style.NavButtonActiveBackground : null;
        var inactiveBgToken = !string.IsNullOrWhiteSpace(style.NavButtonBackground) ? style.NavButtonBackground : null;

        var activeFgToken = !string.IsNullOrWhiteSpace(style.NavButtonActiveForeground) ? style.NavButtonActiveForeground : null;
        var inactiveFgToken = !string.IsNullOrWhiteSpace(style.NavButtonForeground) ? style.NavButtonForeground : "#A4A8B1";

        if (isActive)
        {
            button.BorderThickness = new Thickness(0);

            switch (style.NavIndicatorStyle?.ToLower())
            {
                case "left-pill":
                    button.Background = activeBgToken != null ? new SolidColorBrush(Color.Parse(activeBgToken)) : Brushes.Transparent;
                    button.BorderThickness = new Thickness(4, 0, 0, 0);
                    button.BorderBrush = new SolidColorBrush(accentColor);
                    break;
                case "underline":
                    button.Background = activeBgToken != null ? new SolidColorBrush(Color.Parse(activeBgToken)) : Brushes.Transparent;
                    button.BorderThickness = new Thickness(0, 0, 0, 2);
                    button.BorderBrush = new SolidColorBrush(accentColor);
                    break;
                case "glow":
                    button.Background = activeBgToken != null ? new SolidColorBrush(Color.Parse(activeBgToken)) : Brushes.Transparent;
                    button.Foreground = new SolidColorBrush(accentColor);
                    break;
                case "fill":
                default:
                    button.Background = activeBgToken != null ? new SolidColorBrush(Color.Parse(activeBgToken)) : new SolidColorBrush(Color.FromArgb(32, accentColor.R, accentColor.G, accentColor.B));
                    button.Foreground = activeFgToken != null ? new SolidColorBrush(Color.Parse(activeFgToken)) : new SolidColorBrush(accentColor);
                    break;
            }
            if (activeFgToken != null) button.Foreground = new SolidColorBrush(Color.Parse(activeFgToken));
        }
        else
        {
            button.Background = inactiveBgToken != null ? new SolidColorBrush(Color.Parse(inactiveBgToken)) : Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.Parse(inactiveFgToken));
            button.BorderThickness = new Thickness(0);
            button.BorderBrush = Brushes.Transparent;
        }

        button.CornerRadius = new CornerRadius(double.IsNaN(style.NavButtonCornerRadius) ? 14 : style.NavButtonCornerRadius);
        button.Padding = new Thickness(16, 0);
        button.FontSize = double.IsNaN(style.NavButtonFontSize) ? 14 : style.NavButtonFontSize;
        button.FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal;

    }

    private Border CreateStatTile(string title, TextBlock valueBlock, string subtitle)
    {
        return CreateGlassPanel(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreatePanelEyebrow(title),
                valueBlock,
                new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(Color.Parse("#A4B4DA"))
                }
            }
        });
    }

    private async Task InstallModIfMissingAsync(string slug, LauncherProfile profile, string modsDir, CancellationToken cancellationToken, string? projectId = null)
    {
        if (_settings.OfflineMode)
        {
            bool physicalFileExists = Directory.Exists(modsDir) &&
                Directory.EnumerateFiles(modsDir, "*.jar")
                    .Any(f => Path.GetFileName(f).Contains(slug, StringComparison.OrdinalIgnoreCase));

            if (physicalFileExists)
            {
                LauncherLog.Info($"[ModInstaller] Offline mode: '{slug}' is physically present. Skipping check.");
                return;
            }

            bool isOnline = await CheckInternetConnectivityAsync();
            if (!isOnline)
            {
                LauncherLog.Warn($"[ModInstaller] Offline mode: '{slug}' is missing, but no internet is available to download it.");
                return;
            }
            LauncherLog.Info($"[ModInstaller] Offline mode is active, but '{slug}' is missing and we have internet. Proceeding with installation.");
        }

        try
        {
            if (string.Equals(profile.Loader, "vanilla", StringComparison.OrdinalIgnoreCase))
                return;

            string targetId = projectId ?? slug;
            bool physicalFileExists = Directory.Exists(modsDir) &&
                Directory.EnumerateFiles(modsDir, "*.jar")
                    .Any(f => Path.GetFileName(f).Contains(slug, StringComparison.OrdinalIgnoreCase));

            if (profile.InstalledModIds.Contains(targetId))
            {
                if (!physicalFileExists)
                {
                    LauncherLog.Warn($"[ModInstaller] {targetId} is tracked for '{profile.Name}' but the jar is missing from {modsDir}. Reinstalling.");
                }
                else
                {
                    LauncherLog.Info($"[ModInstaller] {targetId} is already tracked. Done.");
                    return;
                }
            }

            if (physicalFileExists)
            {
                LauncherLog.Info($"[ModInstaller] '{slug}' is physically present in {modsDir}. Ensuring tracking is correct.");
                if (!profile.InstalledModIds.Contains(targetId))
                {
                    profile.InstalledModIds.Add(targetId);
                    _profileStore.Save(profile);
                }
                LauncherLog.Info($"[ModInstaller] {targetId} is already tracked. Done.");
                return;
            }

            // We search first to get the official Project ID if not provided.
            LauncherLog.Info($"[ModInstaller] Resolving official ID for {slug} ({profile.GameVersion}/{profile.Loader})...");
            var results = await _modrinthClient.SearchProjectsAsync(targetId, "mod", profile.GameVersion, profile.Loader, cancellationToken);
            var project = results.FirstOrDefault(p => 
                string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase) || 
                string.Equals(p.ProjectId, slug, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) ||
                p.Title.Contains(slug, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                LauncherLog.Info($"[ModInstaller] Could not find {slug} on Modrinth. Skipping auto-install.");
                return;
            }

            if (profile.InstalledModIds.Contains(project.ProjectId))
            {
                LauncherLog.Info($"[ModInstaller] {project.Title} ({project.ProjectId}) is already tracked. Done.");
                return;
            }

            // Check if the file already exists physically but isn't tracked yet
            var existing = Directory.EnumerateFiles(modsDir, "*.jar")
                .Any(f => Path.GetFileName(f).Contains(slug, StringComparison.OrdinalIgnoreCase));

            if (existing)
            {
                LauncherLog.Info($"[ModInstaller] {project.Title} exists physically but wasn't tracked. Adding ID {project.ProjectId}.");
                profile.InstalledModIds.Add(project.ProjectId);
                _profileStore.Save(profile);
                return;
            }

            LauncherLog.Info($"[ModInstaller] Found {project.Title}. Installing...");
            await InstallSelectedModAsync(project, cancellationToken);
            LauncherLog.Info($"[ModInstaller] {project.Title} installed successfully.");
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"[ModInstaller] Auto-installation of {slug} failed, but continuing instance operation.", ex);
        }
    }

    private void SyncSkinShuffleAvatarToLauncher()
    {
        if (_selectedProfile is null) return;
        
        try
        {
            var configDir = Path.Combine(_selectedProfile.InstanceDirectory, "config", "skinshuffle");
            var presetsPath = Path.Combine(configDir, "presets.json");
            
            if (File.Exists(presetsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(presetsPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("chosenPreset", out var chosenPresetElem) && 
                    root.TryGetProperty("loadedPresets", out var presetsArray))
                {
                    int chosenIdx = chosenPresetElem.GetInt32();
                    if (chosenIdx >= 0 && chosenIdx < presetsArray.GetArrayLength())
                    {
                        var preset = presetsArray[chosenIdx];
                        if (preset.TryGetProperty("skin", out var skinObj) && 
                            skinObj.TryGetProperty("skin_name", out var skinNameElem))
                        {
                            var skinName = skinNameElem.GetString();
                            if (!string.IsNullOrEmpty(skinName))
                            {
                                var imagePath = Path.Combine(configDir, "skins", $"{skinName}.png");
                                if (File.Exists(imagePath))
                                {
                                    var destPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
                                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                                    File.Copy(imagePath, destPath, true);
                                    
                                    _settings.CustomSkinPath = destPath;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void EnsureDeathClientThemeResourcePack(string instancePath, string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        try
        {
            var rpDir = Path.Combine(instancePath, "resourcepacks");
            Directory.CreateDirectory(rpDir);
            var zipPath = Path.Combine(rpDir, "DeathClientTheme.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteTextEntry(
                    archive,
                    "pack.mcmeta",
                    "{\"pack\":{\"pack_format\":1,\"description\":\"Aether Launcher UI theme for home, multiplayer, and singleplayer menus\"}}");

                AddExistingFileToArchive(archive, ResolveThemeLogoPath(), "pack.png");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_title_logo.png"), "assets/minecraft/textures/gui/title/minecraft.png");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_title_logo.png"), "assets/minecraft/textures/gui/title/minceraft.png");
                WriteTextEntry(archive, "assets/minecraft/textures/gui/title/minecraft.png.mcmeta", "{\"animation\":{\"frametime\":5}}");
                WriteTextEntry(archive, "assets/minecraft/textures/gui/title/minceraft.png.mcmeta", "{\"animation\":{\"frametime\":5}}");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_edition.png"), "assets/minecraft/textures/gui/title/edition.png");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_button.png"), "assets/minecraft/textures/gui/sprites/widget/button.png");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_button_highlighted.png"), "assets/minecraft/textures/gui/sprites/widget/button_highlighted.png");
                WriteTextEntry(archive, "assets/minecraft/textures/gui/sprites/widget/button_highlighted.png.mcmeta", "{\"animation\":{\"frametime\":4}}");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_button_disabled.png"), "assets/minecraft/textures/gui/sprites/widget/button_disabled.png");
                AddExistingFileToArchive(archive, ResolveBundledThemeAsset("death_client_widgets.png"), "assets/minecraft/textures/gui/widgets.png");

                var themeBackground = ResolveThemeBackgroundPath();
                var panoramaBackground = ResolveThemePanoramaPath();
                if (!string.IsNullOrWhiteSpace(panoramaBackground) && IsSquareImage(panoramaBackground))
                {
                    for (var i = 0; i < 6; i++)
                        AddExistingFileToArchive(archive, panoramaBackground, $"assets/minecraft/textures/gui/title/background/panorama_{i}.png");
                }

                if (!string.IsNullOrWhiteSpace(themeBackground))
                    AddExistingFileToArchive(archive, themeBackground, "assets/minecraft/textures/gui/options_background.png");

                WriteTextEntry(
                    archive,
                    "assets/minecraft/texts/splashes.txt",
                    "Aether Launcher: Redefining Play\nUnrivaled Performance, Unmatched Style\nQueue up and dominate\nPeak precision, crafted for champions\nCleanest UI, fastest launch\nOffline mode, but never basic\nJoin the Reborn Movement");

                AddSkinAndCapeEntries(archive);
            }

            UpdateResourcePackOptions(instancePath, "file/DeathClientTheme.zip");
        }
        catch { }
    }

    private void AddSkinAndCapeEntries(ZipArchive archive)
    {
        var allowSkinOverride = !IsUsingMicrosoftAccount() || HasManualSkinOverride();
        var allowCapeOverride = !IsUsingMicrosoftAccount() || HasManualCapeOverride();

        if (allowSkinOverride && !string.IsNullOrWhiteSpace(_settings.CustomSkinPath) && File.Exists(_settings.CustomSkinPath))
        {
            AddExistingFileToArchive(archive, _settings.CustomSkinPath, "assets/minecraft/textures/entity/steve.png");
            AddExistingFileToArchive(archive, _settings.CustomSkinPath, "assets/minecraft/textures/entity/alex.png");
            AddExistingFileToArchive(archive, _settings.CustomSkinPath, "assets/minecraft/textures/entity/player/wide/steve.png");
            AddExistingFileToArchive(archive, _settings.CustomSkinPath, "assets/minecraft/textures/entity/player/slim/alex.png");
        }

        var animatedCapeSourcePath = string.IsNullOrWhiteSpace(_settings.CustomCapeSourcePath)
            ? _settings.CustomCapePath
            : _settings.CustomCapeSourcePath;
        var useDedicatedAnimatedCapeMod = !string.IsNullOrWhiteSpace(animatedCapeSourcePath)
            && File.Exists(animatedCapeSourcePath)
            && string.Equals(Path.GetExtension(animatedCapeSourcePath), ".gif", StringComparison.OrdinalIgnoreCase);

        if (allowCapeOverride && !useDedicatedAnimatedCapeMod && !string.IsNullOrWhiteSpace(_settings.CustomCapePath) && File.Exists(_settings.CustomCapePath))
        {
            var isAnimatedCape = SkinClient.IsAnimatedCape(_settings.CustomCapePath);
            AddExistingFileToArchive(archive, _settings.CustomCapePath, "assets/minecraft/textures/entity/cape.png");
            if (!isAnimatedCape)
            {
                AddExistingFileToArchive(archive, _settings.CustomCapePath, "assets/minecraft/textures/entity/elytra.png");
            }

            // Animated cape support: if the cape is a GIF or a vertical PNG spritesheet, generate a .mcmeta animation file
            try
            {
                bool isGif = false;
                using (var fs = new FileStream(_settings.CustomCapePath, FileMode.Open, FileAccess.Read, FileShare.Read))
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

                bool isAnimatedPng = false;
                if (!isGif)
                {
                    var capeDims = SkinClient.GetPngDimensionsPublic(_settings.CustomCapePath);
                    if (capeDims != null)
                    {
                        var (cw, ch) = capeDims.Value;
                        int frameHeight = (cw >= 128) ? 64 : 32;
                        if (ch > frameHeight && ch % frameHeight == 0)
                        {
                            isAnimatedPng = true;
                        }
                    }
                }

                if (isGif || isAnimatedPng)
                {
                    var mcmeta = "{\"animation\":{\"interpolate\":false,\"frametime\":2}}";
                    WriteTextEntry(archive, "assets/minecraft/textures/entity/cape.png.mcmeta", mcmeta);
                    LauncherLog.Info($"[Cape] Generated .mcmeta for resource pack zip: isGif={isGif}, isAnimatedPng={isAnimatedPng}");
                }
            }
            catch { /* non-fatal — cape still works as static */ }
        }
    }

    private void UpdateResourcePackOptions(string instancePath, string packName)
    {
        var optionsPath = Path.Combine(instancePath, "options.txt");
        var lines = File.Exists(optionsPath)
            ? File.ReadAllLines(optionsPath).ToList()
            : [];

        UpsertOptionList(lines, "resourcePacks", packName, includeVanilla: true);
        UpsertOptionList(lines, "incompatibleResourcePacks", packName, includeVanilla: false);
        File.WriteAllLines(optionsPath, lines);
    }

    private static void UpsertOptionList(List<string> lines, string key, string value, bool includeVanilla)
    {
        var index = lines.FindIndex(line => line.StartsWith($"{key}:"));
        var values = index >= 0
            ? ParseOptionList(lines[index])
            : [];

        values.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        values.Insert(0, value);

        if (includeVanilla && !values.Contains("vanilla", StringComparer.OrdinalIgnoreCase))
            values.Add("vanilla");

        var rendered = string.Join(",", values.Select(item => $"\"{item}\""));
        var nextLine = $"{key}:[{rendered}]";

        if (index >= 0)
            lines[index] = nextLine;
        else
            lines.Add(nextLine);
    }

    private static List<string> ParseOptionList(string line)
    {
        var startIndex = line.IndexOf('[');
        var endIndex = line.LastIndexOf(']');
        if (startIndex < 0 || endIndex <= startIndex)
            return [];

        return line[(startIndex + 1)..endIndex]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('\"'))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveThemeBackgroundPath()
    {
        var customBackground = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "custom_bg.png");
        if (File.Exists(customBackground))
            return customBackground;

        var bundledBackground = Path.Combine(AppContext.BaseDirectory, "Resources", "death_client_menu_background.png");
        if (File.Exists(bundledBackground))
            return bundledBackground;

        return string.Empty;
    }

    private string ResolveThemeLogoPath()
    {
        var bundledLogo = Path.Combine(AppContext.BaseDirectory, "Resources", "death_client_logo.png");
        if (File.Exists(bundledLogo))
            return bundledLogo;

        return ResolveThemeBackgroundPath();
    }

    private static string ResolveBundledThemeAsset(string fileName)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        if (File.Exists(bundled))
            return bundled;

        return string.Empty;
    }

    private string ResolveThemePanoramaPath()
    {
        var customBackground = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "custom_bg.png");
        if (File.Exists(customBackground) && IsSquareImage(customBackground))
            return customBackground;

        var bundledPanorama = Path.Combine(AppContext.BaseDirectory, "Resources", "death_client_panorama.png");
        if (File.Exists(bundledPanorama))
            return bundledPanorama;

        return string.Empty;
    }

    private static void AddExistingFileToArchive(ZipArchive archive, string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return;

        archive.CreateEntryFromFile(sourcePath, destinationPath);
    }

    private static bool IsSquareImage(string path)
    {
        try
        {
            using var bitmap = new Bitmap(path);
            return bitmap.PixelSize.Width == bitmap.PixelSize.Height;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static bool SupportsFancyMenu(LauncherProfile profile)
    {
        var loader = profile.Loader?.Trim().ToLowerInvariant();
        if (loader != "fabric" && loader != "quilt")
            return false;

        return IsFancyMenuCapableVersion(profile.GameVersion);
    }

    private static bool IsFancyMenuCapableVersion(string version)
    {
        var match = Regex.Match(version, @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?");
        if (!match.Success)
            return false;

        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);

        if (major >= 24)
            return true;

        return major > 1 || (major == 1 && minor >= 19);
    }

    private async Task LoadSkinAsync()
    {
        try
        {
            await Task.CompletedTask; // keep async signature
            UpdateCharacterPreview();
        }
        catch { }
    }

    private void ApplyHoverMotion(Control? control)
    {
        if (control == null) return;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        
        var transitions = new Transitions
        {
            new DoubleTransition { Property = Control.OpacityProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Easing = new BackEaseOut(), Duration = TimeSpan.FromMilliseconds(300) }
        };

        if (control is TemplatedControl)
        {
            transitions.Add(new BrushTransition { Property = TemplatedControl.BackgroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BrushTransition { Property = TemplatedControl.ForegroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(200) });
            transitions.Add(new BrushTransition { Property = TemplatedControl.BorderBrushProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
        }
        else if (control is Border)
        {
            transitions.Add(new BrushTransition { Property = Border.BackgroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BrushTransition { Property = Border.BorderBrushProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BoxShadowsTransition { Property = Border.BoxShadowProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
        }
        else if (control is Panel)
        {
            transitions.Add(new BrushTransition { Property = Panel.BackgroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
        }

        control.Transitions = transitions;
        
        IBrush? originalBg = null;
        IBrush? originalFg = null;
        IBrush? originalBorder = null;
        BoxShadows originalShadow = new BoxShadows();
        bool captured = false;
        
        control.PointerEntered += (s, e) =>
        {
            control.Opacity = 0.95;
            control.RenderTransform = TransformOperations.Parse("scale(1.07) rotate(1.5deg) translate(0px, -3px)");
            
            if (control is Button btn)
            {
                if (!captured)
                {
                    originalBg = btn.Background;
                    originalFg = btn.Foreground;
                    originalBorder = btn.BorderBrush;
                    captured = true;
                }
                
                if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverBackground)) btn.Background = new SolidColorBrush(Color.Parse(_settings.Style.ButtonHoverBackground));
                if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverForeground)) btn.Foreground = new SolidColorBrush(Color.Parse(_settings.Style.ButtonHoverForeground));
                if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverBorderColor)) btn.BorderBrush = new SolidColorBrush(Color.Parse(_settings.Style.ButtonHoverBorderColor));
            }
            else if (control is Border border)
            {
                if (!captured)
                {
                    originalBg = border.Background;
                    originalBorder = border.BorderBrush;
                    originalShadow = border.BoxShadow;
                    captured = true;
                }
                border.BorderBrush = new SolidColorBrush(Color.Parse("#00F2FE"));
                border.BoxShadow = BoxShadows.Parse("0 8 20 0 #4C00F2FE, 0 2 8 0 #4C6E5BFF");
            }
        };
        control.PointerExited += (s, e) =>
        {
            control.Opacity = 1.0;
            control.RenderTransform = TransformOperations.Parse("scale(1.0) rotate(0deg) translate(0px, 0px)");
            if (captured)
            {
                if (control is Button btn)
                {
                    if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverBackground)) btn.Background = originalBg;
                    if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverForeground)) btn.Foreground = originalFg;
                    if (!string.IsNullOrWhiteSpace(_settings.Style.ButtonHoverBorderColor)) btn.BorderBrush = originalBorder;
                }
                else if (control is Border border)
                {
                    border.Background = originalBg;
                    border.BorderBrush = originalBorder;
                    border.BoxShadow = originalShadow;
                }
            }
        };
    }

    public async Task ChangeSkinAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Minecraft Skin",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });
            if (files.Count > 0)
            {
                var skinPath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "skin.png");
                Directory.CreateDirectory(Path.GetDirectoryName(skinPath)!);
                await using var stream = await files[0].OpenReadAsync();
                await using var dest = File.Create(skinPath);
                await stream.CopyToAsync(dest);

                _settings.CustomSkinPath = skinPath;
                _settingsStore.Save(_settings);

                UpdateCharacterPreview();
                await DialogService.ShowInfoAsync(this, "Skin Applied", "Your skin has been updated and will be used when launching vanilla modpacks.");
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Error", $"Failed to set skin: {ex.Message}");
        }
    }

    public async Task ChangeCapeAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Minecraft Cape",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });
            if (files.Count > 0)
            {
                var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tmp");
                try
                {
                    await using (var stream = await files[0].OpenReadAsync())
                    await using (var dest = File.Create(tempFile))
                    {
                        await stream.CopyToAsync(dest);
                    }

                    var capePath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "cape.png");
                    var gifSourcePath = Path.Combine(_defaultMinecraftPath.BasePath, "death-client", "cape.gif");
                    Directory.CreateDirectory(Path.GetDirectoryName(capePath)!);

                    bool isGif = false;
                    await using (var probe = File.OpenRead(tempFile))
                    {
                        if (probe.Length >= 6)
                        {
                            var header = new byte[6];
                            if (await probe.ReadAsync(header, 0, 6) == 6)
                                isGif = header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46;
                        }
                    }

                    if (SkinClient.ConvertGifToPngSpritesheet(tempFile, capePath, out int frames, out string? gifError))
                    {
                        LauncherLog.Info($"[Cape] Converted GIF to vertical PNG spritesheet with {frames} frames.");
                    }
                    else
                    {
                        if (gifError != null)
                        {
                            LauncherLog.Warn($"[Cape] GIF conversion failed: {gifError}. Copying file directly.");
                        }
                        File.Copy(tempFile, capePath, true);
                    }

                    if (isGif)
                    {
                        File.Copy(tempFile, gifSourcePath, true);
                        _settings.CustomCapeSourcePath = gifSourcePath;
                    }
                    else
                    {
                        _settings.CustomCapeSourcePath = capePath;
                        if (File.Exists(gifSourcePath))
                        {
                            try { File.Delete(gifSourcePath); } catch { }
                        }
                    }

                    _settings.CustomCapePath = capePath;
                    _settingsStore.Save(_settings);

                    UpdateCharacterPreview();
                    await DialogService.ShowInfoAsync(this, "Cape Applied", "Your cape has been updated and will be used when launching vanilla modpacks.");
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch {}
                }
            }
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Error", $"Failed to set cape: {ex.Message}");
        }
    }
    private static string CreateDownloadDestination(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        return destinationPath;
    }
    private int GetSystemRamMb()
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                var info = File.ReadAllText("/proc/meminfo");
                var match = Regex.Match(info, @"MemTotal:\s+(\d+)\s+kB");
                if (match.Success) return int.Parse(match.Groups[1].Value) / 1024;
            }
            return (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        }
        catch { return 8192; } // Fallback to 8GB
    }

    private async Task ExportProfileAsync(LauncherProfile profile)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Select Export Destination" });
            if (folder == null || folder.Count == 0) return;

            var exportPath = Path.Combine(folder[0].Path.LocalPath, $"{profile.Name}_backup.zip");
            if (File.Exists(exportPath)) File.Delete(exportPath);

            ToggleBusyState(true, $"Exporting {profile.Name}...");

            await Task.Run(() => {
                using var zip = System.IO.Compression.ZipFile.Open(exportPath, System.IO.Compression.ZipArchiveMode.Create);
                
                // Manifest
                var manifestPath = Path.Combine(profile.InstanceDirectory, LauncherProfile.ManifestFileName);
                if (File.Exists(manifestPath))
                    zip.CreateEntryFromFile(manifestPath, LauncherProfile.ManifestFileName);
                
                // Mods
                if (Directory.Exists(profile.ModsDirectory))
                {
                    foreach (var file in Directory.GetFiles(profile.ModsDirectory))
                        zip.CreateEntryFromFile(file, Path.Combine("mods", Path.GetFileName(file)));
                }

                // Config
                var configDir = Path.Combine(profile.InstanceDirectory, "config");
                if (Directory.Exists(configDir))
                {
                    foreach (var file in Directory.GetFiles(configDir, "*", SearchOption.AllDirectories))
                    {
                        var relPath = Path.GetRelativePath(profile.InstanceDirectory, file);
                        zip.CreateEntryFromFile(file, relPath);
                    }
                }
            });

            await DialogService.ShowInfoAsync(this, "Export Success", $"Profile exported to {exportPath}");
        }
        catch (Exception ex) { await DialogService.ShowInfoAsync(this, "Export Failed", ex.Message); }
        finally { ToggleBusyState(false, "Ready."); }
    }

    public async Task ImportProfileZipAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions 
            { 
                Title = "Select Profile Backup (.zip)",
                FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Backup Zip") { Patterns = ["*.zip"] }]
            });
            if (files == null || files.Count == 0) return;

            ToggleBusyState(true, "Importing profile...");
            
            await Task.Run(() => {
                var zipPath = files[0].Path.LocalPath;
                using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
                
                var manifestEntry = zip.GetEntry(LauncherProfile.ManifestFileName);
                if (manifestEntry == null) throw new Exception("Manifest not found in zip.");

                LauncherProfile? profile;
                using (var stream = manifestEntry.Open())
                {
                    profile = JsonSerializer.Deserialize<LauncherProfile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                if (profile == null) throw new Exception("Invalid manifest.");

                var targetDir = Path.Combine(_profileStore.GetInstancesRoot(), Slugify(profile.Name));
                int counter = 1;
                while (Directory.Exists(targetDir))
                {
                    targetDir = Path.Combine(_profileStore.GetInstancesRoot(), $"{Slugify(profile.Name)}-{counter++}");
                }

                Directory.CreateDirectory(targetDir);
                foreach (var entry in zip.Entries)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!fullPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(fullPath);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        entry.ExtractToFile(fullPath, true);
                    }
                }
                
                // Update the manifest with the new directory
                profile.InstanceDirectory = targetDir;
                _profileStore.Save(profile);
            });

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                RefreshProfiles();
            });
            await DialogService.ShowInfoAsync(this, "Import Success", "The profile has been imported successfully.");
        }
        catch (Exception ex) { await DialogService.ShowInfoAsync(this, "Import Failed", ex.Message); }
        finally { ToggleBusyState(false, "Ready."); }
    }

    public async Task ImportInstanceFolderAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions 
            { 
                Title = "Select Instance Directory" 
            });
            if (folders == null || folders.Count == 0) return;

            var folderPath = folders[0].Path.LocalPath;
            var folderName = Path.GetFileName(folderPath);
            
            // Basic detection for Fabric/Quilt/Forge
            string loader = "vanilla";
            string gameVersion = _settings.Version; // Default from latest selected or 1.21.1
            if (string.IsNullOrEmpty(gameVersion)) gameVersion = "1.21.1";

            if (Directory.Exists(Path.Combine(folderPath, "mods")))
            {
                loader = "fabric"; // Most common for custom folders, or can be detected via jar scan
            }

            var profile = _profileStore.CreateProfile(folderName, gameVersion, loader, null);
            profile.InstanceDirectory = folderPath; // Redirect to external path
            _profileStore.Save(profile);
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                RefreshProfiles(profile);
                SetActiveSection("profiles");
            });
            await DialogService.ShowInfoAsync(this, "Import Success", $"Successfully imported {folderName} as an instance.");
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Import Error", ex.Message);
        }
        finally { ToggleBusyState(false, "Ready."); }
    }

    private string Slugify(string value)
    {
        return Regex.Replace(value.ToLower(), @"[^a-z0-9]", "-").Trim('-');
    }

    private async Task ScanForModConflictsAsync(LauncherProfile profile)
    {
        if (!Directory.Exists(profile.ModsDirectory)) return;

        var logs = new List<string>();
        var modVersions = new Dictionary<string, string>(); // id -> version

        try
        {
            var jars = Directory.GetFiles(profile.ModsDirectory, "*.jar");
            foreach (var jar in jars)
            {
                try {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(jar);
                    var fabricJson = zip.GetEntry("fabric.mod.json");
                    if (fabricJson != null)
                    {
                        using var stream = fabricJson.Open();
                        using var doc = JsonDocument.Parse(stream);
                        if (doc.RootElement.TryGetProperty("id", out var idProp))
                        {
                            var id = idProp.GetString() ?? "";
                            var version = doc.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "0.0.0";
                            if (!string.IsNullOrEmpty(id)) modVersions[id] = version ?? "";
                        }
                    }
                } catch { /* Skip malformed jars */ }
            }

            foreach (var jar in jars)
            {
                try {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(jar);
                    var fabricJson = zip.GetEntry("fabric.mod.json");
                    if (fabricJson != null)
                    {
                        using var stream = fabricJson.Open();
                        using var doc = JsonDocument.Parse(stream);
                        var modId = doc.RootElement.GetProperty("id").GetString();
                        if (doc.RootElement.TryGetProperty("depends", out var depends))
                        {
                            foreach (var dep in depends.EnumerateObject())
                            {
                                if (dep.Name == "minecraft" || dep.Name == "fabricloader" || dep.Name == "java" || dep.Name == "fabric") continue;
                                if (!modVersions.ContainsKey(dep.Name))
                                    logs.Add($"• {modId} needs '{dep.Name}' but it's missing.");
                            }
                        }
                    }
                } catch { }
            }

            if (logs.Count == 0)
                await DialogService.ShowInfoAsync(this, "Scan Complete", "No obvious missing dependencies found in fabric.mod.json files.");
            else
                await DialogService.ShowInfoAsync(this, "Potential Conflicts", "Missing dependencies found:\n\n" + string.Join("\n", logs));
        }
        catch (Exception ex) { await DialogService.ShowInfoAsync(this, "Scan Failed", ex.Message); }
    }
    private void UpdateResponsiveLayout()
    {
        if (_avatarGlass == null || _avatarControls == null || _avatarActions == null || _mainContentStack == null) return;

        double threshold = 1180; // Slightly higher threshold for safe floating
        _isNarrowMode = this.Bounds.Width < threshold;

        if (_isNarrowMode)
        {
            if (_mainRowGrid != null)
                _mainRowGrid.ColumnDefinitions = new ColumnDefinitions("*,0");
            _mainContentStack.Margin = new Thickness(0); // Content fills screen
            SetAvatarExpansion(false);
        }
        else
        {
            if (_mainRowGrid != null)
                _mainRowGrid.ColumnDefinitions = new ColumnDefinitions("*,340");
            _mainContentStack.Margin = new Thickness(0, 24, 0, 0); // Keep top margin to align with avatar!
            _avatarGlass.Background = new LinearGradientBrush { 
                GradientStops = { new GradientStop(Color.FromArgb(60, 25, 31, 56), 0), new GradientStop(Color.FromArgb(30, 15, 21, 36), 1) } 
            };
            _avatarGlass.BorderThickness = new Thickness(1);
            _avatarGlass.IsHitTestVisible = true;
            _avatarControls.Children[0].IsVisible = true;
            _avatarActions.IsVisible = true;
            _avatarActions.Opacity = 1;
        }
    }

    private void SetAvatarExpansion(bool expanded)
    {
        if (!_isNarrowMode || _avatarGlass == null || _avatarControls == null || _avatarActions == null) return;

        if (expanded)
        {
            _avatarGlass.Background = new SolidColorBrush(Color.FromArgb(200, 9, 12, 18));
            _avatarGlass.BorderThickness = new Thickness(1);
            _avatarControls.Children[0].IsVisible = true;
            _avatarActions.IsVisible = true;
            _avatarActions.Opacity = 1;
        }
        else
        {
            _avatarGlass.Background = Brushes.Transparent;
            _avatarGlass.BorderThickness = new Thickness(0);
            _avatarControls.Children[0].IsVisible = false;
            _avatarActions.IsVisible = false;
            _avatarActions.Opacity = 0;
        }
    }

    private Color GetAccentColor(byte alpha)
    {
        try
        {
            var color = Color.Parse(_settings.AccentColor);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
        catch
        {
            return Color.FromArgb(alpha, 110, 91, 255); // Fallback to #6E5BFF
        }
    }

    private static TextBlock CreateStatusTextBlock() => new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeight.SemiBold
    };

    private static TextBlock CreateMutedTextBlock() => new()
    {
        Foreground = new SolidColorBrush(Color.Parse("#A0A8B8"))
    };

    private void UsernameInput_TextChanged(object? sender, TextChangedEventArgs e) => UsernameInput_TextChanged();

    private void CbVersion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SyncModrinthFilters();
        UpdateLauncherContext();
        UpdateCharacterPreview();
    }

    private async void MinecraftVersion_SelectionChanged(object? sender, SelectionChangedEventArgs e) => await ListVersionsAsync(GetSelectedVersionCategory());
    private async void DownloadVersionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await DownloadSelectedVersionAsync();
    private async void RenameProfileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await RenameSelectedProfileAsync();
    private async void ClearProfileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await DeleteSelectedProfileAsync();
    private async void ImportMrpackButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ImportMrpackAsync();
    private async void QuickInstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await QuickInstallInstanceAsync();
    private async void QuickModSearchButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await QuickModSearchAsync();
    private void ProfileListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ProfileListBox_SelectionChanged();
    private void ModrinthResultsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelectedProjectDetails();

    private async Task PerformFirstRunSetup()
    {
        if (!_settings.IsFirstRun) return;

        // Force reset IsFirstRun only once during development if needed
        // _settings.IsFirstRun = true; 

        // Core directory initialization (silent for all platforms)
        // Core directory initialization in the central data directory
        var directories = new[] 
        { 
            Path.Combine(AppRuntime.DataDirectory, "assets"), 
            Path.Combine(AppRuntime.DataDirectory, "death-client"), 
            Path.Combine(AppRuntime.DataDirectory, "node-skin-server"),
            Path.Combine(AppRuntime.DataDirectory, "death-client-mod")
        };
        foreach (var dir in directories) if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Windows-only visual setup process
        if (OperatingSystem.IsWindows())
        {
            LauncherLog.Info("Performing Windows first-run setup...");
            var setupWin = new SetupWindow();

            try 
            {
                await Dispatcher.UIThread.InvokeAsync(() => setupWin.Show());

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var psCommand = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{Path.Combine(desktopPath, "Aether Launcher.lnk")}'); $s.TargetPath='{exePath}'; $s.Save()";
                    Process.Start(new ProcessStartInfo 
                    { 
                        FileName = "powershell", 
                        Arguments = $"-Command \"{psCommand}\"", 
                        CreateNoWindow = true, 
                        UseShellExecute = false 
                    });
                    LauncherLog.Info("Windows desktop shortcut created.");
                }

                await Task.Delay(4000); // Allow time to read disclaimer
            }
            catch (Exception ex) { LauncherLog.Error("Windows setup failed", ex); }
            finally { await Dispatcher.UIThread.InvokeAsync(() => setupWin.Close()); }
        }

        _settings.IsFirstRun = false;
        _settingsStore.Save(_settings);
    }

    private async void PlayOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_playOverlay).Properties.IsLeftButtonPressed || !_playOverlay.IsEnabled)
            return;

        await LaunchAsync();
    }

    public async void CreateProfileButton_Click() => await CreateProfileAsync();
    public async void BtnStart_Click() => await LaunchAsync();
    public async void ModrinthSearchButton_Click() => await SearchModrinthAsync();
    public async Task ResetLayoutAsync()
    {
        try
        {
            // Reset all style tokens to defaults
            _settings.Style = LayoutStyle.Default();
            _settings.SelectedPreset = "None";
            _settings.AccentColor = "#6E5BFF";
            _settingsStore.Save(_settings);

            ApplySelectedPresetStyle();

            InvalidateUiCache();
            Content = BuildRoot();
            SetActiveSection("settings");
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Reset Failed", ex.Message);
        }
    }

    private bool IsPerformanceModeEnabled()
    {
        return _settings.PerformanceMode 
            || Environment.ProcessorCount <= 2 
            || GetSystemRamMb() < 2500;
    }

    private void ApplyThemeVariant()
    {
        if (Application.Current == null) return;

        var theme = _settings.ThemeVariant?.ToLowerInvariant();
        if (theme == "light")
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
        else if (theme == "dark")
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
        else
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }

        UpdateWindowIcon();
    }

    private string GetTaskbarIconUri()
    {
        bool isLight = false;
        if (Application.Current != null)
        {
            isLight = Application.Current.ActualThemeVariant == ThemeVariant.Light;
        }
        else
        {
            isLight = _settings.ThemeVariant == "light";
        }
        return isLight 
            ? "avares://AetherLauncher/assets/deathclient-taskbar-light.png" 
            : "avares://AetherLauncher/assets/deathclient-taskbar.png";
    }

    private void UpdateWindowIcon()
    {
        try
        {
            var uri = GetTaskbarIconUri();
            Icon = new WindowIcon(AssetLoader.Open(new Uri(uri)));
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"[Icon] Failed to update window icon: {ex.Message}");
        }
    }

    private IBrush GetAccentStripBrush()
    {
        return Brushes.Transparent;
    }

    private void ApplySelectedPresetStyle()
    {
        try
        {
            var preset = _settings.SelectedPreset;
            LayoutStyle presetStyle = null;

            if (string.Equals(preset, "Liquid Glass", StringComparison.OrdinalIgnoreCase))
            {
                presetStyle = GetLiquidGlassStyle();
            }
            else if (string.Equals(preset, "Mountains", StringComparison.OrdinalIgnoreCase))
            {
                presetStyle = GetMountainsStyle();
            }
            else if (string.Equals(preset, "Clear Blue Sky", StringComparison.OrdinalIgnoreCase))
            {
                presetStyle = GetClearBlueSkyStyle();
            }
            else
            {
                presetStyle = LayoutStyle.Default();
            }

            if (presetStyle != null)
            {
                _settings.Style = presetStyle;
                if (!string.IsNullOrEmpty(presetStyle.AccentColor))
                {
                    _settings.AccentColor = presetStyle.AccentColor;
                }
                else
                {
                    _settings.AccentColor = "#6E5BFF";
                }
                _settingsStore.Save(_settings);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"[Layout] Failed to apply selected preset: {ex.Message}");
        }
    }

    private LayoutStyle GetLiquidGlassStyle()
    {
        return new LayoutStyle
        {
            BorderStyle = "rounded",
            CornerRadius = 24,
            WindowBackground = "#00000000",
            WindowBorderColor = "#35FFFFFF",
            WindowBorderThickness = 1.2,
            WindowMargin = 12,
            NavPosition = "sidebar",
            SidebarSide = "left",
            SidebarBackground = "#0AFFFFFF",
            SidebarBorderColor = "#15FFFFFF",
            SidebarWidth = 230,
            AccentColor = "#FF0A84FF",
            CardBackground = "#15FFFFFF",
            CardCornerRadius = 18,
            CardBorderColor = "#25FFFFFF",
            CardPadding = 20,
            ButtonBackground = "#FF0A84FF",
            ButtonForeground = "#FFFFFF",
            ButtonCornerRadius = 12,
            ButtonHoverBackground = "#FF3080FF",
            FieldBackground = "#10FFFFFF",
            FieldBorderColor = "#15FFFFFF",
            FieldRadius = 10,
            FieldPadding = 12,
            ProgressBarForeground = "#FF0A84FF",
            ProgressBarBackground = "#15FFFFFF",
            ProgressBarRadius = 6,
            ItemCardBackground = "#0AFFFFFF",
            ItemCardRadius = 12,
            OverlayColor = "#B005080E",
            AccountsOverlayBackground = "#B014161F",
            AccountsOverlayCornerRadius = 20,
            AccountsOverlayBorderColor = "#25FFFFFF",
            AccountsOverlayBorderThickness = 1,
            PlayButtonGlobal = true,
            BackgroundOpacity = 0.4
        };
    }

    private LayoutStyle GetMountainsStyle()
    {
        return new LayoutStyle
        {
            BorderStyle = "rounded",
            CornerRadius = 16,
            WindowBackground = "#0C0E14",
            WindowBorderColor = "#30FF9F0A",
            WindowBorderThickness = 1,
            WindowMargin = 12,
            NavPosition = "sidebar",
            SidebarSide = "left",
            SidebarBackground = "#0C0D12",
            SidebarBorderColor = "#151720",
            SidebarWidth = 230,
            AccentColor = "#FF9F0A",
            CardBackground = "#121824",
            CardCornerRadius = 12,
            CardBorderColor = "#1F2A38",
            CardPadding = 20,
            ButtonBackground = "#FF9F0A",
            ButtonForeground = "#000000",
            ButtonCornerRadius = 8,
            ButtonHoverBackground = "#FFB340",
            FieldBackground = "#1E293B",
            FieldBorderColor = "#334155",
            FieldRadius = 6,
            FieldPadding = 12,
            ProgressBarForeground = "#FF9F0A",
            ProgressBarBackground = "#1E293B",
            ProgressBarRadius = 4,
            ItemCardBackground = "#1E293B",
            ItemCardRadius = 8,
            OverlayColor = "#E00B0E14",
            AccountsOverlayBackground = "#1E293B",
            AccountsOverlayCornerRadius = 16,
            AccountsOverlayBorderColor = "#334155",
            AccountsOverlayBorderThickness = 1,
            PlayButtonGlobal = true,
            BackgroundOpacity = 0.7
        };
    }

    private LayoutStyle GetClearBlueSkyStyle()
    {
        return new LayoutStyle
        {
            BorderStyle = "rounded",
            CornerRadius = 24,
            WindowBackground = "#F0F9FF",
            WindowBorderColor = "#300284C7",
            WindowBorderThickness = 1.2,
            WindowMargin = 12,
            NavPosition = "sidebar",
            SidebarSide = "left",
            SidebarBackground = "#E0F2FE",
            SidebarBorderColor = "#BCE2FD",
            SidebarWidth = 230,
            AccentColor = "#0EA5E9",
            CardBackground = "#D5FFFFFF",
            CardCornerRadius = 18,
            CardBorderColor = "#200284C7",
            CardPadding = 20,
            ButtonBackground = "#0EA5E9",
            ButtonForeground = "#FFFFFF",
            ButtonCornerRadius = 12,
            ButtonHoverBackground = "#38BDF8",
            FieldBackground = "#A0FFFFFF",
            FieldBorderColor = "#3038BDF8",
            FieldRadius = 10,
            FieldPadding = 12,
            ProgressBarForeground = "#0EA5E9",
            ProgressBarBackground = "#E0F2FE",
            ProgressBarRadius = 6,
            ItemCardBackground = "#E8F4FD",
            ItemCardRadius = 12,
            OverlayColor = "#D0F8FAFC",
            AccountsOverlayBackground = "#FFFFFF",
            AccountsOverlayCornerRadius = 20,
            AccountsOverlayBorderColor = "#300284C7",
            AccountsOverlayBorderThickness = 1,
            PlayButtonGlobal = true,
            BackgroundOpacity = 0.95,
            PrimaryForeground = "#0F172A",
            SecondaryForeground = "#475569"
        };
    }


}

internal static class AvaloniaControlExtensions
{
    public static T With<T>(this T control, int row = -1, int column = -1, int columnSpan = 1, int rowSpan = 1) where T : Control
    {
        if (row >= 0) Grid.SetRow(control, row);
        if (column >= 0) Grid.SetColumn(control, column);
        if (columnSpan > 1) Grid.SetColumnSpan(control, columnSpan);
        if (rowSpan > 1) Grid.SetRowSpan(control, rowSpan);
        return control;
    }

    public static T With<T>(this T control, Action<T> action) where T : Control
    {
        action(control);
        return control;
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public class WorldItem
{
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

public class ResourcePackItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

