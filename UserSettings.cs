using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineMinecraftLauncher;

internal sealed class UserSettingsStore
{
    public static UserSettingsStore? Instance { get; private set; }
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public UserSettingsStore(string launcherBasePath)
    {
        Instance = this;
        var settingsDirectory = Path.Combine(launcherBasePath, "death-client");
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public UserSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new UserSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to load settings from '{_settingsPath}'.", ex);
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        LauncherLog.AtomicWriteAllText(_settingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
    }
}

/// <summary>
/// All style tokens for the launcher layout. Set from an AXAML layout file
/// or the quick toggles in the Layout tab. Only non-null/non-default values
/// from the AXAML file override these — everything else stays as-is.
/// </summary>
internal sealed class LayoutStyle
{
    // ─── Window / Shell ─────────────────────────────────────────────────
    public string BorderStyle { get; set; } = "rounded";
    public int CornerRadius { get; set; } = 28;
    public string? WindowBackground { get; set; }
    public string? WindowBorderColor { get; set; }
    public double WindowBorderThickness { get; set; } = 1;
    public double WindowMargin { get; set; } = 12;

    // ─── Sidebar ────────────────────────────────────────────────────────
    public string NavPosition { get; set; } = "sidebar";
    public string SidebarSide { get; set; } = "left";
    public bool SidebarCollapsed { get; set; } = false;
    public string? SidebarBackground { get; set; }
    public string? SidebarBorderColor { get; set; }
    public double SidebarWidth { get; set; } = 240;
    public double SidebarPadding { get; set; } = 18;

    // ─── Navigation Buttons ─────────────────────────────────────────────
    public string? NavButtonBackground { get; set; }
    public string? NavButtonActiveBackground { get; set; }
    public string? NavButtonForeground { get; set; }
    public string? NavButtonActiveForeground { get; set; }
    public double NavButtonCornerRadius { get; set; } = 14;
    public double NavButtonSpacing { get; set; } = 12;
    public double NavButtonHeight { get; set; } = double.NaN;
    public double NavButtonFontSize { get; set; } = double.NaN;
    public string? NavIndicatorStyle { get; set; }  // "fill" | "left-pill" | "underline" | "glow"

    // ─── Typography / Branding ──────────────────────────────────────────
    public string? TitleText { get; set; }
    public double TitleFontSize { get; set; } = 18;
    public string? TitleForeground { get; set; }
    public string? PrimaryFontFamily { get; set; }
    public string? PrimaryForeground { get; set; }
    public string? SecondaryForeground { get; set; }

    // ─── Colors / Accent ────────────────────────────────────────────────
    public string? AccentColorOverride { get; set; }
    public string? AccentColor { get; set; }  // shorthand alias used by some methods
    public double BackgroundOpacity { get; set; } = 0.65;
    public string? BackgroundOverlayColor { get; set; }
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOverlayOpacity { get; set; } = double.NaN;
    public double AccentStripHeight { get; set; } = double.NaN;
    public bool PlayButtonGlobal { get; set; } = false;

    // ─── Cards / Panels ─────────────────────────────────────────────────
    public string? CardBackground { get; set; }
    public double CardCornerRadius { get; set; } = 16;
    public string? CardBorderColor { get; set; }
    public double CardPadding { get; set; } = 18;

    // ─── Buttons ────────────────────────────────────────────────────────
    public string? ButtonBackground { get; set; }
    public string? ButtonForeground { get; set; }
    public double ButtonCornerRadius { get; set; } = 14;
    public double ButtonHeight { get; set; } = double.NaN;
    public double ButtonFontSize { get; set; } = double.NaN;
    public double ButtonPadding { get; set; } = double.NaN;
    public string? ButtonHoverBackground { get; set; }
    public string? ButtonHoverForeground { get; set; }
    public string? ButtonHoverBorderColor { get; set; }

    // ─── Content Area ───────────────────────────────────────────────────
    public double ContentPadding { get; set; } = double.NaN;
    public double ContentSpacing { get; set; } = double.NaN;
    public string? ContentBackground { get; set; }

    // ─── Density ────────────────────────────────────────────────────────
    public bool CompactMode { get; set; } = false;

    // ─── Fields ─────────────────────────────────────────────────────────
    public string? FieldBackground { get; set; }
    public string? FieldForeground { get; set; }
    public string? FieldBorderColor { get; set; }
    public double FieldRadius { get; set; } = double.NaN;
    public double FieldPadding { get; set; } = double.NaN;
    public double FieldFontSize { get; set; } = double.NaN;

    // ─── Progress Bars ──────────────────────────────────────────────────
    public string? ProgressBarForeground { get; set; }
    public string? ProgressBarBackground { get; set; }
    public double ProgressBarHeight { get; set; } = double.NaN;
    public double ProgressBarRadius { get; set; } = double.NaN;

    // ─── Item Cards ─────────────────────────────────────────────────────
    public string? ItemCardBackground { get; set; }
    public double ItemCardRadius { get; set; } = double.NaN;

    // ─── Overlays ───────────────────────────────────────────────────────
    public string? OverlayColor { get; set; }
    public string? AccountsOverlayBackground { get; set; }
    public double AccountsOverlayCornerRadius { get; set; } = double.NaN;
    public string? AccountsOverlayBorderColor { get; set; }
    public double AccountsOverlayBorderThickness { get; set; } = double.NaN;

    // ─── Sections ───────────────────────────────────────────────────────
    public string? SectionOrder { get; set; }

    public LayoutStyle Clone() => new()
    {
        BorderStyle = BorderStyle, CornerRadius = CornerRadius,
        WindowBackground = WindowBackground, WindowBorderColor = WindowBorderColor,
        WindowBorderThickness = WindowBorderThickness, WindowMargin = WindowMargin,
        NavPosition = NavPosition, SidebarSide = SidebarSide, SidebarCollapsed = SidebarCollapsed,
        SidebarBackground = SidebarBackground, SidebarBorderColor = SidebarBorderColor,
        SidebarWidth = SidebarWidth, SidebarPadding = SidebarPadding,
        NavButtonBackground = NavButtonBackground, NavButtonActiveBackground = NavButtonActiveBackground,
        NavButtonForeground = NavButtonForeground, NavButtonActiveForeground = NavButtonActiveForeground,
        NavButtonCornerRadius = NavButtonCornerRadius, NavButtonSpacing = NavButtonSpacing,
        NavButtonHeight = NavButtonHeight, NavButtonFontSize = NavButtonFontSize,
        TitleText = TitleText, TitleFontSize = TitleFontSize, TitleForeground = TitleForeground,
        PrimaryFontFamily = PrimaryFontFamily, PrimaryForeground = PrimaryForeground,
        SecondaryForeground = SecondaryForeground,
        AccentColorOverride = AccentColorOverride, BackgroundOpacity = BackgroundOpacity,
        BackgroundOverlayColor = BackgroundOverlayColor,
        BackgroundImagePath = BackgroundImagePath,
        BackgroundOverlayOpacity = BackgroundOverlayOpacity,
        AccentStripHeight = AccentStripHeight,
        PlayButtonGlobal = PlayButtonGlobal,
        CardBackground = CardBackground, CardCornerRadius = CardCornerRadius,
        CardBorderColor = CardBorderColor, CardPadding = CardPadding,
        ButtonBackground = ButtonBackground, ButtonForeground = ButtonForeground,
        ButtonCornerRadius = ButtonCornerRadius, ButtonHeight = ButtonHeight,
        ButtonFontSize = ButtonFontSize, ButtonPadding = ButtonPadding,
        ButtonHoverBackground = ButtonHoverBackground, ButtonHoverForeground = ButtonHoverForeground, ButtonHoverBorderColor = ButtonHoverBorderColor,
        ContentPadding = ContentPadding, ContentSpacing = ContentSpacing,
        ContentBackground = ContentBackground, CompactMode = CompactMode,
        FieldBackground = FieldBackground, FieldForeground = FieldForeground,
        FieldBorderColor = FieldBorderColor, FieldRadius = FieldRadius,
        FieldPadding = FieldPadding, FieldFontSize = FieldFontSize,
        ProgressBarForeground = ProgressBarForeground, ProgressBarBackground = ProgressBarBackground,
        ProgressBarHeight = ProgressBarHeight, ProgressBarRadius = ProgressBarRadius,
        ItemCardBackground = ItemCardBackground, ItemCardRadius = ItemCardRadius,
        OverlayColor = OverlayColor, SectionOrder = SectionOrder,
        AccountsOverlayBackground = AccountsOverlayBackground, AccountsOverlayCornerRadius = AccountsOverlayCornerRadius,
        AccountsOverlayBorderColor = AccountsOverlayBorderColor, AccountsOverlayBorderThickness = AccountsOverlayBorderThickness
    };

    public static LayoutStyle Default() => new();
}

internal sealed class UserSettings
{
    public string Username { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string CustomSkinPath { get; set; } = string.Empty;
    public string CustomCapePath { get; set; } = string.Empty;
    public string CustomCapeSourcePath { get; set; } = string.Empty;
    public bool EnableFancyMenu { get; set; } = false;
    public bool OfflineMode { get; set; } = false;
    public string ClientLayout { get; set; } = string.Empty; // Legacy — kept for migration only
    public string ThemeVariant { get; set; } = "dark";
    public string AccentColor { get; set; } = "#6E5BFF";
    public string? SelectedPreset { get; set; } = "Mountains";
    public LayoutStyle Style { get; set; } = new();
    public List<string> SectionOrder { get; set; } = ["Hero", "Stats", "Actions"];
    public string LastSelectedProfilePath { get; set; } = string.Empty;
    public string BaseMinecraftPath { get; set; } = string.Empty;
    public string SelectedAccountId { get; set; } = string.Empty;
    public string MicrosoftClientId { get; set; } = string.Empty;
    public List<LauncherAccount> Accounts { get; set; } = [];

    // New Launch Settings
    public int MaxRamMb { get; set; } = 2048;
    public string JvmArgs { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -Dsun.stdout.encoding=UTF-8";
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool PerformanceMode { get; set; } = false;
    public bool IsFirstRun { get; set; } = true;
    public string AfterLaunchBehavior { get; set; } = "close"; // "close" | "minimize" | "background"

    /// <summary>
    /// Migrates legacy ClientLayout semicolon-tokens into the new Style object.
    /// Called once on startup; clears ClientLayout after migration.
    /// </summary>
    public void MigrateLegacyLayout()
    {
        if (string.IsNullOrWhiteSpace(ClientLayout)) return;

        var tokens = ClientLayout.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "sidebar:right":
                    Style.SidebarSide = "right";
                    break;
                case "nav:top":
                    Style.NavPosition = "top";
                    break;
                case "sidebar:collapsed":
                    Style.SidebarCollapsed = true;
                    break;
            }
        }

        ClientLayout = string.Empty; // Migration done
    }
}
