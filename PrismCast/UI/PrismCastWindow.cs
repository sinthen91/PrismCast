using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PrismCast.Hosting;
using PrismCast.Playback;
using PrismCast.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrismCast.UI;

internal sealed class PrismCastWindow : Window
{
    private const ImGuiWindowFlags DeviceWindowFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoBackground;

    private enum Page
    {
        RemoteControl,
        PlexLibrary,
        LocalFiles,
        JoinSession,
        Settings,
        Changelog,
    }

    private enum SettingsPage
    {
        General,
        Plex,
        LocalMedia,
        Playback,
        Screen,
        Networking,
        Relay,
        Advanced,
        About,
    }

    private enum InterfaceMode
    {
        Automatic = 0,
        Tablet = 1,
        Phone = 2,
    }

    private enum LibrarySource
    {
        Plex,
        LocalFiles,
        Web,
    }

    private enum SessionMobileTab
    {
        Rooms,
        Temporary,
        Nearby,
    }

    private enum UiIcon
    {
        Remote,
        Library,
        People,
        Monitor,
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight,
        Forward,
        Back,
        RotateLeft,
        RotateRight,
        PlaceFront,
        Reset,
        Gear,
        Movie,
        Television,
        AnimeSpark,
        FilmReel,
        Folder,
        Link,
        Search,
        Play,
        Ticket,
        Crown,
        Copy,
        Plus,
        ChevronRight,
    }

    private sealed record PlexBrowsePage(string Title, List<PlexItem> Items);
    private sealed record LocalMediaEntry(string Path, string Name, string Folder, long SizeBytes);
    private sealed record WebMediaPreview(string Url, string Title, string Provider, double DurationSeconds, string? ArtworkPath);

    private static readonly Vector4 Accent = new(0.58f, 0.34f, 0.95f, 1f);
    private static readonly Vector4 AccentHover = new(0.68f, 0.46f, 1.00f, 1f);
    private static readonly Vector4 S9Cyan = new(0.24f, 0.82f, 1.00f, 1f);
    private static readonly Vector4 S9Blue = new(0.22f, 0.44f, 0.98f, 1f);
    private static readonly Vector4 S9Glow = new(0.48f, 0.20f, 1.00f, 0.30f);
    private static readonly Vector4 S9PanelLine = new(0.34f, 0.52f, 0.90f, 0.55f);
    private static readonly Vector4 SidebarBg = new(0.055f, 0.052f, 0.075f, 0.98f);
    private static readonly Vector4 PanelBg = new(0.075f, 0.070f, 0.100f, 0.96f);
    private static readonly Vector4 CardBg = new(0.095f, 0.088f, 0.125f, 0.98f);
    private static readonly Vector4 Muted = new(0.57f, 0.56f, 0.64f, 1f);
    private static readonly Vector4 Good = new(0.30f, 0.82f, 0.54f, 1f);
    private static readonly Vector4 Warning = new(0.94f, 0.69f, 0.27f, 1f);
    private static readonly Vector4 Danger = new(0.92f, 0.32f, 0.40f, 1f);
    private static readonly Vector4 DeviceShell = new(0.020f, 0.018f, 0.030f, 0.995f);
    private static readonly Vector4 DeviceBorder = new(0.30f, 0.20f, 0.48f, 0.95f);
    private static readonly Vector4 DeviceScreen = new(0.047f, 0.043f, 0.066f, 0.995f);
    private static readonly Vector4 HeaderBg = new(0.035f, 0.031f, 0.050f, 1f);

    private const float DeviceInset = 10f;
    private const float SessionCardInset = 12f;
    private const float PhoneScreenInset = 13f;
    private const float DeviceHeaderHeight = 46f;
    private const float SidebarWidth = 205f;
    private const float TopBarHeight = 52f;
    private const float BottomPlayerHeight = 68f;
    private const float PhoneStatusBarHeight = 44f;
    private const float PhoneAppHeaderHeight = 106f;
    private const float PhoneBottomNavHeight = 72f;
    private static readonly Vector2 MiniIdleSize = new(138f, 186f);
    private static readonly Vector2 MiniActiveSize = new(214f, 508f);

    private sealed record ChangelogEntry(string Version, string Title, string[] Changes);

    private static readonly ChangelogEntry[] RecentChanges =
    [
        new("0.2.0.41", "Complete Plex library browsing",
        [
            "Removed the 600-item display cap that could make large Plex libraries appear to stop partway through the alphabet.",
            "Plex search and normal browsing now operate over the same complete library result set.",
            "Added viewport-based row virtualization so large phone libraries render visible poster rows lazily instead of loading every card at once.",
        ]),
        new("0.2.0.40", "Web / YouTube library source",
        [
            "Added Web / YouTube as a permanent media source in the Library selector alongside Plex and Local Files.",
            "Added a dedicated URL loader with metadata preview, thumbnail, title, source, and runtime when available.",
            "Web media now launches through the same hosting/session pipeline as Plex and Local Files.",
            "Removed the old Quick URL / YouTube control from Remote so media selection stays inside Library.",
        ]),
        new("0.2.0.38", "Session navigation + group feedback hotfix",
        [
            "Fixed the phone navigation bar disappearing while hosting or viewing a Watch Party/Group session.",
            "Added inline Create Group validation and progress/error feedback directly inside the Host container.",
            "Added a bounded timeout and clearer error when the built-in PrismCast directory is unavailable.",
        ]),
        new("0.2.0.37", "Watch Parties + persistent Groups",
        [
            "Replaced Open/Private lobby choices with one-time Watch Parties and persistent Groups.",
            "Watch Parties use a fresh short invite code for every hosted media session and close when playback ends.",
            "Groups use a permanent invite code; members save the group once and can join whenever that group is live.",
            "Rebuilt the phone Session page into three containers: Host, Join Watch Party or Group, and Your Groups.",
            "PrismCast directory lookup is automatic application infrastructure; there is no relay/directory setup in Settings.",
        ]),
        new("0.2.0.36", "Plex playback main-thread fix",
        [
            "Fixed Plex/local/URL hosting reading Dalamud character data from a background continuation after tunnel startup.",
            "Host display names are now captured on the framework thread before asynchronous hosting continues.",
            "Mobile Library now shows playback errors/status directly above the poster grid instead of hiding them below the scroll area.",
        ]),
        new("0.2.0.35", "Direct lobbies + Plex play repair",
        [
            "Removed the PrismCast relay, permanent rooms, Nearby discovery, and short relay session codes from the active UI flow.",
            "Replaced Sessions with simple Open Lobby / Private Lobby direct-invite hosting and joining.",
            "Removed the PrismCast Relay tile from Settings and simplified Networking around direct invite connections.",
            "Reworked phone Plex PLAY buttons to use native ImGui buttons and made UI tasks queue instead of silently dropping clicks.",
            "Plex playback now resolves, hosts, and switches to Remote through one dedicated action with visible error status.",
        ]),
        new("0.2.0.34", "Settings hub + relay page",
        [
            "Replaced the phone Settings dropdown with a 3x3 grid of individual settings tiles.",
            "Added a dedicated PrismCast Relay settings page with relay status, URL configuration, host ID, and room/nearby guidance.",
            "Added direct Open Relay Settings actions to the Rooms and Nearby tabs when the relay is not configured.",
            "Separated trusted-host networking controls from the PrismCast Relay URL so the relay is no longer hidden inside Networking.",
        ]),
        new("0.2.0.33", "Mobile session hub",
        [
            "Rebuilt the phone Session page around Rooms, Temporary sessions, and Nearby discovery.",
            "Added persistent Open and Private rooms; Private rooms use a permanent invite code for first-time membership.",
            "Added one-time temporary session codes that regenerate for every hosted cast.",
            "Added pull-only Nearby discovery for PrismCast hosts currently loaded in the local FFXIV object table; no notifications are generated.",
            "Removed What's New and the global Players section from the Session page; active room viewers are shown only inside the live room card.",
            "Added host-only room member management so permanent room access can be revoked without cluttering the main Session view.",
        ]),
        new("0.2.0.32", "Dynamic mobile library selector",
        [
            "Replaced the fixed 2x2 Plex category grid with a single expandable library selector.",
            "The selector is generated from each user's Plex libraries and always places Local Files at the bottom.",
            "Added a full-width outlined search bar with magnifying-glass icon and descriptive placeholder text.",
            "Added an explicit scroll region for Plex posters and Local Files while keeping the search and source selector fixed.",
            "Preserved the three-column poster grid, wrapped titles, runtime-aware Play buttons, and mobile padding.",
        ]),
        new("0.2.0.31", "Mobile library redesign",
        [
            "Moved mobile library search directly below the PrismCast header.",
            "Rebuilt Plex library categories as a two-by-two icon grid with Movies, TV Shows, Anime, and Anime Movies prioritized.",
            "Changed mobile Plex browsing to a centered three-column poster grid with padded cards and wrapped titles.",
            "Added Plex runtime metadata to playable movie and episode buttons.",
            "Strengthened mobile Play/Open buttons with brighter Solution 9 purple/cyan styling.",
            "Kept Local Files accessible from the compact mobile library search toolbar.",
        ]),
        new("0.2.0.30", "Rounded phone screen corners",
        [
            "Rounded the actual phone-screen child window so its square background can no longer cover the neon shell corners.",
            "Added a slightly larger inner screen inset to keep content safely inside the lit device rim.",
            "Kept the neon border drawn above the screen for a clean uninterrupted outline.",
        ]),
        new("0.2.0.29", "Corner cap shell fix",
        [
            "Added top-layer shell edging so the phone screen no longer pokes through the rounded outer corners.",
            "Re-drew the neon chassis border over the content edges to keep the corner silhouette clean.",
            "Preserved the existing neon shell styling and larger default phone launch size.",
        ]),
        new("0.2.0.28", "Neon shell border",
        [
            "Replaced the embossed phone shell with a brighter neon tube border treatment.",
            "Added layered violet and cyan glow lines around the device frame for a lit-edge look.",
            "Kept the larger phone launch size so remote helper text remains visible at startup.",
        ]),
        new("0.2.0.27", "Remote fit + embossed shell",
        [
            "Raised the default phone height so both Screen Controls help lines remain visible on launch.",
            "Thickened the phone shell border and added layered highlight/shadow treatment for an embossed frame.",
        ]),
        new("0.2.0.26", "Header visibility pass",
        [
            "Made the supplied PrismCast wordmark significantly larger in phone mode.",
            "Replaced the tiny plain minimize/close text buttons with large high-contrast custom window controls.",
            "Adjusted the phone launch/minimum height to preserve the full Remote control layout after enlarging the header chrome.",
        ]),
        new("0.2.0.25", "Remote sizing + persistent transport controls",
        [
            "Increased the phone minimize and close controls for easier targeting.",
            "Slightly enlarged the PrismCast header logo.",
            "Kept back, play/pause, and forward transport controls visible even while idle.",
            "Adjusted the default phone launch size and minimum height to the taller approved ratio so bottom screen actions are not clipped.",
        ]),
        new("0.2.0.24", "Remote polish + session cleanup",
        [
            "Enlarged the PrismCast header logo for better readability.",
            "Added a Stop Session control beside Settings while hosting.",
            "Moved Scale and Flat/Curved controls into the top of Screen Controls.",
            "Cleared stale Plex artwork immediately when a session ends.",
            "Redrew the Settings icon as a proper cog outline.",
        ]),
        new("0.2.0.23", "Remote layout + real media assets",
        [
            "Expanded the phone Remote control surface to fill the available page instead of leaving dead space.",
            "Removed phone-page and Screen Controls scrollbars from the Remote layout.",
            "Replaced the drawn PrismCast header mark with the supplied PrismCast logo artwork.",
            "Replaced text play/pause glyphs with the supplied white play and pause icons.",
            "Removed duplicate Remote/Live text beside the Settings cog; connection state now lives only in the status bar.",
        ]),
        new("0.2.0.22", "Remote fidelity build repair",
        [
            "Fixed malformed generated source in the alpha.21 UI helper block.",
            "Preserved the intended alpha.21 Remote fidelity design and controls.",
            "Replaced one icon primitive with a safer draw-list implementation.",
        ]),
        new("0.2.0.21", "Remote fidelity pass",
        [
            "Split phone chrome into a local-time status bar and PrismCast app header.",
            "Rebuilt Now Playing padding, progress styling, transport alignment, and glow treatment.",
            "Added monitor and control icons plus centered directional controls.",
            "Added icon-driven Remote, Library, and Session bottom navigation.",
        ]),
        new("0.2.0.20", "Remote page overhaul",
        [
            "Rebuilt the phone-mode Remote page to better match the approved Solution 9 mockup.",
            "Removed the Session block and duplicate bottom player from the phone Remote page.",
            "Introduced compact circular transport controls and a cleaner Screen Controls layout.",
            "Kept direct access to Place in Front and Reset Rotation while reducing clutter.",
        ]),
        new("0.2.0.19", "Solution 9 visual redesign",
        [
            "Rebuilt the PrismCast shell around the Solution 9 mobile-app design language.",
            "Added neon tech-panel borders, angular corner traces, cyan/violet HUD accents, and a prism identity system.",
            "Phone Remote, Library, Session, Settings, and Changelog pages now use compact mobile-first layouts.",
            "The Session page now combines invite-code status, privacy-first viewer names, and What's New cards.",
            "Plex and Local Files remain unified under the Library tab.",
        ]),
        new("0.2.0.18", "Startup crash hotfix",
        [
            "Fixed a crash when enabling PrismCast after the responsive UI update.",
            "Removed ImGui window-state access from the plugin constructor path.",
            "Tablet, Phone, Automatic, and the unified Plex/Local Files library remain enabled.",
        ]),
        new("0.2.0.17", "Responsive phone mode + unified library",
        [
            "Added switchable Tablet, Phone, and Automatic interface modes.",
            "Phone mode uses a bottom navigation layout inspired by the Solution 9 mobile mockup.",
            "The Library page now includes both Plex and Local Files as source sections.",
            "Settings and Recent Changes remain accessible from the compact phone shell.",
        ]),
        new("0.2.0.16", "Mini remote layout polish",
        [
            "Widened the portrait mini remote so every control fits cleanly.",
            "Centered the transport buttons, D-pad, and secondary controls in minimized mode.",
            "Prevented the right-edge buttons from clipping off-screen in the mini remote.",
        ]),
        new("0.2.0.15", "Remote control refinement",
        [
            "Screen movement, rotation, tilt, roll, and scale controls support click-and-hold repeat.",
            "New hosted sessions place the shared screen directly in front of the host by default.",
            "Remote Control now includes Place in Front of Me and Reset Rotation actions.",
            "Minimized mode is a vertical pocket remote with playback and screen controls.",
            "Sidebar changelog entries are clickable and open the full Recent Changes page.",
        ]),
        new("0.2.0.14", "Mini remote + viewer list",
        [
            "Added the first minimized remote mode.",
            "Added the host viewer list using first names only.",
            "Simplified the main Remote Control screen.",
            "Added the sidebar changelog summary.",
        ]),
        new("0.2.0.13", "Tablet shell + Plex posters",
        [
            "Replaced the normal Dalamud-looking frame with the PrismCast media-tablet shell.",
            "Added cached portrait Plex artwork to the media library.",
            "Fixed Plex search/refresh toolbar sizing.",
        ]),
        new("0.2.0.10", "Remote camera projection fix",
        [
            "World-space rendering switched to FFXIV's exact view-projection matrix.",
            "Designed to prevent remote viewers from seeing fragmented screen geometry at different camera angles.",
        ]),
        new("0.2.0.9", "Host-authoritative screen sync",
        [
            "Fixed network serialization of world-space X/Y/Z position.",
            "Host position, rotation, scale, and screen shape became authoritative for all viewers.",
        ]),
        new("0.2.0.6", "Plex series + screen controls",
        [
            "Added Plex TV/anime series browsing through shows, seasons, and episodes.",
            "Added live X/Y/Z, rotation, scale, and flat/curved screen controls.",
        ]),
    ];

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".flv", ".m4v", ".wmv", ".ts", ".m2ts"
    };

    private readonly IDalamudPluginInterface _pi;
    private readonly Configuration _config;
    private readonly SessionController _session;
    private readonly DependencyManager _deps;
    private readonly PlexClient _plex;
    private readonly VideoEngine _video;
    private readonly RelayClient _relay;
    private readonly IObjectTable _objects;
    private readonly IFramework _framework;
    private readonly FileDialogManager _dialogs = new();
    private readonly object _plexLock = new();
    private readonly object _localLock = new();
    private readonly ConcurrentDictionary<string, byte> _posterDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _posterCacheDirectory;
    private readonly string _uiAssetDirectory;
    private readonly string _logoAssetPath;
    private readonly string _playIconPath;
    private readonly string _pauseIconPath;
    private static readonly HttpClient PosterHttp = new();
    private static readonly HttpClient PresenceHttp = new();

    [PluginService]
    internal ITextureProvider TextureProvider { get; private set; } = null!;

    private Page _page;
    private SettingsPage _settingsPage;
    private bool _phoneSettingsHome = true;

    private string _url = "";
    private WebMediaPreview? _webPreview;
    private string _webStatus = "";
    private bool _webPreviewLoading;
    private string _invite = "";
    private string _plexSearch = "";
    private string _localSearch = "";
    private string _uiStatus = "";
    private Task? _uiTask;
    private bool _minimized;
    private Vector2 _expandedSize = new(1120, 760);
    private readonly string _viewerPresenceId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, DateTime> _repeatButtonNextFire = new(StringComparer.Ordinal);
    private int _selectedChangelogIndex;
    private PlexItem? _nowPlayingPlexItem;
    private PlexItem? _selectedPlexDetailsItem;
    private LibrarySource _librarySource;

    private SessionMobileTab _sessionMobileTab = SessionMobileTab.Rooms; // legacy state retained for config/source compatibility
    private bool _lobbyPrivate;
    private bool _activeLobbyPrivate;
    private bool _showCreateRoom;
    private bool _showJoinPrivateRoom;
    private bool _sessionRoomRefreshAttempted;
    private string _newRoomName = "";
    private bool _newRoomPrivate;
    private string _roomJoinCode = "";
    private string _activeJoinedRoomId = "";
    private string _manageRoomId = "";
    private string _pendingLeaveGroupId = "";
    private List<PrismRoomInfo> _roomStatuses = [];
    private List<PrismNearbySession> _nearbySessions = []; // legacy Nearby prototype
    private DateTime _nextGroupRefresh = DateTime.MinValue;

    private List<PlexLibrary> _libraries = [];
    private List<PlexItem> _plexItems = [];
    private readonly List<PlexBrowsePage> _plexHistory = [];
    private List<PlexServer> _plexServers = [];
    private int _libraryIndex = -1;
    private int _plexServerIndex = -1;
    private string _plexPageTitle = "";
    private bool _plexInitialLoadAttempted;

    private List<LocalMediaEntry> _localFiles = [];
    private bool _localInitialScanAttempted;

    private Vector3 _screenPositionEdit;
    private float _screenYawDegreesEdit;
    private float _screenPitchDegreesEdit;
    private float _screenRollDegreesEdit;
    private float _screenScaleEdit;
    private bool _screenCurvedEdit;

    public PrismCastWindow(
        IDalamudPluginInterface pi,
        Configuration config,
        SessionController session,
        DependencyManager deps,
        PlexClient plex,
        VideoEngine video,
        RelayClient relay,
        IObjectTable objects,
        IFramework framework)
        : base("PrismCast###PrismCastMain", DeviceWindowFlags)
    {
        _pi = pi;
        _pi.Inject(this);
        _posterCacheDirectory = Path.Combine(_pi.GetPluginConfigDirectory(), "plex-posters");
        Directory.CreateDirectory(_posterCacheDirectory);
        _uiAssetDirectory = Path.Combine(_pi.GetPluginConfigDirectory(), "ui-assets");
        Directory.CreateDirectory(_uiAssetDirectory);
        _logoAssetPath = Path.Combine(_uiAssetDirectory, "prismcast-logo.png");
        _playIconPath = Path.Combine(_uiAssetDirectory, "play-white.png");
        _pauseIconPath = Path.Combine(_uiAssetDirectory, "pause-white.png");
        ExtractEmbeddedUiAsset("prismcast-logo.png", _logoAssetPath);
        ExtractEmbeddedUiAsset("play-white.png", _playIconPath);
        ExtractEmbeddedUiAsset("pause-white.png", _pauseIconPath);
        _config = config;
        _session = session;
        _deps = deps;
        _plex = plex;
        _video = video;
        _relay = relay;
        _objects = objects;
        _framework = framework;

        _page = config.RememberLastPage && Enum.IsDefined(typeof(Page), config.LastPage)
            ? (Page)config.LastPage
            : Page.RemoteControl;
        _librarySource = _page == Page.LocalFiles ? LibrarySource.LocalFiles : LibrarySource.Plex;
        if (_page == Page.LocalFiles)
            _page = Page.PlexLibrary;

        _screenPositionEdit = config.ScreenPosition;
        _screenYawDegreesEdit = RadiansToDegrees(config.ScreenYaw);
        _screenPitchDegreesEdit = RadiansToDegrees(config.ScreenPitch);
        _screenRollDegreesEdit = RadiansToDegrees(config.ScreenRoll);
        _screenScaleEdit = config.ScreenScale;
        _screenCurvedEdit = config.CurvedScreen;

        Size = ConfiguredInterfaceMode() == InterfaceMode.Phone ? new Vector2(440, 1004) : new Vector2(1120, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = GetInitialExpandedConstraints();
    }

    private InterfaceMode ConfiguredInterfaceMode()
        => _config.InterfaceMode switch
        {
            1 => InterfaceMode.Tablet,
            2 => InterfaceMode.Phone,
            _ => InterfaceMode.Automatic,
        };

    private InterfaceMode EffectiveInterfaceMode()
    {
        var configured = ConfiguredInterfaceMode();
        if (configured != InterfaceMode.Automatic)
            return configured;

        var width = _minimized ? _expandedSize.X : Math.Max(_expandedSize.X, ImGui.GetWindowSize().X);
        return width < 760f ? InterfaceMode.Phone : InterfaceMode.Tablet;
    }

    private WindowSizeConstraints GetExpandedConstraints()
        => EffectiveInterfaceMode() == InterfaceMode.Phone
            ? PhoneConstraints()
            : TabletConstraints();

    private WindowSizeConstraints GetInitialExpandedConstraints()
        => ConfiguredInterfaceMode() == InterfaceMode.Phone
            ? PhoneConstraints()
            : TabletConstraints();

    private static WindowSizeConstraints PhoneConstraints()
        => new()
        {
            MinimumSize = new Vector2(430, 982),
            MaximumSize = new Vector2(640, 1100),
        };

    private static WindowSizeConstraints TabletConstraints()
        => new()
        {
            MinimumSize = new Vector2(960, 650),
            MaximumSize = new Vector2(1700, 1050),
        };

    private Vector2 MinimumExpandedSize()
        => EffectiveInterfaceMode() == InterfaceMode.Phone ? new Vector2(430, 982) : new Vector2(960, 650);

    private void ApplyRecommendedWindowSize(InterfaceMode mode)
    {
        var target = mode == InterfaceMode.Phone ? new Vector2(440, 1004) : new Vector2(1120, 760);
        _expandedSize = target;
        if (!_minimized)
            ImGui.SetWindowSize(target);
    }

    public override void Draw()
    {
        SizeConstraints = _minimized
            ? new WindowSizeConstraints
            {
                MinimumSize = _session.Mode == PrismMode.Idle ? MiniIdleSize : MiniActiveSize,
                MaximumSize = _session.Mode == PrismMode.Idle ? MiniIdleSize : MiniActiveSize,
            }
            : GetExpandedConstraints();

        PushTheme();
        try
        {
            if (_minimized)
                DrawMiniRemote();
            else
                DrawShell();

            _dialogs.Draw();
        }
        finally
        {
            PopTheme();
        }
    }

    private void SetMinimized(bool minimized)
    {
        if (_minimized == minimized)
            return;

        if (minimized)
            _expandedSize = ImGui.GetWindowSize();

        _minimized = minimized;
        var min = MinimumExpandedSize();
        var target = minimized
            ? (_session.Mode == PrismMode.Idle ? MiniIdleSize : MiniActiveSize)
            : new Vector2(Math.Max(min.X, _expandedSize.X), Math.Max(min.Y, _expandedSize.Y));
        ImGui.SetWindowSize(target);
    }

    private static float CenterMini(float totalWidth, float contentWidth)
        => MathF.Max(12f, (totalWidth - contentWidth) * 0.5f);

    private void DrawMiniRemote()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(DeviceShell), 17f);
        draw.AddRect(pos + Vector2.One, pos + size - Vector2.One, ImGui.ColorConvertFloat4ToU32(DeviceBorder), 17f, ImDrawFlags.None, 2f);
        draw.AddLine(pos + new Vector2(8, 2), pos + new Vector2(54, 2), U32(AccentHover), 2f);
        draw.AddLine(pos + size - new Vector2(8, 2), pos + size - new Vector2(54, 2), U32(S9Cyan), 2f);

        // Portrait pocket-remote layout inspired by the compact Aetherphone remote:
        // branding at the top, artwork/status in the center, minimal transport and
        // screen controls, and a dedicated power control at the bottom.
        ImGui.SetCursorPos(new Vector2(12, 10));
        ImGui.PushStyleColor(ImGuiCol.Text, AccentHover);
        ImGui.TextUnformatted("◆");
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 5f);
        if (ImGui.Button("PrismCast##MiniExpand", new Vector2(92, 25)))
            SetMinimized(false);

        if (_session.Mode == PrismMode.Idle)
        {
            DrawMiniIdleLogo();
            ImGui.SetCursorPos(new Vector2(18, MiniIdleSize.Y - 55f));
            PushDangerButton();
            if (ImGui.Button("PWR##MiniIdle", new Vector2(MiniIdleSize.X - 36f, 36)))
                IsOpen = false;
            PopDangerButton();
            return;
        }

        var info = _video.ReadInfo();
        var totalWidth = MiniActiveSize.X;
        DrawMiniArtwork();

        var titleWidth = totalWidth - 28f;
        ImGui.SetCursorPos(new Vector2(CenterMini(totalWidth, titleWidth), 181));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + titleWidth);
        ImGui.TextWrapped(TrimForDisplay(CurrentMediaTitle(), 34));
        ImGui.PopTextWrapPos();

        if (_session.Mode == PrismMode.Hosting)
        {
            const float smallButton = 43f;
            const float playButton = 50f;
            const float gap = 5f;
            const float dpadButton = 38f;
            const float row4Button = 38f;
            const float topY = 236f;

            var transportWidth = smallButton + gap + playButton + gap + smallButton;
            ImGui.SetCursorPos(new Vector2(CenterMini(totalWidth, transportWidth), topY));
            if (ImGui.Button("-10##Mini", new Vector2(smallButton, 29)))
                _session.SeekHost(Math.Max(0, info.PositionSeconds - 10));
            ImGui.SameLine(0, gap);
            if (ImGui.Button(info.Paused ? "▶##MiniPlay" : "Ⅱ##MiniPause", new Vector2(playButton, 29)))
                _session.PauseHost(!info.Paused);
            ImGui.SameLine(0, gap);
            if (ImGui.Button("+10##Mini", new Vector2(smallButton, 29)))
                _session.SeekHost(info.PositionSeconds + 10);

            var moveStep = CurrentMovementStep();
            var rot = Math.Max(0.1f, _config.ScreenRotationStepDegrees);
            var dpadWidth = dpadButton * 3f + gap * 2f;
            var dpadX = CenterMini(totalWidth, dpadWidth);
            var dpadCenterX = CenterMini(totalWidth, dpadButton);

            ImGui.SetCursorPos(new Vector2(dpadCenterX, 276));
            if (RepeatButton("↑", "MiniUp", new Vector2(dpadButton, 28))) NudgeScreen(0, moveStep, 0);
            ImGui.SetCursorPos(new Vector2(dpadX, 309));
            if (RepeatButton("←", "MiniLeft", new Vector2(dpadButton, 28))) NudgeLocalHorizontal(-moveStep, 0);
            ImGui.SameLine(0, gap);
            if (ImGui.Button("◎##MiniCenter", new Vector2(dpadButton, 28))) PlaceInFrontOfPlayer();
            ImGui.SameLine(0, gap);
            if (RepeatButton("→", "MiniRight", new Vector2(dpadButton, 28))) NudgeLocalHorizontal(moveStep, 0);
            ImGui.SetCursorPos(new Vector2(dpadCenterX, 342));
            if (RepeatButton("↓", "MiniDown", new Vector2(dpadButton, 28))) NudgeScreen(0, -moveStep, 0);

            var actionRowWidth = row4Button * 4f + gap * 3f;
            ImGui.SetCursorPos(new Vector2(CenterMini(totalWidth, actionRowWidth), 379));
            if (RepeatButton("F", "MiniForward", new Vector2(row4Button, 27))) NudgeLocalHorizontal(0, moveStep);
            ImGui.SameLine(0, gap);
            if (RepeatButton("↺", "MiniRotateLeft", new Vector2(row4Button, 27))) { _screenYawDegreesEdit -= rot; ApplyScreenEdits(); }
            ImGui.SameLine(0, gap);
            if (RepeatButton("↻", "MiniRotateRight", new Vector2(row4Button, 27))) { _screenYawDegreesEdit += rot; ApplyScreenEdits(); }
            ImGui.SameLine(0, gap);
            if (RepeatButton("B", "MiniBack", new Vector2(row4Button, 27))) NudgeLocalHorizontal(0, -moveStep);
        }
        else
        {
            ImGui.SetCursorPos(new Vector2(CenterMini(totalWidth, 140f), 250));
            ImGui.TextDisabled("HOST CONTROLLED");
            ImGui.SetCursorPos(new Vector2(CenterMini(totalWidth, 150f), 280));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 150f);
            ImGui.TextWrapped("Viewing shared session");
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorPos(new Vector2(18, MiniActiveSize.Y - 52f));
        PushDangerButton();
        if (ImGui.Button("PWR##MiniActive", new Vector2(MiniActiveSize.X - 36f, 34)))
            RunUiTask(StopSessionFromUiAsync);
        PopDangerButton();
    }

    private void DrawMiniIdleLogo()
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(22, 52);
        var max = min + new Vector2(MiniIdleSize.X - 44f, 62f);
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.10f)), 12f);
        draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.45f)), 12f);
        var logo = "◆";
        var logoSize = ImGui.CalcTextSize(logo);
        draw.AddText(new Vector2((min.X + max.X - logoSize.X) * 0.5f, min.Y + 12f), ImGui.ColorConvertFloat4ToU32(AccentHover), logo);
        var ready = "READY";
        var readySize = ImGui.CalcTextSize(ready);
        draw.AddText(new Vector2((min.X + max.X - readySize.X) * 0.5f, min.Y + 36f), ImGui.ColorConvertFloat4ToU32(Muted), ready);
    }

    private void DrawMiniArtwork()
    {
        const float posterWidth = 88f;
        const float posterHeight = 124f;
        ImGui.SetCursorPos(new Vector2((MiniActiveSize.X - posterWidth) * 0.5f, 47f));
        if (_nowPlayingPlexItem is { } item)
        {
            DrawPlexPoster(item, new Vector2(posterWidth, posterHeight));
            return;
        }

        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(posterWidth, posterHeight);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.075f, 0.065f, 0.11f, 1f)), 8f);
        draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.45f)), 8f);
        var logo = "◆";
        var logoSize = ImGui.CalcTextSize(logo);
        draw.AddText(min + new Vector2((posterWidth - logoSize.X) * 0.5f, 42f), ImGui.ColorConvertFloat4ToU32(AccentHover), logo);
        var media = "PRISMCAST";
        var mediaSize = ImGui.CalcTextSize(media);
        draw.AddText(min + new Vector2((posterWidth - mediaSize.X) * 0.5f, 70f), ImGui.ColorConvertFloat4ToU32(Muted), media);
        ImGui.Dummy(new Vector2(posterWidth, posterHeight));
    }

    private static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    private static void DrawTechFrame(Vector4? accentOverride = null, float inset = 1f)
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);
        var accent = accentOverride ?? Accent;
        var line = new Vector4(accent.X, accent.Y, accent.Z, 0.58f);
        var cyan = new Vector4(S9Cyan.X, S9Cyan.Y, S9Cyan.Z, 0.72f);
        draw.AddRect(min, max, U32(new Vector4(S9PanelLine.X, S9PanelLine.Y, S9PanelLine.Z, 0.34f)), 10f, ImDrawFlags.None, 1f);

        const float longSeg = 26f;
        const float shortSeg = 10f;
        draw.AddLine(min + new Vector2(0, longSeg), min + new Vector2(0, shortSeg), U32(line), 2f);
        draw.AddLine(min + new Vector2(shortSeg, 0), min + new Vector2(longSeg, 0), U32(line), 2f);
        draw.AddLine(max - new Vector2(0, longSeg), max - new Vector2(0, shortSeg), U32(cyan), 2f);
        draw.AddLine(max - new Vector2(shortSeg, 0), max - new Vector2(longSeg, 0), U32(cyan), 2f);
        draw.AddLine(min + new Vector2(4, 4), min + new Vector2(15, 4), U32(cyan), 1.5f);
        draw.AddLine(max - new Vector2(4, 4), max - new Vector2(15, 4), U32(line), 1.5f);
    }

    private static void DrawPrismGlyph(Vector2 center, float radius)
    {
        var draw = ImGui.GetWindowDrawList();
        var top = center + new Vector2(0, -radius);
        var right = center + new Vector2(radius * 0.72f, 0);
        var bottom = center + new Vector2(0, radius);
        var left = center + new Vector2(-radius * 0.72f, 0);
        draw.AddLine(top, right, U32(AccentHover), 2f);
        draw.AddLine(right, bottom, U32(S9Cyan), 2f);
        draw.AddLine(bottom, left, U32(S9Blue), 2f);
        draw.AddLine(left, top, U32(Accent), 2f);
        draw.AddLine(top, bottom, U32(new Vector4(S9Cyan.X, S9Cyan.Y, S9Cyan.Z, 0.45f)), 1f);
    }

    private static void DrawSectionHeading(string label, string? suffix = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.58f, 0.75f, 1f, 1f));
        ImGui.TextUnformatted(label.ToUpperInvariant());
        ImGui.PopStyleColor();
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Accent);
            ImGui.TextUnformatted(suffix);
            ImGui.PopStyleColor();
        }
    }

    private static void DrawArc(ImDrawListPtr draw, Vector2 center, float radius, float start, float end, int segments, Vector4 color, float thickness)
    {
        var previous = center + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        for (var i = 1; i <= segments; i++)
        {
            var t = start + (end - start) * (i / (float)segments);
            var next = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
            draw.AddLine(previous, next, U32(color), thickness);
            previous = next;
        }
    }

    private static void DrawUiIcon(UiIcon icon, Vector2 center, float size, Vector4 color, float thickness = 2f)
    {
        var draw = ImGui.GetWindowDrawList();
        var c = U32(color);
        var h = size * 0.5f;
        switch (icon)
        {
            case UiIcon.Remote:
                draw.AddRect(center - new Vector2(h * 0.58f, h), center + new Vector2(h * 0.58f, h), c, 3f, ImDrawFlags.None, thickness);
                draw.AddCircleFilled(center - new Vector2(0, h * 0.45f), MathF.Max(1.5f, size * 0.08f), c, 12);
                draw.AddLine(center + new Vector2(-h * 0.28f, h * 0.30f), center + new Vector2(h * 0.28f, h * 0.30f), c, thickness);
                break;
            case UiIcon.Library:
                for (var i = 0; i < 3; i++)
                {
                    var y = center.Y + (i - 1) * size * 0.23f;
                    draw.AddCircle(center + new Vector2(0, (i - 1) * size * 0.23f), h * 0.46f, c, 24, thickness);
                    if (i < 2)
                        draw.AddLine(new Vector2(center.X - h * 0.75f, y), new Vector2(center.X - h * 0.75f, y + size * 0.23f), c, thickness);
                }
                break;
            case UiIcon.People:
                draw.AddCircle(center + new Vector2(-h * 0.30f, -h * 0.25f), h * 0.25f, c, 16, thickness);
                draw.AddCircle(center + new Vector2(h * 0.30f, -h * 0.20f), h * 0.22f, c, 16, thickness);
                draw.AddRect(center + new Vector2(-h * 0.72f, h * 0.12f), center + new Vector2(0.05f * size, h * 0.70f), c, h * 0.22f, ImDrawFlags.None, thickness);
                draw.AddRect(center + new Vector2(-0.02f * size, h * 0.15f), center + new Vector2(h * 0.72f, h * 0.70f), c, h * 0.22f, ImDrawFlags.None, thickness);
                break;
            case UiIcon.Monitor:
                draw.AddRect(center - new Vector2(h, h * 0.62f), center + new Vector2(h, h * 0.62f), c, 2f, ImDrawFlags.None, thickness);
                draw.AddLine(center + new Vector2(0, h * 0.62f), center + new Vector2(0, h * 0.92f), c, thickness);
                draw.AddLine(center + new Vector2(-h * 0.40f, h * 0.92f), center + new Vector2(h * 0.40f, h * 0.92f), c, thickness);
                break;
            case UiIcon.ArrowUp:
            case UiIcon.ArrowDown:
            case UiIcon.ArrowLeft:
            case UiIcon.ArrowRight:
            {
                Vector2 dir = icon switch
                {
                    UiIcon.ArrowUp => new Vector2(0, -1),
                    UiIcon.ArrowDown => new Vector2(0, 1),
                    UiIcon.ArrowLeft => new Vector2(-1, 0),
                    _ => new Vector2(1, 0),
                };
                var perp = new Vector2(-dir.Y, dir.X);
                var tip = center + dir * h * 0.72f;
                var tail = center - dir * h * 0.58f;
                draw.AddLine(tail, tip, c, thickness + 0.5f);
                draw.AddLine(tip, center + dir * h * 0.12f + perp * h * 0.42f, c, thickness + 0.5f);
                draw.AddLine(tip, center + dir * h * 0.12f - perp * h * 0.42f, c, thickness + 0.5f);
                break;
            }
            case UiIcon.Forward:
            case UiIcon.Back:
            {
                var dir = icon == UiIcon.Forward ? -1f : 1f;
                for (var i = 0; i < 2; i++)
                {
                    var y = center.Y + (i - 0.5f) * h * 0.55f;
                    var tip = new Vector2(center.X, y + dir * h * 0.30f);
                    draw.AddLine(new Vector2(center.X - h * 0.55f, y - dir * h * 0.15f), tip, c, thickness);
                    draw.AddLine(tip, new Vector2(center.X + h * 0.55f, y - dir * h * 0.15f), c, thickness);
                }
                break;
            }
            case UiIcon.RotateLeft:
            case UiIcon.RotateRight:
            case UiIcon.Reset:
            {
                var clockwise = icon == UiIcon.RotateRight;
                var start = clockwise ? -2.45f : -0.70f;
                var end = clockwise ? 0.75f : -3.90f;
                DrawArc(draw, center, h * 0.72f, start, end, 18, color, thickness);
                var t = end;
                var tip = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * h * 0.72f;
                var tangent = new Vector2(-MathF.Sin(t), MathF.Cos(t)) * (clockwise ? 1f : -1f);
                var normal = new Vector2(-tangent.Y, tangent.X);
                draw.AddLine(tip, tip - tangent * h * 0.38f + normal * h * 0.22f, c, thickness);
                draw.AddLine(tip, tip - tangent * h * 0.38f - normal * h * 0.22f, c, thickness);
                break;
            }
            case UiIcon.PlaceFront:
                DrawUiIcon(UiIcon.Monitor, center, size * 0.82f, color, thickness);
                draw.AddLine(center + new Vector2(-h * 0.20f, -h * 0.95f), center + new Vector2(h * 0.50f, -h * 0.95f), c, thickness);
                draw.AddLine(center + new Vector2(h * 0.50f, -h * 0.95f), center + new Vector2(h * 0.28f, -h * 1.15f), c, thickness);
                draw.AddLine(center + new Vector2(h * 0.50f, -h * 0.95f), center + new Vector2(h * 0.28f, -h * 0.75f), c, thickness);
                break;
            case UiIcon.Movie:
            {
                var min = center - new Vector2(h * 0.90f, h * 0.62f);
                var max = center + new Vector2(h * 0.90f, h * 0.62f);
                draw.AddRect(min, max, c, 2f, ImDrawFlags.None, thickness);
                for (var i = -1; i <= 1; i += 2)
                {
                    var x = center.X + i * h * 0.63f;
                    draw.AddCircleFilled(new Vector2(x, center.Y - h * 0.37f), MathF.Max(1.2f, h * 0.09f), c, 10);
                    draw.AddCircleFilled(new Vector2(x, center.Y + h * 0.37f), MathF.Max(1.2f, h * 0.09f), c, 10);
                }
                break;
            }
            case UiIcon.Television:
                draw.AddRect(center - new Vector2(h * 0.90f, h * 0.62f), center + new Vector2(h * 0.90f, h * 0.62f), c, 3f, ImDrawFlags.None, thickness);
                draw.AddLine(center + new Vector2(-h * 0.24f, -h * 0.70f), center + new Vector2(-h * 0.55f, -h * 1.02f), c, thickness);
                draw.AddLine(center + new Vector2(h * 0.24f, -h * 0.70f), center + new Vector2(h * 0.55f, -h * 1.02f), c, thickness);
                draw.AddLine(center + new Vector2(-h * 0.34f, h * 0.82f), center + new Vector2(h * 0.34f, h * 0.82f), c, thickness);
                break;
            case UiIcon.AnimeSpark:
                draw.AddLine(center + new Vector2(0, -h), center + new Vector2(0, h), c, thickness);
                draw.AddLine(center + new Vector2(-h, 0), center + new Vector2(h, 0), c, thickness);
                draw.AddLine(center + new Vector2(-h * 0.58f, -h * 0.58f), center + new Vector2(h * 0.58f, h * 0.58f), c, thickness * 0.78f);
                draw.AddLine(center + new Vector2(h * 0.58f, -h * 0.58f), center + new Vector2(-h * 0.58f, h * 0.58f), c, thickness * 0.78f);
                draw.AddCircleFilled(center, MathF.Max(1.3f, h * 0.12f), c, 12);
                break;
            case UiIcon.FilmReel:
                draw.AddCircle(center, h * 0.90f, c, 28, thickness);
                for (var i = 0; i < 4; i++)
                {
                    var a = MathF.PI * 0.25f + i * MathF.PI * 0.5f;
                    var hole = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * h * 0.43f;
                    draw.AddCircle(hole, h * 0.18f, c, 14, thickness * 0.86f);
                }
                draw.AddCircleFilled(center, h * 0.08f, c, 10);
                draw.AddLine(center + new Vector2(h * 0.72f, h * 0.52f), center + new Vector2(h * 1.06f, h * 0.88f), c, thickness);
                break;
            case UiIcon.Folder:
                draw.AddRect(center - new Vector2(h * 0.92f, h * 0.50f), center + new Vector2(h * 0.92f, h * 0.64f), c, 3f, ImDrawFlags.None, thickness);
                draw.AddLine(center + new Vector2(-h * 0.90f, -h * 0.50f), center + new Vector2(-h * 0.42f, -h * 0.82f), c, thickness);
                draw.AddLine(center + new Vector2(-h * 0.42f, -h * 0.82f), center + new Vector2(h * 0.10f, -h * 0.82f), c, thickness);
                break;
            case UiIcon.Link:
            {
                var a = center + new Vector2(-h * 0.30f, h * 0.24f);
                var b = center + new Vector2(h * 0.30f, -h * 0.24f);
                draw.AddCircle(a, h * 0.50f, c, 20, thickness);
                draw.AddCircle(b, h * 0.50f, c, 20, thickness);
                draw.AddLine(center + new Vector2(-h * 0.18f, h * 0.14f), center + new Vector2(h * 0.18f, -h * 0.14f), c, thickness + 0.4f);
                break;
            }
            case UiIcon.Search:
                draw.AddCircle(center - new Vector2(h * 0.14f, h * 0.14f), h * 0.52f, c, 20, thickness);
                draw.AddLine(center + new Vector2(h * 0.24f, h * 0.24f), center + new Vector2(h * 0.86f, h * 0.86f), c, thickness);
                break;
            case UiIcon.Play:
            {
                var p1 = center + new Vector2(-h * 0.40f, -h * 0.62f);
                var p2 = center + new Vector2(-h * 0.40f, h * 0.62f);
                var p3 = center + new Vector2(h * 0.68f, 0);
                draw.AddTriangleFilled(p1, p2, p3, c);
                break;
            }
            case UiIcon.Ticket:
            {
                var min = center - new Vector2(h * 0.92f, h * 0.58f);
                var max = center + new Vector2(h * 0.92f, h * 0.58f);
                draw.AddRect(min, max, c, 2f, ImDrawFlags.None, thickness);
                draw.AddCircleFilled(new Vector2(min.X, center.Y), h * 0.17f, U32(DeviceScreen), 12);
                draw.AddCircleFilled(new Vector2(max.X, center.Y), h * 0.17f, U32(DeviceScreen), 12);
                draw.AddLine(center - new Vector2(0, h * 0.38f), center + new Vector2(0, h * 0.38f), c, thickness * 0.72f);
                break;
            }
            case UiIcon.Crown:
            {
                var left = center + new Vector2(-h * 0.90f, h * 0.52f);
                var right = center + new Vector2(h * 0.90f, h * 0.52f);
                var points = new[]
                {
                    left,
                    center + new Vector2(-h * 0.72f, -h * 0.54f),
                    center + new Vector2(-h * 0.22f, h * 0.02f),
                    center + new Vector2(0, -h * 0.82f),
                    center + new Vector2(h * 0.22f, h * 0.02f),
                    center + new Vector2(h * 0.72f, -h * 0.54f),
                    right,
                };
                for (var i = 0; i < points.Length - 1; i++)
                    draw.AddLine(points[i], points[i + 1], c, thickness);
                draw.AddLine(left, right, c, thickness);
                break;
            }
            case UiIcon.Copy:
                draw.AddRect(center - new Vector2(h * 0.72f, h * 0.52f), center + new Vector2(h * 0.54f, h * 0.74f), c, 2f, ImDrawFlags.None, thickness);
                draw.AddRect(center - new Vector2(h * 0.44f, h * 0.78f), center + new Vector2(h * 0.82f, h * 0.48f), c, 2f, ImDrawFlags.None, thickness);
                break;
            case UiIcon.Plus:
                draw.AddLine(center - new Vector2(h * 0.66f, 0), center + new Vector2(h * 0.66f, 0), c, thickness);
                draw.AddLine(center - new Vector2(0, h * 0.66f), center + new Vector2(0, h * 0.66f), c, thickness);
                break;
            case UiIcon.ChevronRight:
                draw.AddLine(center - new Vector2(h * 0.30f, h * 0.54f), center + new Vector2(h * 0.30f, 0), c, thickness);
                draw.AddLine(center + new Vector2(h * 0.30f, 0), center + new Vector2(-h * 0.30f, h * 0.54f), c, thickness);
                break;
            case UiIcon.Gear:
            {
                // Actual cog silhouette rather than radial spokes, which looked like a star at phone scale.
                const int teeth = 8;
                var innerRadius = h * 0.54f;
                var outerRadius = h * 0.86f;
                var points = new List<Vector2>(teeth * 4);
                for (var i = 0; i < teeth; i++)
                {
                    var mid = i * MathF.Tau / teeth;
                    var a0 = mid - 0.22f;
                    var a1 = mid - 0.09f;
                    var a2 = mid + 0.09f;
                    var a3 = mid + 0.22f;
                    points.Add(center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * innerRadius);
                    points.Add(center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * outerRadius);
                    points.Add(center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * outerRadius);
                    points.Add(center + new Vector2(MathF.Cos(a3), MathF.Sin(a3)) * innerRadius);
                }
                for (var i = 0; i < points.Count; i++)
                    draw.AddLine(points[i], points[(i + 1) % points.Count], c, thickness);
                draw.AddCircle(center, h * 0.24f, c, 20, thickness);
                break;
            }
        }
    }

    private bool RepeatCurrentInvisible(string id)
    {
        var active = ImGui.IsItemActive();
        var now = DateTime.UtcNow;
        if (!active)
        {
            _repeatButtonNextFire.Remove(id);
            return false;
        }
        if (!_repeatButtonNextFire.TryGetValue(id, out var next))
        {
            _repeatButtonNextFire[id] = now.AddMilliseconds(230);
            return true;
        }
        if (now < next) return false;
        _repeatButtonNextFire[id] = now.AddMilliseconds(48);
        return true;
    }

    private bool IconTileButton(string id, UiIcon icon, string label, Vector2 size, bool repeat = false, bool active = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##{id}", size);
        var clicked = repeat ? RepeatCurrentInvisible(id) : ImGui.IsItemClicked();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        var fill = active ? new Vector4(0.30f, 0.14f, 0.52f, 0.98f) : new Vector4(0.075f, 0.085f, 0.155f, 0.98f);
        if (hovered) fill = active ? new Vector4(0.38f, 0.18f, 0.65f, 1f) : new Vector4(0.10f, 0.12f, 0.22f, 1f);
        var border = held || active ? AccentHover : new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.78f);
        draw.AddRectFilled(pos, max, U32(fill), 7f);
        draw.AddRect(pos, max, U32(border), 7f, ImDrawFlags.None, active ? 2f : 1.3f);
        if (active)
            draw.AddLine(pos + new Vector2(8, 1), pos + new Vector2(size.X - 8, 1), U32(AccentHover), 2f);
        var iconCenter = pos + new Vector2(size.X * 0.5f, size.Y * 0.34f);
        DrawUiIcon(icon, iconCenter, MathF.Min(24f, size.Y * 0.34f), Vector4.One, 2.2f);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2((size.X - textSize.X) * 0.5f, size.Y - textSize.Y - 7f), U32(Vector4.One), label);
        return clicked;
    }

    private bool HorizontalIconButton(string id, UiIcon icon, string label, Vector2 size, bool active = false)
    {
        var pos = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        var fill = active ? new Vector4(0.30f, 0.14f, 0.52f, 0.98f) : new Vector4(0.075f, 0.085f, 0.155f, 0.98f);
        if (hovered) fill = active ? new Vector4(0.38f, 0.18f, 0.65f, 1f) : new Vector4(0.10f, 0.12f, 0.22f, 1f);
        draw.AddRectFilled(pos, max, U32(fill), 7f);
        draw.AddRect(pos, max, U32(active ? AccentHover : S9Blue), 7f, ImDrawFlags.None, active ? 2f : 1.2f);
        DrawUiIcon(icon, pos + new Vector2(22f, size.Y * 0.5f), 18f, Vector4.One, 2f);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2(40f, (size.Y - textSize.Y) * 0.5f), U32(Vector4.One), label);
        return pressed;
    }

    private static void PushTechButton(bool active = false)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active
            ? new Vector4(0.30f, 0.14f, 0.52f, 0.96f)
            : new Vector4(0.08f, 0.09f, 0.16f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.18f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.20f, 0.70f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, active ? AccentHover : new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.50f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
    }

    private static void PopTechButton()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }

    private static void ExtractEmbeddedUiAsset(string resourceSuffix, string destinationPath)
    {
        try
        {
            var assembly = typeof(PrismCastWindow).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(resourceName))
                return;

            using var input = assembly.GetManifestResourceStream(resourceName);
            if (input is null)
                return;

            using var output = File.Create(destinationPath);
            input.CopyTo(output);
        }
        catch
        {
            // UI artwork is cosmetic. PrismCast should still open if an asset cannot be extracted.
        }
    }

    private bool DrawUiAsset(string path, Vector2 size)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            var shared = TextureProvider.GetFromFileAbsolute(path);
            if (shared.TryGetWrap(out var wrap, out _) && wrap is not null)
            {
                ImGui.Image(wrap.Handle, size);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void DrawShell()
    {
        if (EffectiveInterfaceMode() == InterfaceMode.Phone)
        {
            DrawPhoneShell();
            return;
        }

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        var shellColor = ImGui.ColorConvertFloat4ToU32(DeviceShell);
        var borderColor = ImGui.ColorConvertFloat4ToU32(DeviceBorder);
        var screenColor = ImGui.ColorConvertFloat4ToU32(DeviceScreen);

        draw.AddRectFilled(windowPos, windowPos + windowSize, shellColor, 24f);
        draw.AddRect(windowPos + new Vector2(1, 1), windowPos + windowSize - new Vector2(1, 1), borderColor, 24f, ImDrawFlags.None, 2f);

        var screenMin = windowPos + new Vector2(DeviceInset, DeviceInset);
        var screenMax = windowPos + windowSize - new Vector2(DeviceInset, DeviceInset);
        draw.AddRectFilled(screenMin, screenMax, screenColor, 17f);

        // Small centered tablet camera/sensor detail. Purely visual, because humans apparently
        // trust a rectangle more when it has a tiny dot on it.
        var sensorCenter = new Vector2((screenMin.X + screenMax.X) * 0.5f, screenMin.Y + 7f);
        draw.AddCircleFilled(sensorCenter, 2.3f, ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.18f, 0.34f, 1f)));

        ImGui.SetCursorPos(new Vector2(DeviceInset, DeviceInset));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, DeviceScreen);
        if (ImGui.BeginChild("##PrismDeviceScreen", windowSize - new Vector2(DeviceInset * 2f, DeviceInset * 2f), false))
        {
            DrawDeviceHeader();

            var available = ImGui.GetContentRegionAvail();
            var mainWidth = Math.Max(0, available.X - SidebarWidth - 8f);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, SidebarBg);
            if (ImGui.BeginChild("##PrismSidebar", new Vector2(SidebarWidth, available.Y), false))
                DrawSidebar();
            ImGui.EndChild();
            ImGui.PopStyleColor();

            ImGui.SameLine(0, 8f);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
            if (ImGui.BeginChild("##PrismMain", new Vector2(mainWidth, available.Y), false))
            {
                DrawTopBar();
                ImGui.Separator();

                var hasBottomPlayer = _session.Mode != PrismMode.Idle && _page != Page.RemoteControl;
                var pageHeight = ImGui.GetContentRegionAvail().Y - (hasBottomPlayer ? BottomPlayerHeight + 8f : 0f);
                if (ImGui.BeginChild("##PrismPage", new Vector2(0, Math.Max(1, pageHeight)), false))
                    DrawCurrentPage();
                ImGui.EndChild();

                if (hasBottomPlayer)
                {
                    ImGui.Spacing();
                    DrawBottomNowPlaying();
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();

        // Top-layer shell cap: draw a masking rim and then re-draw the neon tube so any
        // square corner bleed from child windows is hidden under the rounded device edge.
        var capMin = windowPos + new Vector2(10.5f, 10.5f);
        var capMax = windowPos + windowSize - new Vector2(10.5f, 10.5f);
        draw.AddRect(capMin, capMax, shellColor, 23f, ImDrawFlags.None, 9.0f);
        draw.AddRect(capMin + new Vector2(1.5f, 1.5f), capMax - new Vector2(1.5f, 1.5f), U32(new Vector4(0.035f, 0.040f, 0.075f, 0.95f)), 21f, ImDrawFlags.None, 3.0f);

        var overlayGlow = new[]
        {
            (inset: 1.2f, thickness: 8f, color: new Vector4(0.50f, 0.10f, 0.98f, 0.10f), rounding: 28f),
            (inset: 3.2f, thickness: 5f, color: new Vector4(0.30f, 0.24f, 1.00f, 0.16f), rounding: 27f),
        };
        foreach (var layer in overlayGlow)
        {
            var min = windowPos + new Vector2(layer.inset, layer.inset);
            var max = windowPos + windowSize - new Vector2(layer.inset, layer.inset);
            draw.AddRect(min, max, U32(layer.color), layer.rounding, ImDrawFlags.None, layer.thickness);
        }
        draw.AddRect(windowPos + new Vector2(7f, 7f), windowPos + windowSize - new Vector2(7f, 7f), U32(new Vector4(0.86f, 0.58f, 1.00f, 0.96f)), 24f, ImDrawFlags.None, 3.2f);
        draw.AddRect(windowPos + new Vector2(9.5f, 9.5f), windowPos + windowSize - new Vector2(9.5f, 9.5f), U32(new Vector4(0.24f, 0.88f, 1.00f, 0.72f)), 22f, ImDrawFlags.None, 1.3f);

        ImGui.PopStyleColor();
    }

    private void DrawPhoneShell()
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var draw = ImGui.GetWindowDrawList();
        var shellColor = ImGui.ColorConvertFloat4ToU32(DeviceShell);
        var screenColor = ImGui.ColorConvertFloat4ToU32(DeviceScreen);

        draw.AddRectFilled(windowPos, windowPos + windowSize, shellColor, 30f);

        // Neon tube shell: layered outer glow plus a bright inner rim.
        // ImGui cannot blur, so the effect is faked with stacked outlines at increasing widths.
        var glowLayers = new[]
        {
            (inset: 0.5f, thickness: 12f, color: new Vector4(0.58f, 0.10f, 0.98f, 0.08f), rounding: 29f),
            (inset: 1.5f, thickness: 8f, color: new Vector4(0.46f, 0.12f, 1.00f, 0.12f), rounding: 28f),
            (inset: 3.0f, thickness: 5f, color: new Vector4(0.34f, 0.22f, 1.00f, 0.18f), rounding: 27f),
            (inset: 5.5f, thickness: 3f, color: new Vector4(0.28f, 0.70f, 1.00f, 0.22f), rounding: 25f),
        };
        foreach (var layer in glowLayers)
        {
            var min = windowPos + new Vector2(layer.inset, layer.inset);
            var max = windowPos + windowSize - new Vector2(layer.inset, layer.inset);
            draw.AddRect(min, max, U32(layer.color), layer.rounding, ImDrawFlags.None, layer.thickness);
        }

        var tubeOuterMin = windowPos + new Vector2(7f, 7f);
        var tubeOuterMax = windowPos + windowSize - new Vector2(7f, 7f);
        draw.AddRect(tubeOuterMin, tubeOuterMax, U32(new Vector4(0.82f, 0.55f, 1.00f, 0.95f)), 24f, ImDrawFlags.None, 2.8f);
        var tubeInnerMin = windowPos + new Vector2(9.5f, 9.5f);
        var tubeInnerMax = windowPos + windowSize - new Vector2(9.5f, 9.5f);
        draw.AddRect(tubeInnerMin, tubeInnerMax, U32(new Vector4(0.24f, 0.88f, 1.00f, 0.68f)), 22f, ImDrawFlags.None, 1.2f);

        // Slight magenta bloom near the top/bottom, closer to the reference's lit neon aura.
        draw.AddRectFilled(windowPos + new Vector2(24f, 0f), windowPos + new Vector2(windowSize.X - 24f, 38f), U32(new Vector4(0.55f, 0.08f, 0.90f, 0.10f)), 18f);
        draw.AddRectFilled(windowPos + new Vector2(22f, windowSize.Y - 54f), windowPos + new Vector2(windowSize.X - 22f, windowSize.Y - 6f), U32(new Vector4(0.45f, 0.08f, 0.88f, 0.07f)), 20f);

        var screenMin = windowPos + new Vector2(PhoneScreenInset, PhoneScreenInset);
        var screenMax = windowPos + windowSize - new Vector2(PhoneScreenInset, PhoneScreenInset);
        draw.AddRectFilled(screenMin, screenMax, screenColor, 20f);

        ImGui.SetCursorPos(new Vector2(PhoneScreenInset, PhoneScreenInset));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, DeviceScreen);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 20f);
        if (ImGui.BeginChild("##PrismPhoneScreen", windowSize - new Vector2(PhoneScreenInset * 2f, PhoneScreenInset * 2f), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawPhoneStatusBar();
            DrawPhoneAppHeader();

            var available = ImGui.GetContentRegionAvail();
            var showBottomPlayer = _session.Mode != PrismMode.Idle && _page != Page.RemoteControl;
            var reserved = PhoneBottomNavHeight + 8f + (showBottomPlayer ? BottomPlayerHeight + 8f : 0f);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
            if (ImGui.BeginChild("##PrismPhoneMain", new Vector2(0, Math.Max(1, available.Y - reserved)), false,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (ImGui.BeginChild("##PrismPhonePage", new Vector2(0, 0), false,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    DrawCurrentPage();
                ImGui.EndChild();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();

            if (showBottomPlayer)
            {
                ImGui.Spacing();
                DrawBottomNowPlaying();
            }

            ImGui.Spacing();
            DrawPhoneBottomNav();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneStatusBar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.022f, 0.024f, 0.045f, 1f));
        if (ImGui.BeginChild("##PhoneStatusBar", new Vector2(0, PhoneStatusBarHeight), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var w = ImGui.GetWindowWidth();
            var draw = ImGui.GetWindowDrawList();

            ImGui.SetCursorPos(new Vector2(12, 13));
            ImGui.TextUnformatted(DateTime.Now.ToString("h:mm"));

            var state = _session.Mode switch
            {
                PrismMode.Hosting => "LIVE",
                PrismMode.Viewing => "CONNECTED",
                _ => "READY",
            };
            var stateColor = _session.Mode == PrismMode.Idle ? Muted : Good;
            var stateText = $"● {state}";
            var stateW = ImGui.CalcTextSize(stateText).X;

            const float buttonW = 42f;
            const float buttonH = 34f;
            const float gap = 6f;
            var controlsX = w - buttonW * 2f - gap - 10f;
            var stateX = Math.Max(90f, controlsX - stateW - 18f);

            ImGui.SetCursorPos(new Vector2(stateX, 13));
            ImGui.PushStyleColor(ImGuiCol.Text, stateColor);
            ImGui.TextUnformatted(stateText);
            ImGui.PopStyleColor();

            // Large custom window controls. These deliberately read as controls instead of
            // tiny pieces of punctuation floating in the status bar.
            ImGui.SetCursorPos(new Vector2(controlsX, 5f));
            var minPos = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##PhoneMin", new Vector2(buttonW, buttonH)))
                SetMinimized(true);
            var minHover = ImGui.IsItemHovered();
            var minMax = minPos + new Vector2(buttonW, buttonH);
            draw.AddRectFilled(minPos, minMax,
                U32(minHover ? new Vector4(0.10f, 0.19f, 0.34f, 1f) : new Vector4(0.055f, 0.085f, 0.15f, 0.98f)), 8f);
            draw.AddRect(minPos, minMax,
                U32(minHover ? S9Cyan : new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.70f)), 8f, ImDrawFlags.None, 1.5f);
            var minCenter = minPos + new Vector2(buttonW * 0.5f, buttonH * 0.5f);
            draw.AddLine(minCenter + new Vector2(-8f, 3f), minCenter + new Vector2(8f, 3f), U32(Vector4.One), 2.7f);

            ImGui.SameLine(0, gap);
            var closePos = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##PhoneClose", new Vector2(buttonW, buttonH)))
                IsOpen = false;
            var closeHover = ImGui.IsItemHovered();
            var closeMax = closePos + new Vector2(buttonW, buttonH);
            draw.AddRectFilled(closePos, closeMax,
                U32(closeHover ? new Vector4(0.66f, 0.12f, 0.20f, 1f) : new Vector4(0.34f, 0.07f, 0.12f, 0.98f)), 8f);
            draw.AddRect(closePos, closeMax,
                U32(closeHover ? new Vector4(1f, 0.43f, 0.50f, 1f) : new Vector4(0.88f, 0.26f, 0.34f, 0.90f)), 8f, ImDrawFlags.None, 1.5f);
            var closeCenter = closePos + new Vector2(buttonW * 0.5f, buttonH * 0.5f);
            draw.AddLine(closeCenter + new Vector2(-6f, -6f), closeCenter + new Vector2(6f, 6f), U32(Vector4.One), 2.6f);
            draw.AddLine(closeCenter + new Vector2(6f, -6f), closeCenter + new Vector2(-6f, 6f), U32(Vector4.One), 2.6f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneAppHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, HeaderBg);
        if (ImGui.BeginChild("##PhoneAppHeader", new Vector2(0, PhoneAppHeaderHeight), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var w = ImGui.GetWindowWidth();
            var origin = ImGui.GetWindowPos();
            var draw = ImGui.GetWindowDrawList();
            draw.AddLine(origin + new Vector2(10, PhoneAppHeaderHeight - 1), origin + new Vector2(w - 10, PhoneAppHeaderHeight - 1), U32(new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.45f)), 1f);
            draw.AddLine(origin + new Vector2(10, PhoneAppHeaderHeight - 1), origin + new Vector2(318, PhoneAppHeaderHeight - 1), U32(Accent), 2f);

            // Larger supplied PrismCast wordmark so the name and tagline remain readable in phone mode.
            ImGui.SetCursorPos(new Vector2(8f, 1f));
            if (!DrawUiAsset(_logoAssetPath, new Vector2(320f, 105f)))
            {
                DrawPrismGlyph(origin + new Vector2(27, PhoneAppHeaderHeight * 0.5f), 12f);
                ImGui.SetCursorPos(new Vector2(47, 31));
                ImGui.TextUnformatted("PrismCast");
            }

            const float gearSize = 40f;
            const float stopSize = 36f;
            var gearPos = new Vector2(w - gearSize - 11f, 33f);

            if (_session.Mode == PrismMode.Hosting)
            {
                var stopPos = new Vector2(gearPos.X - stopSize - 8f, 35f);
                ImGui.SetCursorPos(stopPos);
                var stopScreen = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton("##PhoneStopSession", new Vector2(stopSize, stopSize)))
                    RunUiTask(StopSessionFromUiAsync);
                var stopHovered = ImGui.IsItemHovered();
                if (stopHovered)
                    ImGui.SetTooltip("Stop Session");
                var stopCenter = stopScreen + new Vector2(stopSize * 0.5f);
                draw.AddCircleFilled(stopCenter, stopSize * 0.48f,
                    U32(stopHovered ? new Vector4(0.58f, 0.10f, 0.18f, 0.98f) : new Vector4(0.40f, 0.07f, 0.13f, 0.94f)), 24);
                draw.AddCircle(stopCenter, stopSize * 0.47f, U32(new Vector4(1f, 0.30f, 0.38f, 0.88f)), 24, 1.5f);
                const float square = 9f;
                draw.AddRectFilled(stopCenter - new Vector2(square * 0.5f), stopCenter + new Vector2(square * 0.5f), U32(Vector4.One), 1.5f);
            }

            ImGui.SetCursorPos(gearPos);
            var gearScreen = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##PhoneSettingsGear", new Vector2(gearSize, gearSize)))
            {
                _phoneSettingsHome = true;
                SelectPage(Page.Settings);
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetTooltip("Settings");
            if (hovered)
                draw.AddCircleFilled(gearScreen + new Vector2(gearSize * 0.5f), gearSize * 0.48f, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.18f)), 24);
            DrawUiIcon(UiIcon.Gear, gearScreen + new Vector2(gearSize * 0.5f), 25f, hovered ? AccentHover : Vector4.One, 1.9f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawDeviceHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, HeaderBg);
        if (ImGui.BeginChild("##DeviceHeader", new Vector2(0, DeviceHeaderHeight), false))
        {
            var isPhone = EffectiveInterfaceMode() == InterfaceMode.Phone;
            var w = ImGui.GetWindowWidth();
            var draw = ImGui.GetWindowDrawList();
            var origin = ImGui.GetWindowPos();
            draw.AddLine(origin + new Vector2(12, DeviceHeaderHeight - 1), origin + new Vector2(w - 12, DeviceHeaderHeight - 1), U32(new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.35f)), 1f);
            draw.AddLine(origin + new Vector2(12, DeviceHeaderHeight - 1), origin + new Vector2(90, DeviceHeaderHeight - 1), U32(Accent), 2f);

            ImGui.SetCursorPos(new Vector2(8f, 1f));
            if (!DrawUiAsset(_logoAssetPath, new Vector2(136f, 44f)))
            {
                DrawPrismGlyph(origin + new Vector2(22, DeviceHeaderHeight * 0.5f), 10f);
                ImGui.SetCursorPos(new Vector2(39, 10));
                ImGui.TextUnformatted("PrismCast");
            }

            var status = _session.Mode switch
            {
                PrismMode.Hosting => "LIVE",
                PrismMode.Viewing => "CONNECTED",
                _ => "READY"
            };
            var statusColor = _session.Mode == PrismMode.Idle ? Muted : Good;
            const float b = 30f;
            var buttonCount = isPhone ? 4 : _session.Mode == PrismMode.Hosting ? 3 : 2;
            var startButtons = w - (b * buttonCount) - (2f * (buttonCount - 1)) - 10f;

            if (!isPhone)
            {
                var sw = ImGui.CalcTextSize(status).X;
                ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 20f, startButtons - sw - 26f));
                ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                ImGui.TextUnformatted($"● {status}");
                ImGui.PopStyleColor();
            }

            ImGui.SetCursorPos(new Vector2(startButtons, 8));
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f));
            if (isPhone)
            {
                if (ImGui.Button("N##OpenChangelog", new Vector2(b, 28))) SelectPage(Page.Changelog);
                ImGui.SameLine(0, 2f);
                if (ImGui.Button("S##OpenSettings", new Vector2(b, 28))) SelectPage(Page.Settings);
                ImGui.SameLine(0, 2f);
            }
            else if (_session.Mode == PrismMode.Hosting)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.34f, 0.07f, 0.12f, 0.98f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.66f, 0.12f, 0.20f, 1f));
                if (ImGui.Button("■##StopTabletSession", new Vector2(b, 28)))
                    RunUiTask(StopSessionFromUiAsync);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Stop Session");
                ImGui.PopStyleColor(2);
                ImGui.SameLine(0, 2f);
            }
            if (ImGui.Button("–##MinimizePrism", new Vector2(b, 28))) SetMinimized(true);
            ImGui.SameLine(0, 2f);
            if (ImGui.Button("×##ClosePrism", new Vector2(b, 28))) IsOpen = false;
            ImGui.PopStyleColor(2);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneBottomNav()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.035f, 0.035f, 0.065f, 0.98f));
        if (ImGui.BeginChild("##PhoneBottomNav", new Vector2(0, PhoneBottomNavHeight), true))
        {
            DrawTechFrame();
            var width = (ImGui.GetContentRegionAvail().X - 16f) / 3f;
            BottomNavButton(Page.RemoteControl, UiIcon.Remote, "Remote", width);
            ImGui.SameLine(0, 8f);
            BottomNavButton(Page.PlexLibrary, UiIcon.Library, "Library", width);
            ImGui.SameLine(0, 8f);
            BottomNavButton(Page.JoinSession, UiIcon.People, "Session", width);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void BottomNavButton(Page page, UiIcon icon, string label, float width)
    {
        var active = page == Page.PlexLibrary
            ? (_page == Page.PlexLibrary || _page == Page.LocalFiles)
            : _page == page;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, 52f);
        if (ImGui.InvisibleButton($"##BottomNav{page}", size))
            SelectPage(page);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        if (active || hovered)
        {
            var fill = active ? new Vector4(0.24f, 0.10f, 0.42f, 0.95f) : new Vector4(0.10f, 0.08f, 0.18f, 0.85f);
            draw.AddRectFilled(pos, max, U32(fill), 8f);
            draw.AddRect(pos, max, U32(active ? AccentHover : S9Blue), 8f, ImDrawFlags.None, active ? 1.8f : 1f);
        }
        if (active)
        {
            draw.AddLine(pos + new Vector2(8, 1), pos + new Vector2(size.X - 8, 1), U32(AccentHover), 2.5f);
            draw.AddCircleFilled(pos + new Vector2(size.X * 0.5f, 17f), 15f, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.12f)), 24);
        }
        DrawUiIcon(icon, pos + new Vector2(size.X * 0.5f, 17f), 19f, active ? AccentHover : new Vector4(0.72f, 0.75f, 0.92f, 1f), 2f);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(pos + new Vector2((size.X - textSize.X) * 0.5f, 33f), U32(active ? Vector4.One : new Vector4(0.80f, 0.82f, 0.92f, 1f)), label);
    }

    private void DrawSidebar()
    {
        ImGui.Dummy(new Vector2(1, 10));
        var p = ImGui.GetWindowPos();
        DrawPrismGlyph(p + new Vector2(22, 27), 10f);
        ImGui.SetCursorPosX(40);
        ImGui.PushStyleColor(ImGuiCol.Text, AccentHover);
        ImGui.TextUnformatted("PRISMCAST");
        ImGui.PopStyleColor();
        ImGui.SetCursorPosX(40);
        ImGui.TextDisabled("S9 MEDIA NETWORK");
        ImGui.Dummy(new Vector2(1, 18));

        SidebarButton(Page.RemoteControl, "REMOTE");
        SidebarButton(Page.PlexLibrary, "LIBRARY");
        SidebarButton(Page.JoinSession, "SESSION");

        ImGui.Dummy(new Vector2(1, 12));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1, 8));
        SidebarButton(Page.Settings, "SETTINGS");

        var changelogY = ImGui.GetWindowHeight() - 244f;
        if (ImGui.GetCursorPosY() < changelogY)
            ImGui.SetCursorPosY(changelogY);

        ImGui.Separator();
        ImGui.Dummy(new Vector2(1, 7));
        DrawSidebarChangelog();
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1, 8));
        DrawSidebarSessionState();
    }

    private void SidebarButton(Page page, string label)
    {
        var active = page == Page.PlexLibrary ? (_page == Page.PlexLibrary || _page == Page.LocalFiles) : _page == page;
        PushTechButton(active);
        if (ImGui.Button(label, new Vector2(-1, 38)))
            SelectPage(page);
        PopTechButton();
    }

    private void DrawSidebarChangelog()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        ImGui.TextUnformatted("CHANGELOG");
        ImGui.PopStyleColor();

        for (var i = 0; i < Math.Min(3, RecentChanges.Length); i++)
        {
            var entry = RecentChanges[i];
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.16f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.25f));
            if (ImGui.Button($"{entry.Version}  {entry.Title}##Change{i}", new Vector2(-1, 24)))
            {
                _selectedChangelogIndex = i;
                SelectPage(Page.Changelog);
            }
            ImGui.PopStyleColor(3);
        }
    }

    private void DrawSidebarSessionState()
    {
        var (label, color) = _session.Mode switch
        {
            PrismMode.Hosting => ("HOSTING", Good),
            PrismMode.Viewing => ("CONNECTED", Good),
            _ => ("IDLE", Muted)
        };

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted($"● {label}");
        ImGui.PopStyleColor();

        if (_session.Mode == PrismMode.Hosting && !string.IsNullOrWhiteSpace(_session.CurrentTitle))
            ImGui.TextWrapped(TrimForDisplay(_session.CurrentTitle!, 24));
        else if (_session.Mode == PrismMode.Viewing && _session.ViewerState is { } viewer)
            ImGui.TextWrapped(TrimForDisplay(viewer.Title, 24));
        else
            ImGui.TextDisabled(_deps.Status);
    }

    private void SelectPage(Page page)
    {
        if (page == Page.Settings && _page != Page.Settings)
            _phoneSettingsHome = true;
        _page = page;
        if (_config.RememberLastPage)
        {
            _config.LastPage = (int)page;
            SaveConfig();
        }
    }

    private void DrawTopBar()
    {
        var title = _page switch
        {
            Page.RemoteControl => "Remote",
            Page.PlexLibrary => "Library",
            Page.LocalFiles => "Library",
            Page.JoinSession => "Session",
            Page.Settings => "Settings",
            Page.Changelog => "What's New",
            _ => "PrismCast"
        };

        ImGui.Dummy(new Vector2(1, 5));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.92f, 0.94f, 1f, 1f));
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 8f);
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        ImGui.TextUnformatted("///");
        ImGui.PopStyleColor();

        var statusText = _session.Mode switch
        {
            PrismMode.Hosting => "● LIVE",
            PrismMode.Viewing => "● CONNECTED",
            _ => "● IDLE"
        };
        var statusColor = _session.Mode == PrismMode.Idle ? Muted : Good;
        var statusWidth = ImGui.CalcTextSize(statusText).X;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 20, ImGui.GetWindowWidth() - statusWidth - 16));
        ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
        ImGui.TextUnformatted(statusText);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(1, 5));
    }

    private void DrawCurrentPage()
    {
        switch (_page)
        {
            case Page.RemoteControl:
                DrawRemoteControl();
                break;
            case Page.PlexLibrary:
                DrawLibraryHub();
                break;
            case Page.LocalFiles:
                _librarySource = LibrarySource.LocalFiles;
                DrawLibraryHub();
                break;
            case Page.JoinSession:
                DrawJoinSession();
                break;
            case Page.Settings:
                DrawSettings();
                break;
            case Page.Changelog:
                DrawChangelogPage();
                break;
        }

        if (!string.IsNullOrWhiteSpace(_uiStatus))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, _uiStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? Danger : Muted);
            ImGui.TextWrapped(_uiStatus);
            ImGui.PopStyleColor();
        }
    }

    private void DrawChangelogPage()
    {
        DrawSectionHeading("What's New", "// RECENT BUILDS");
        ImGui.TextWrapped("PrismCast updates, interface revisions, and synchronization changes.");
        ImGui.Spacing();

        for (var i = 0; i < RecentChanges.Length; i++)
        {
            var entry = RecentChanges[i];
            var selected = i == _selectedChangelogIndex;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, selected
                ? new Vector4(0.13f, 0.08f, 0.22f, 0.98f)
                : new Vector4(0.055f, 0.060f, 0.105f, 0.98f));
            if (ImGui.BeginChild($"##Changelog{i}", new Vector2(0, 138), true))
            {
                DrawTechFrame(selected ? AccentHover : S9Blue);
                ImGui.PushStyleColor(ImGuiCol.Text, selected ? AccentHover : S9Cyan);
                ImGui.TextUnformatted(entry.Version);
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.TextUnformatted(entry.Title);
                ImGui.Spacing();
                foreach (var change in entry.Changes)
                    ImGui.BulletText(change);
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private bool CircleTechButton(string id, string text, float diameter, string? caption = null, bool accent = false)
    {
        var extraHeight = string.IsNullOrWhiteSpace(caption) ? 0f : 16f;
        var totalSize = new Vector2(diameter, diameter + extraHeight);
        var pos = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##{id}", totalSize);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        var center = pos + new Vector2(diameter * 0.5f, diameter * 0.5f);
        var fill = accent ? new Vector4(0.18f, 0.09f, 0.30f, 0.98f) : new Vector4(0.045f, 0.065f, 0.115f, 0.98f);
        if (hovered) fill = accent ? new Vector4(0.25f, 0.12f, 0.42f, 1f) : new Vector4(0.07f, 0.10f, 0.18f, 1f);
        var radius = diameter * 0.5f;
        var glow = accent ? AccentHover : S9Cyan;
        draw.AddCircleFilled(center, radius, U32(fill), 36);
        draw.AddCircle(center, radius + 3f, U32(new Vector4(glow.X, glow.Y, glow.Z, hovered ? 0.22f : 0.10f)), 36, 4f);
        if (accent)
            draw.AddCircle(center, radius + 6f, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, hovered ? 0.16f : 0.07f)), 36, 5f);
        draw.AddCircle(center, radius - 1f, U32(new Vector4(0.25f, 0.30f, 0.48f, 0.95f)), 36, 1.2f);
        DrawArc(draw, center, radius - 1f, -2.45f, 1.95f, 26, glow, accent ? 3.6f : 2.4f);
        var edgeAngle = 1.95f;
        var edgeCenter = center + new Vector2(MathF.Cos(edgeAngle), MathF.Sin(edgeAngle)) * (radius - 1f);
        draw.AddCircleFilled(edgeCenter, accent ? 2.7f : 2.2f, U32(Vector4.One), 12);
        if (active)
            draw.AddCircle(center, radius - 5f, U32(new Vector4(1f, 1f, 1f, 0.34f)), 36, 1.2f);
        var textSize = ImGui.CalcTextSize(text);
        draw.AddText(center - textSize * 0.5f, U32(Vector4.One), text);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var captionSize = ImGui.CalcTextSize(caption);
            draw.AddText(new Vector2(pos.X + (diameter - captionSize.X) * 0.5f, pos.Y + diameter + 1f), U32(Muted), caption);
        }
        return pressed;
    }

    private bool TechActionButton(string id, string label, Vector2 size, bool active = false)
    {
        PushTechButton(active);
        var pressed = ImGui.Button($"{label}##{id}", size);
        PopTechButton();
        return pressed;
    }

    private bool RepeatTechActionButton(string id, string label, Vector2 size, bool active = false)
    {
        PushTechButton(active);
        var pressed = RepeatButton(label, id, size);
        PopTechButton();
        return pressed;
    }

    private bool TechPadButton(string id, string icon, string label, Vector2 size, bool repeat = false, bool accent = false)
    {
        var text = string.IsNullOrWhiteSpace(label) ? icon : $"{icon}\n{label}";
        return repeat
            ? RepeatTechActionButton(id, text, size, accent)
            : TechActionButton(id, text, size, accent);
    }

    private bool CircleMediaButton(string id, string iconPath, float diameter, bool accent = true)
    {
        var pos = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##{id}", new Vector2(diameter, diameter));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        var center = pos + new Vector2(diameter * 0.5f, diameter * 0.5f);
        var radius = diameter * 0.5f;

        var fill = accent ? new Vector4(0.20f, 0.09f, 0.34f, 0.98f) : new Vector4(0.06f, 0.08f, 0.15f, 0.98f);
        if (hovered)
            fill = accent ? new Vector4(0.27f, 0.12f, 0.46f, 1f) : new Vector4(0.09f, 0.12f, 0.22f, 1f);

        draw.AddCircleFilled(center, radius, U32(fill), 48);
        draw.AddCircle(center, radius - 2f, U32(new Vector4(0.26f, 0.30f, 0.55f, 0.72f)), 48, 2f);
        draw.AddCircle(center, radius - 5f, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, hovered || active ? 0.75f : 0.46f)), 48, 2f);
        DrawArc(draw, center, radius - 2f, -2.45f, 1.95f, 28, AccentHover, 3.4f);
        var edgeAngle = 1.95f;
        var edgeCenter = center + new Vector2(MathF.Cos(edgeAngle), MathF.Sin(edgeAngle)) * (radius - 2f);
        draw.AddCircleFilled(edgeCenter, 3.3f, U32(Vector4.One), 20);

        if (File.Exists(iconPath))
        {
            try
            {
                var shared = TextureProvider.GetFromFileAbsolute(iconPath);
                if (shared.TryGetWrap(out var wrap, out _) && wrap is not null)
                {
                    var iconSize = diameter * 0.34f;
                    var iconMin = center - new Vector2(iconSize * 0.5f);
                    var iconMax = center + new Vector2(iconSize * 0.5f);
                    draw.AddImage(wrap.Handle, iconMin, iconMax);
                }
            }
            catch
            {
            }
        }

        return pressed;
    }

    private void DrawRemoteControl()
    {
        DrawNowPlayingCard();
        ImGui.Spacing();

        var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
        var screenHeight = phone
            ? Math.Max(388f, ImGui.GetContentRegionAvail().Y)
            : Math.Max(292f, ImGui.GetContentRegionAvail().Y);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.060f, 0.105f, 0.98f));
        if (ImGui.BeginChild("##ScreenControlCard", new Vector2(0, screenHeight), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawTechFrame();
            DrawPhoneScreenControls();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawNowPlayingCard()
    {
        const float cardHeight = 246f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##NowPlayingCard", new Vector2(0, cardHeight), true))
        {
            DrawTechFrame();
            var info = _video.ReadInfo();
            var title = CurrentMediaTitle();
            if (string.IsNullOrWhiteSpace(title)) title = "Nothing playing";

            const float leftPad = 14f;
            const float topPad = 14f;
            const float posterW = 104f;
            const float posterH = 146f;
            ImGui.SetCursorPos(new Vector2(leftPad, topPad));
            if (_session.Mode != PrismMode.Idle && _nowPlayingPlexItem is { } item)
                DrawPlexPoster(item, new Vector2(posterW, posterH));
            else
            {
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(posterW, posterH);
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(min, max, U32(new Vector4(0.03f, 0.04f, 0.08f, 1f)), 10f);
                draw.AddRect(min, max, U32(new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.60f)), 10f);
                DrawPrismGlyph((min + max) * 0.5f, 21f);
                ImGui.Dummy(new Vector2(posterW, posterH));
            }

            var metaX = leftPad + posterW + 16f;
            var metaW = Math.Max(160f, ImGui.GetWindowWidth() - metaX - 14f);
            ImGui.SetCursorPos(new Vector2(metaX, topPad + 4f));
            if (ImGui.BeginChild("##ResponsiveNowPlayingMeta", new Vector2(metaW, posterH - 4f), false))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, AccentHover);
                ImGui.TextUnformatted("NOW PLAYING");
                ImGui.PopStyleColor();
                ImGui.Spacing();
                ImGui.SetWindowFontScale(1.14f);
                ImGui.TextWrapped(title);
                ImGui.SetWindowFontScale(1f);
                ImGui.TextDisabled(_session.Mode == PrismMode.Hosting
                    ? "Host session"
                    : _session.Mode == PrismMode.Viewing ? "Connected to host" : "Choose media from Library");
                ImGui.Spacing();
                DrawPhoneTimeline(info);
            }
            ImGui.EndChild();

            const float small = 58f;
            const float main = 72f;
            const float gap = 24f;
            var total = small * 2f + main + gap * 2f;
            var x = Math.Max(12f, (ImGui.GetWindowWidth() - total) * 0.5f);
            var y = cardHeight - main - 12f;
            var transportEnabled = _session.Mode == PrismMode.Hosting;
            ImGui.BeginDisabled(!transportEnabled);
            ImGui.SetCursorPos(new Vector2(x, y + (main - small) * 0.5f));
            if (CircleTechButton("SeekBackResponsive", "-10", small) && transportEnabled)
                _session.SeekHost(Math.Max(0, info.PositionSeconds - 10));
            ImGui.SetCursorPos(new Vector2(x + small + gap, y));
            var mediaIcon = info.Paused || _session.Mode == PrismMode.Idle ? _playIconPath : _pauseIconPath;
            if (CircleMediaButton("PlayPauseResponsive", mediaIcon, main, true) && transportEnabled)
                _session.PauseHost(!info.Paused);
            ImGui.SetCursorPos(new Vector2(x + small + gap + main + gap, y + (main - small) * 0.5f));
            if (CircleTechButton("SeekForwardResponsive", "+10", small) && transportEnabled)
                _session.SeekHost(info.PositionSeconds + 10);
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneTimeline(MpvPlaybackInfo info)
    {
        var duration = Math.Max(0, info.DurationSeconds);
        var position = Math.Clamp(info.PositionSeconds, 0, duration > 0 ? duration : Math.Max(1, info.PositionSeconds));
        var fraction = duration > 0 ? (float)(position / duration) : 0f;
        var width = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        var pos = ImGui.GetCursorScreenPos();
        const float height = 6f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + new Vector2(width, height), U32(new Vector4(0.10f, 0.12f, 0.22f, 1f)), 4f);
        var fill = width * fraction;
        if (fill > 0f)
        {
            draw.AddRectFilled(pos, pos + new Vector2(fill, height), U32(Accent), 4f);
            draw.AddLine(new Vector2(pos.X + fill, pos.Y - 1f), new Vector2(pos.X + fill, pos.Y + height + 1f), U32(Vector4.One), 2f);
        }
        ImGui.Dummy(new Vector2(width, height + 3f));
        ImGui.TextDisabled(FormatTime(position));
        var endText = FormatTime(duration);
        var endW = ImGui.CalcTextSize(endText).X;
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 12f, ImGui.GetWindowWidth() - endW - 12f));
        ImGui.TextDisabled(endText);
    }

    private void DrawTimeline(MpvPlaybackInfo info)
    {
        var duration = Math.Max(0, info.DurationSeconds);
        var position = Math.Clamp(info.PositionSeconds, 0, duration > 0 ? duration : Math.Max(1, info.PositionSeconds));
        var fraction = duration > 0 ? (float)(position / duration) : 0f;
        ImGui.ProgressBar(fraction, new Vector2(-1, 9), "");
        ImGui.TextDisabled($"{FormatTime(position)} / {FormatTime(duration)}");
    }

    private void DrawPhoneScreenControls()
    {
        if (EffectiveInterfaceMode() == InterfaceMode.Tablet)
        {
            DrawTabletScreenControls();
            return;
        }

        ImGui.SetCursorPos(new Vector2(14f, 12f));
        var headingPos = ImGui.GetCursorScreenPos();
        DrawUiIcon(UiIcon.Monitor, headingPos + new Vector2(10f, 9f), 18f, new Vector4(0.58f, 0.75f, 1f, 1f), 1.8f);
        ImGui.SetCursorPosX(40f);
        DrawSectionHeading("Screen Controls", "///");

        if (_session.Mode == PrismMode.Viewing)
        {
            SyncViewerScreenEditors();
            ImGui.SetCursorPosX(14f);
            ImGui.TextDisabled("HOST CONTROLLED");
            ImGui.TextWrapped("The host owns shared screen placement. Your local playback volume is independent.");
            return;
        }

        var enabled = _session.Mode == PrismMode.Hosting;
        var moveStep = Math.Max(0.001f, ImGui.GetIO().KeyShift ? _config.ScreenFineMovementStep : _config.ScreenMovementStep);
        var rotationStep = Math.Max(0.1f, _config.ScreenRotationStepDegrees);
        var scaleStep = Math.Max(0.005f, _config.ScreenScaleStep);
        var childW = ImGui.GetWindowWidth();
        const float gap = 8f;
        ImGui.BeginDisabled(!enabled);

        // Quick screen sizing and projection controls stay visible at the top instead of hiding in Advanced.
        const float quickY = 40f;
        ImGui.SetCursorPos(new Vector2(14f, quickY));
        if (RepeatTechActionButton("PhoneScaleDown", "Scale -", new Vector2(68, 28))) { _screenScaleEdit -= scaleStep; ApplyScreenEdits(); }
        ImGui.SetCursorPos(new Vector2(88f, quickY));
        if (RepeatTechActionButton("PhoneScaleUp", "Scale +", new Vector2(68, 28))) { _screenScaleEdit += scaleStep; ApplyScreenEdits(); }

        const float modeW = 58f;
        var modeX = childW - (modeW * 2f + 8f) - 14f;
        ImGui.SetCursorPos(new Vector2(modeX, quickY));
        if (SegmentButton("Flat", !_screenCurvedEdit)) { _screenCurvedEdit = false; ApplyScreenEdits(); }
        ImGui.SetCursorPos(new Vector2(modeX + modeW + 8f, quickY));
        if (SegmentButton("Curved", _screenCurvedEdit)) { _screenCurvedEdit = true; ApplyScreenEdits(); }

        const float padW = 82f;
        const float padH = 58f;
        var dpadTotal = padW * 3f + gap * 2f;
        var dpadX = Math.Max(12f, (childW - dpadTotal) * 0.5f);
        var centerX = dpadX + padW + gap;
        var baseY = 80f;

        ImGui.SetCursorPos(new Vector2(centerX, baseY));
        if (IconTileButton("PhoneUp", UiIcon.ArrowUp, "Up", new Vector2(padW, padH), true)) NudgeScreen(0, moveStep, 0);
        ImGui.SetCursorPos(new Vector2(dpadX, baseY + padH + gap));
        if (IconTileButton("PhoneLeft", UiIcon.ArrowLeft, "Left", new Vector2(padW, padH), true)) NudgeLocalHorizontal(-moveStep, 0);
        ImGui.SetCursorPos(new Vector2(centerX, baseY + padH + gap));
        if (IconTileButton("PhoneCenter", UiIcon.Monitor, "Center", new Vector2(padW, padH), false, true)) PlaceInFrontOfPlayer();
        ImGui.SetCursorPos(new Vector2(dpadX + (padW + gap) * 2f, baseY + padH + gap));
        if (IconTileButton("PhoneRight", UiIcon.ArrowRight, "Right", new Vector2(padW, padH), true)) NudgeLocalHorizontal(moveStep, 0);
        ImGui.SetCursorPos(new Vector2(centerX, baseY + (padH + gap) * 2f));
        if (IconTileButton("PhoneDown", UiIcon.ArrowDown, "Down", new Vector2(padW, padH), true)) NudgeScreen(0, -moveStep, 0);

        const float util = 78f;
        var utilTotal = util * 4f + gap * 3f;
        var utilX = Math.Max(12f, (childW - utilTotal) * 0.5f);
        var utilY = baseY + (padH + gap) * 3f + 2f;
        ImGui.SetCursorPos(new Vector2(utilX, utilY));
        if (IconTileButton("PhoneForward", UiIcon.Forward, "Forward", new Vector2(util, 68f), true)) NudgeLocalHorizontal(0, moveStep);
        ImGui.SetCursorPos(new Vector2(utilX + util + gap, utilY));
        if (IconTileButton("PhoneBack", UiIcon.Back, "Back", new Vector2(util, 68f), true)) NudgeLocalHorizontal(0, -moveStep);
        ImGui.SetCursorPos(new Vector2(utilX + (util + gap) * 2f, utilY));
        if (IconTileButton("PhoneRotateL", UiIcon.RotateLeft, "Rotate Left", new Vector2(util, 68f), true)) { _screenYawDegreesEdit -= rotationStep; ApplyScreenEdits(); }
        ImGui.SetCursorPos(new Vector2(utilX + (util + gap) * 3f, utilY));
        if (IconTileButton("PhoneRotateR", UiIcon.RotateRight, "Rotate Right", new Vector2(util, 68f), true)) { _screenYawDegreesEdit += rotationStep; ApplyScreenEdits(); }

        var actionY = utilY + 76f;
        const float actionW = 168f;
        const float actionH = 42f;
        var actionGap = 12f;
        var actionTotal = actionW * 2f + actionGap;
        var actionX = Math.Max(12f, (childW - actionTotal) * 0.5f);
        ImGui.SetCursorPos(new Vector2(actionX, actionY));
        if (HorizontalIconButton("PhonePlaceFront", UiIcon.PlaceFront, "Place in Front", new Vector2(actionW, actionH), true)) PlaceInFrontOfPlayer();
        ImGui.SetCursorPos(new Vector2(actionX + actionW + actionGap, actionY));
        if (HorizontalIconButton("PhoneResetRot", UiIcon.Reset, "Reset Rotation", new Vector2(actionW, actionH), false)) ResetScreenRotation();

        ImGui.SetCursorPos(new Vector2(14f, actionY + actionH + 14f));
        ImGui.TextDisabled("Control the in-game screen in Final Fantasy XIV");

        ImGui.EndDisabled();
        if (!enabled)
        {
            ImGui.SetCursorPos(new Vector2(14f, actionY + actionH + 38f));
            ImGui.TextDisabled("Start media from Library to activate shared controls.");
        }
    }

    private void DrawTabletScreenControls()
    {
        ImGui.SetCursorPos(new Vector2(14f, 12f));
        var headingPos = ImGui.GetCursorScreenPos();
        DrawUiIcon(UiIcon.Monitor, headingPos + new Vector2(10f, 9f), 18f,
            new Vector4(0.58f, 0.75f, 1f, 1f), 1.8f);
        ImGui.SetCursorPosX(40f);
        DrawSectionHeading("Screen Controls", "///");

        if (_session.Mode == PrismMode.Viewing)
        {
            SyncViewerScreenEditors();
            ImGui.SetCursorPosX(14f);
            ImGui.TextDisabled("HOST CONTROLLED");
            ImGui.TextWrapped("The host owns shared screen placement. Your local playback volume is independent.");
            return;
        }

        var enabled = _session.Mode == PrismMode.Hosting;
        var moveStep = Math.Max(0.001f,
            ImGui.GetIO().KeyShift ? _config.ScreenFineMovementStep : _config.ScreenMovementStep);
        var rotationStep = Math.Max(0.1f, _config.ScreenRotationStepDegrees);
        var scaleStep = Math.Max(0.005f, _config.ScreenScaleStep);
        var childWidth = ImGui.GetWindowWidth();
        const float gap = 8f;

        ImGui.BeginDisabled(!enabled);
        ImGui.SetCursorPos(new Vector2(14f, 40f));
        if (RepeatTechActionButton("TabletScaleDown", "Scale -", new Vector2(78f, 28f)))
        {
            _screenScaleEdit -= scaleStep;
            ApplyScreenEdits();
        }
        ImGui.SameLine(0, gap);
        if (RepeatTechActionButton("TabletScaleUp", "Scale +", new Vector2(78f, 28f)))
        {
            _screenScaleEdit += scaleStep;
            ApplyScreenEdits();
        }

        const float modeWidth = 66f;
        var modeX = childWidth - modeWidth * 2f - gap - 14f;
        ImGui.SetCursorPos(new Vector2(modeX, 40f));
        ImGui.SetNextItemWidth(modeWidth);
        if (SegmentButton("Flat", !_screenCurvedEdit))
        {
            _screenCurvedEdit = false;
            ApplyScreenEdits();
        }
        ImGui.SameLine(0, gap);
        ImGui.SetNextItemWidth(modeWidth);
        if (SegmentButton("Curved", _screenCurvedEdit))
        {
            _screenCurvedEdit = true;
            ApplyScreenEdits();
        }

        const float padWidth = 68f;
        const float padHeight = 48f;
        const float padX = 18f;
        const float padY = 76f;
        var padColumn = padWidth + gap;
        var padRow = padHeight + gap;

        ImGui.SetCursorPos(new Vector2(padX + padColumn, padY));
        if (IconTileButton("TabletUp", UiIcon.ArrowUp, "Up", new Vector2(padWidth, padHeight), true))
            NudgeScreen(0, moveStep, 0);
        ImGui.SetCursorPos(new Vector2(padX, padY + padRow));
        if (IconTileButton("TabletLeft", UiIcon.ArrowLeft, "Left", new Vector2(padWidth, padHeight), true))
            NudgeLocalHorizontal(-moveStep, 0);
        ImGui.SetCursorPos(new Vector2(padX + padColumn, padY + padRow));
        if (IconTileButton("TabletCenter", UiIcon.Monitor, "Center", new Vector2(padWidth, padHeight), false, true))
            PlaceInFrontOfPlayer();
        ImGui.SetCursorPos(new Vector2(padX + padColumn * 2f, padY + padRow));
        if (IconTileButton("TabletRight", UiIcon.ArrowRight, "Right", new Vector2(padWidth, padHeight), true))
            NudgeLocalHorizontal(moveStep, 0);
        ImGui.SetCursorPos(new Vector2(padX + padColumn, padY + padRow * 2f));
        if (IconTileButton("TabletDown", UiIcon.ArrowDown, "Down", new Vector2(padWidth, padHeight), true))
            NudgeScreen(0, -moveStep, 0);

        var utilityX = padX + padColumn * 3f + 26f;
        var utilityArea = Math.Max(280f, childWidth - utilityX - 16f);
        var utilityWidth = Math.Clamp((utilityArea - gap * 3f) / 4f, 64f, 112f);
        var utilities = new (string Id, UiIcon Icon, string Label, Action Action)[]
        {
            ("TabletForward", UiIcon.Forward, "Forward", () => NudgeLocalHorizontal(0, moveStep)),
            ("TabletBack", UiIcon.Back, "Back", () => NudgeLocalHorizontal(0, -moveStep)),
            ("TabletRotateL", UiIcon.RotateLeft, "Rotate Left", () => { _screenYawDegreesEdit -= rotationStep; ApplyScreenEdits(); }),
            ("TabletRotateR", UiIcon.RotateRight, "Rotate Right", () => { _screenYawDegreesEdit += rotationStep; ApplyScreenEdits(); }),
        };

        for (var i = 0; i < utilities.Length; i++)
        {
            ImGui.SetCursorPos(new Vector2(utilityX + i * (utilityWidth + gap), 92f));
            var utility = utilities[i];
            if (IconTileButton(utility.Id, utility.Icon, utility.Label, new Vector2(utilityWidth, 68f), true))
                utility.Action();
        }

        var actionGap = 12f;
        var actionWidth = Math.Max(120f, (utilityArea - actionGap) * 0.5f);
        ImGui.SetCursorPos(new Vector2(utilityX, 176f));
        if (HorizontalIconButton("TabletPlaceFront", UiIcon.PlaceFront, "Place in Front",
                new Vector2(actionWidth, 42f), true))
            PlaceInFrontOfPlayer();
        ImGui.SetCursorPos(new Vector2(utilityX + actionWidth + actionGap, 176f));
        if (HorizontalIconButton("TabletResetRotation", UiIcon.Reset, "Reset Rotation",
                new Vector2(actionWidth, 42f), false))
            ResetScreenRotation();

        ImGui.EndDisabled();
        ImGui.SetCursorPos(new Vector2(utilityX, 232f));
        ImGui.TextDisabled(enabled
            ? "Shift enables fine movement. Screen changes are shared with viewers."
            : "Start media from Library to activate shared controls.");
    }

    private void DrawScreenControlCard()
    {
        DrawSectionHeading("Screen Controls", "///");
        if (_session.Mode == PrismMode.Viewing)
        {
            SyncViewerScreenEditors();
            ImGui.TextWrapped("Host controlled");
            ImGui.TextDisabled($"X {_screenPositionEdit.X:F2}  Y {_screenPositionEdit.Y:F2}  Z {_screenPositionEdit.Z:F2}");
            ImGui.TextDisabled($"Scale {_screenScaleEdit:F2}  •  {(_screenCurvedEdit ? "Curved" : "Flat")}");
            return;
        }

        var fine = ImGui.GetIO().KeyShift;
        var moveStep = Math.Max(0.001f, fine ? _config.ScreenFineMovementStep : _config.ScreenMovementStep);
        var rotationStep = Math.Max(0.1f, _config.ScreenRotationStepDegrees);
        var scaleStep = Math.Max(0.005f, _config.ScreenScaleStep);
        var enabled = _session.Mode == PrismMode.Hosting;

        if (!enabled)
            ImGui.TextDisabled("Start media to activate shared controls.");
        else
            ImGui.TextDisabled(fine ? $"Fine {moveStep:F3}" : $"Step {moveStep:F3} • Shift = fine");

        ImGui.BeginDisabled(!enabled);

        // Compact fixed-position pad. The entire primary control surface stays in one
        // predictable place instead of turning into a cockpit every time we add a feature.
        var origin = ImGui.GetCursorPos();
        var dpad = new Vector2(54, 30);
        const float gap = 5f;
        var col = dpad.X + gap;
        var row = dpad.Y + gap;

        ImGui.SetCursorPos(new Vector2(origin.X + col, origin.Y));
        if (RepeatButton("↑", "RcUp", dpad)) NudgeScreen(0, moveStep, 0);
        ImGui.SetCursorPos(new Vector2(origin.X, origin.Y + row));
        if (RepeatButton("←", "RcLeft", dpad)) NudgeLocalHorizontal(-moveStep, 0);
        ImGui.SameLine(0, gap);
        if (ImGui.Button("◎##rc", dpad)) PlaceInFrontOfPlayer();
        ImGui.SameLine(0, gap);
        if (RepeatButton("→", "RcRight", dpad)) NudgeLocalHorizontal(moveStep, 0);
        ImGui.SetCursorPos(new Vector2(origin.X + col, origin.Y + row * 2));
        if (RepeatButton("↓", "RcDown", dpad)) NudgeScreen(0, -moveStep, 0);

        var quickX = origin.X + col * 3f + 24f;
        ImGui.SetCursorPos(new Vector2(quickX, origin.Y));
        if (RepeatButton("FWD", "RcForward", new Vector2(62, 30))) NudgeLocalHorizontal(0, moveStep);
        ImGui.SameLine();
        if (RepeatButton("BACK", "RcBack", new Vector2(62, 30))) NudgeLocalHorizontal(0, -moveStep);
        ImGui.SetCursorPos(new Vector2(quickX, origin.Y + row));
        if (RepeatButton("↺", "RcYawLeft", new Vector2(62, 30))) { _screenYawDegreesEdit -= rotationStep; ApplyScreenEdits(); }
        ImGui.SameLine();
        if (RepeatButton("↻", "RcYawRight", new Vector2(62, 30))) { _screenYawDegreesEdit += rotationStep; ApplyScreenEdits(); }

        ImGui.SetCursorPos(new Vector2(origin.X, origin.Y + row * 3f + 8f));
        ImGui.TextDisabled("SIZE");
        ImGui.SameLine();
        if (RepeatButton("−", "RcScaleDown", new Vector2(30, 26))) { _screenScaleEdit -= scaleStep; ApplyScreenEdits(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(92);
        if (ImGui.DragFloat("##ScreenScaleRemote", ref _screenScaleEdit, 0.01f, 0.1f, 8f, "%.2f"))
            ApplyScreenEdits();
        ImGui.SameLine();
        if (RepeatButton("+", "RcScaleUp", new Vector2(30, 26))) { _screenScaleEdit += scaleStep; ApplyScreenEdits(); }
        ImGui.SameLine(0, 14f);
        if (SegmentButton("Flat", !_screenCurvedEdit)) { _screenCurvedEdit = false; ApplyScreenEdits(); }
        ImGui.SameLine();
        if (SegmentButton("Curved", _screenCurvedEdit)) { _screenCurvedEdit = true; ApplyScreenEdits(); }

        ImGui.Spacing();
        if (ImGui.Button("Place in Front of Me", new Vector2(154, 28)))
            PlaceInFrontOfPlayer();
        ImGui.SameLine();
        if (ImGui.Button("Reset Rotation", new Vector2(122, 28)))
            ResetScreenRotation();

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Advanced screen controls"))
        {
            if (RepeatButton("Tilt +", "RcTiltPlus", new Vector2(70, 28))) { _screenPitchDegreesEdit += rotationStep; ApplyScreenEdits(); }
            ImGui.SameLine();
            if (RepeatButton("Tilt −", "RcTiltMinus", new Vector2(70, 28))) { _screenPitchDegreesEdit -= rotationStep; ApplyScreenEdits(); }
            ImGui.SameLine();
            if (RepeatButton("Roll −", "RcRollMinus", new Vector2(70, 28))) { _screenRollDegreesEdit -= rotationStep; ApplyScreenEdits(); }
            ImGui.SameLine();
            if (RepeatButton("Roll +", "RcRollPlus", new Vector2(70, 28))) { _screenRollDegreesEdit += rotationStep; ApplyScreenEdits(); }
            ImGui.TextDisabled($"X {_screenPositionEdit.X:F3}  Y {_screenPositionEdit.Y:F3}  Z {_screenPositionEdit.Z:F3}");
            ImGui.TextDisabled($"Yaw {_screenYawDegreesEdit:F1}°  Pitch {_screenPitchDegreesEdit:F1}°  Roll {_screenRollDegreesEdit:F1}°");
        }

        ImGui.EndDisabled();
    }

    private void DrawSessionCard()
    {
        DrawSectionHeading("Session", "// LINK");

        if (_session.Mode == PrismMode.Hosting)
        {
            ImGui.TextUnformatted("Session live");
            ImGui.TextDisabled("Share this code with viewers");
            ImGui.Spacing();

            var code = _session.InviteCode;
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##SessionCodeReadOnly", ref code, 4096, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("Copy Code", new Vector2(-1, 32)))
                ImGui.SetClipboardText(_session.InviteCode);

            ImGui.Spacing();
            ImGui.TextDisabled("Host ID");
            ImGui.TextWrapped(_session.HostId);
            if (ImGui.Button("Copy Host ID", new Vector2(-1, 30)))
                ImGui.SetClipboardText(_session.HostId);

            ImGui.Spacing();
            var viewers = ViewerPresenceRegistry.GetViewerNames();
            ImGui.TextDisabled($"VIEWERS  {viewers.Count}");
            if (viewers.Count == 0)
            {
                ImGui.TextDisabled("No viewers have joined with this code yet.");
            }
            else
            {
                foreach (var firstName in viewers.Take(6))
                    ImGui.BulletText(firstName);
                if (viewers.Count > 6)
                    ImGui.TextDisabled($"+ {viewers.Count - 6} more");
            }

            var bottom = ImGui.GetWindowHeight() - 52f;
            if (ImGui.GetCursorPosY() < bottom)
                ImGui.SetCursorPosY(bottom);
            PushDangerButton();
            if (ImGui.Button("End Session", new Vector2(-1, 34)))
                RunUiTask(StopSessionFromUiAsync);
            PopDangerButton();
        }
        else if (_session.Mode == PrismMode.Viewing)
        {
            var state = _session.ViewerState;
            ImGui.TextUnformatted("Connected to host");
            ImGui.TextDisabled(state?.Title ?? "Remote PrismCast session");
            ImGui.Spacing();
            ImGui.TextWrapped("Playback and shared screen placement are host controlled. Your local volume remains independent.");

            var volume = _config.Volume;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("Local volume", ref volume, 0, 100))
            {
                _config.Volume = volume;
                _video.SetVolume(volume);
                SaveConfig();
            }

            var bottom = ImGui.GetWindowHeight() - 52f;
            if (ImGui.GetCursorPosY() < bottom)
                ImGui.SetCursorPosY(bottom);
            if (ImGui.Button("Leave Session", new Vector2(-1, 34)))
                RunUiTask(StopSessionFromUiAsync);
        }
        else
        {
            ImGui.TextUnformatted("No active session");
            ImGui.TextWrapped("Choose media from Library to host, or open Session to connect to someone else.");
            ImGui.Spacing();
            if (ImGui.Button("Join a Session", new Vector2(-1, 34)))
                SelectPage(Page.JoinSession);
        }
    }

    private void DrawQuickUrlCard()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        if (ImGui.BeginChild("##QuickUrlCard", new Vector2(0, 104), true))
        {
            ImGui.TextDisabled("QUICK URL / YOUTUBE");
            ImGui.SetNextItemWidth(-104);
            ImGui.InputText("##QuickUrl", ref _url, 2048);
            ImGui.SameLine();
            if (ImGui.Button("Play URL", new Vector2(94, 28)))
                RunUiTask(() => HostUrlFromUiAsync(_url));
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawLibraryHub()
    {
        DrawPhoneLibrary();
    }

    private void DrawPhoneLibrary()
    {
        if (_librarySource == LibrarySource.Plex)
            EnsurePlexLoaded();
        else if (_librarySource == LibrarySource.LocalFiles)
            EnsureLocalLibraryLoaded();

        List<PlexLibrary> libraries;
        List<PlexItem> items;
        bool canBack;
        int selectedIndex;
        lock (_plexLock)
        {
            libraries = [.. _libraries];
            items = [.. _plexItems];
            canBack = _plexHistory.Count > 0;
            selectedIndex = _libraryIndex;
        }

        if (_librarySource != LibrarySource.Web)
        {
            DrawPhoneLibrarySearchBar(_librarySource == LibrarySource.Plex);
            ImGui.Spacing();
        }

        DrawPhoneLibrarySelector(libraries, selectedIndex);
        ImGui.Spacing();

        if (!string.IsNullOrWhiteSpace(_uiStatus))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _uiStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? Danger : Muted);
            ImGui.TextWrapped(_uiStatus);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (_librarySource == LibrarySource.Web)
        {
            DrawWebMediaLibrary(phone: EffectiveInterfaceMode() == InterfaceMode.Phone);
            return;
        }

        if (_librarySource == LibrarySource.LocalFiles)
        {
            DrawPhoneLocalFilesContent();
            return;
        }

        if (!PlexConnected())
        {
            DrawEmptyState("Plex isn't connected", "Connect your Plex account once and PrismCast will discover your media server and libraries.", "Connect Plex", () => RunUiTask(ConnectPlexAsync));
            return;
        }

        if (_selectedPlexDetailsItem is not null)
        {
            DrawPlexDetailsView(_selectedPlexDetailsItem,
                phone: EffectiveInterfaceMode() == InterfaceMode.Phone);
            return;
        }

        var filtered = items
            .Where(x => string.IsNullOrWhiteSpace(_plexSearch) || PlexItemLabel(x).Contains(_plexSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scrollHeight = Math.Max(120f, ImGui.GetContentRegionAvail().Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 9f);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.48f, 0.18f, 0.82f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.64f, 0.30f, 1.00f, 1f));
        if (ImGui.BeginChild("##PhonePlexMediaScroll", new Vector2(0, scrollHeight), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (canBack)
            {
                PushTechButton();
                if (ImGui.Button("<  BACK", new Vector2(92f, 30f)))
                    PlexGoBack();
                PopTechButton();
                ImGui.Spacing();
            }

            if (filtered.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("No media found here.");
            }
            else
            {
                DrawPhonePlexTileGrid(filtered);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    private void DrawPhoneLibrarySearchBar(bool plex)
    {
        const float height = 46f;
        var start = ImGui.GetCursorPos();
        var screen = ImGui.GetCursorScreenPos();
        var width = Math.Max(220f, ImGui.GetContentRegionAvail().X);
        var draw = ImGui.GetWindowDrawList();
        var max = screen + new Vector2(width, height);

        draw.AddRectFilled(screen, max, U32(new Vector4(0.060f, 0.067f, 0.125f, 0.99f)), 10f);
        draw.AddRect(screen - new Vector2(1f, 1f), max + new Vector2(1f, 1f), U32(new Vector4(0.55f, 0.28f, 0.95f, 0.22f)), 11f, ImDrawFlags.None, 4f);
        draw.AddRect(screen, max, U32(new Vector4(0.52f, 0.38f, 0.96f, 0.92f)), 10f, ImDrawFlags.None, 1.4f);
        DrawUiIcon(UiIcon.Search, screen + new Vector2(22f, height * 0.5f), 20f, new Vector4(0.80f, 0.76f, 1f, 1f), 2f);

        ImGui.SetCursorPos(new Vector2(start.X + 42f, start.Y + 8f));
        ImGui.SetNextItemWidth(width - 54f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        if (plex)
            ImGui.InputText("##PhonePlexSearchVisible", ref _plexSearch, 256);
        else
            ImGui.InputText("##PhoneLocalSearchVisible", ref _localSearch, 256);
        var active = ImGui.IsItemActive();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var empty = plex ? string.IsNullOrWhiteSpace(_plexSearch) : string.IsNullOrWhiteSpace(_localSearch);
        if (empty && !active)
        {
            var hint = plex ? "Search movies, shows, anime..." : "Search local files...";
            draw.AddText(screen + new Vector2(43f, 15f), U32(new Vector4(0.64f, 0.63f, 0.78f, 1f)), hint);
        }

        ImGui.SetCursorPos(new Vector2(start.X, start.Y + height));
    }

    private void DrawPhoneLibrarySelector(List<PlexLibrary> libraries, int selectedIndex)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
        const float refreshSize = 42f;
        const float gap = 8f;
        var selectorWidth = Math.Max(220f, available - refreshSize - gap);
        if (!phone)
            selectorWidth = Math.Min(540f, selectorWidth);
        const float selectorHeight = 46f;

        PlexLibrary? selectedLibrary = null;
        if (_librarySource == LibrarySource.Plex && selectedIndex >= 0 && selectedIndex < libraries.Count)
            selectedLibrary = libraries[selectedIndex];

        var title = _librarySource switch
        {
            LibrarySource.LocalFiles => "Local Files",
            LibrarySource.Web => "Web / YouTube",
            _ => selectedLibrary?.Title ?? (libraries.Count > 0 ? libraries[0].Title : "Plex Library"),
        };
        var icon = _librarySource switch
        {
            LibrarySource.LocalFiles => UiIcon.Folder,
            LibrarySource.Web => UiIcon.Link,
            _ => selectedLibrary is not null ? PlexLibraryIcon(selectedLibrary) : UiIcon.Library,
        };

        if (PhoneLibrarySelectorButton("PhoneLibrarySelector", icon, title, new Vector2(selectorWidth, selectorHeight)))
            ImGui.OpenPopup("##PhoneLibrarySelectorPopup");

        ImGui.SameLine(0, gap);
        var utilityTooltip = _librarySource == LibrarySource.Web ? "Clear web media" : "Refresh library";
        if (PhoneLibraryToolbarButton("PhoneLibraryRefresh", UiIcon.Reset, utilityTooltip, refreshSize))
        {
            if (_librarySource == LibrarySource.Plex)
                RunUiTask(LoadPlexLibrariesAsync);
            else if (_librarySource == LibrarySource.LocalFiles)
                RefreshLocalLibrary();
            else
                ClearWebMediaPreview();
        }

        if (ImGui.BeginPopup("##PhoneLibrarySelectorPopup"))
        {
            var menuWidth = Math.Max(260f, selectorWidth - 12f);
            var totalRows = libraries.Count + 2;
            var visibleRows = Math.Min(7, totalRows);
            var menuHeight = Math.Max(54f, visibleRows * 44f + 24f);
            if (ImGui.BeginChild("##PhoneLibrarySelectorScroll", new Vector2(menuWidth, menuHeight), false,
                    totalRows > 7 ? ImGuiWindowFlags.AlwaysVerticalScrollbar : ImGuiWindowFlags.NoScrollbar))
            {
                for (var i = 0; i < libraries.Count; i++)
                {
                    var library = libraries[i];
                    var selected = _librarySource == LibrarySource.Plex && i == selectedIndex;
                    if (PhoneLibraryDropdownItem($"PhoneLibraryChoice{library.Key}", PlexLibraryIcon(library), library.Title, selected))
                    {
                        var index = i;
                        _librarySource = LibrarySource.Plex;
                        lock (_plexLock) _libraryIndex = index;
                        RunUiTask(() => LoadPlexLibraryAsync(library));
                        ImGui.CloseCurrentPopup();
                    }
                }

                if (libraries.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                }

                if (PhoneLibraryDropdownItem("PhoneLibraryLocalFiles", UiIcon.Folder, "Local Files", _librarySource == LibrarySource.LocalFiles))
                {
                    _librarySource = LibrarySource.LocalFiles;
                    EnsureLocalLibraryLoaded();
                    ImGui.CloseCurrentPopup();
                }

                if (PhoneLibraryDropdownItem("PhoneLibraryWeb", UiIcon.Link, "Web / YouTube", _librarySource == LibrarySource.Web))
                {
                    _librarySource = LibrarySource.Web;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private bool PhoneLibrarySelectorButton(string id, UiIcon icon, string label, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        draw.AddRectFilled(pos, max, U32(hovered ? new Vector4(0.13f, 0.09f, 0.24f, 1f) : new Vector4(0.060f, 0.067f, 0.120f, 0.99f)), 9f);
        draw.AddRect(pos, max, U32(hovered ? AccentHover : new Vector4(0.55f, 0.34f, 0.96f, 0.82f)), 9f, ImDrawFlags.None, 1.5f);
        DrawUiIcon(icon, pos + new Vector2(23f, size.Y * 0.5f), 20f, new Vector4(0.80f, 0.74f, 1f, 1f), 1.8f);

        var shown = TrimForDisplay(label, 36);
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(new Vector2(pos.X + 43f, pos.Y + (size.Y - textSize.Y) * 0.5f), U32(Vector4.One), shown);

        var chevronX = max.X - 19f;
        var cy = pos.Y + size.Y * 0.5f;
        draw.AddLine(new Vector2(chevronX - 5f, cy - 2f), new Vector2(chevronX, cy + 3f), U32(new Vector4(0.82f, 0.76f, 1f, 1f)), 2f);
        draw.AddLine(new Vector2(chevronX, cy + 3f), new Vector2(chevronX + 5f, cy - 2f), U32(new Vector4(0.82f, 0.76f, 1f, 1f)), 2f);
        return clicked;
    }

    private bool PhoneLibraryDropdownItem(string id, UiIcon icon, string label, bool selected)
    {
        var width = Math.Max(230f, ImGui.GetContentRegionAvail().X);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(width, 40f));
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(width, 40f);
        if (selected || hovered)
            draw.AddRectFilled(pos, max, U32(selected ? new Vector4(0.25f, 0.10f, 0.46f, 0.98f) : new Vector4(0.10f, 0.09f, 0.18f, 0.96f)), 7f);
        if (selected)
            draw.AddRect(pos, max, U32(AccentHover), 7f, ImDrawFlags.None, 1.5f);
        DrawUiIcon(icon, pos + new Vector2(21f, 20f), 18f, selected ? AccentHover : new Vector4(0.72f, 0.68f, 0.92f, 1f), 1.7f);
        var shown = TrimForDisplay(label, 38);
        var textSize = ImGui.CalcTextSize(shown);
        draw.AddText(new Vector2(pos.X + 42f, pos.Y + (40f - textSize.Y) * 0.5f), U32(Vector4.One), shown);
        if (selected)
        {
            var checkX = max.X - 20f;
            draw.AddLine(new Vector2(checkX - 5f, pos.Y + 21f), new Vector2(checkX - 1f, pos.Y + 25f), U32(Vector4.One), 2f);
            draw.AddLine(new Vector2(checkX - 1f, pos.Y + 25f), new Vector2(checkX + 6f, pos.Y + 16f), U32(Vector4.One), 2f);
        }
        return clicked;
    }

    private static UiIcon PlexLibraryIcon(PlexLibrary library)
    {
        if (library.Title.Contains("anime", StringComparison.OrdinalIgnoreCase) &&
            library.Type.Equals("movie", StringComparison.OrdinalIgnoreCase))
            return UiIcon.FilmReel;
        if (library.Title.Contains("anime", StringComparison.OrdinalIgnoreCase))
            return UiIcon.AnimeSpark;
        if (library.Type.Equals("show", StringComparison.OrdinalIgnoreCase))
            return UiIcon.Television;
        if (library.Type.Equals("movie", StringComparison.OrdinalIgnoreCase))
            return UiIcon.Movie;
        return UiIcon.Library;
    }

    private void DrawWebMediaLibrary(bool phone)
    {
        if (phone)
        {
            DrawSectionHeading("Web / YouTube", "///");
            ImGui.TextDisabled("Paste a YouTube link or direct HTTP/HTTPS media URL.");
            ImGui.Spacing();
        }
        else
        {
            DrawSectionHeading("Web / YouTube", "///");
            ImGui.TextWrapped("Paste a YouTube link or direct HTTP/HTTPS media URL. PrismCast will resolve a preview when possible before playback starts.");
            ImGui.Spacing();
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        var inputHeight = phone ? 126f : 118f;
        if (ImGui.BeginChild("##WebMediaInputCard", new Vector2(0, inputHeight), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawTechFrame(S9Cyan);
            ImGui.TextDisabled("MEDIA URL");
            ImGui.Spacing();

            var inputWidth = Math.Max(140f, ImGui.GetContentRegionAvail().X - 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.060f, 0.067f, 0.125f, 0.99f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.52f, 0.38f, 0.96f, 0.88f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.2f);
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputTextWithHint("##WebMediaUrl", "Paste a YouTube or media URL...", ref _url, 2048))
            {
                _webPreview = null;
                _webStatus = "";
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            ImGui.Spacing();
            PushTechButton();
            if (ImGui.Button(_webPreviewLoading ? "LOADING..." : "LOAD MEDIA", new Vector2(-1, 34f)) && !_webPreviewLoading)
                RunUiTask(LoadWebMediaPreviewAsync);
            PopTechButton();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (!string.IsNullOrWhiteSpace(_webStatus))
        {
            ImGui.Spacing();
            var statusColor = _webStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? Danger : Muted;
            ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
            ImGui.TextWrapped(_webStatus);
            ImGui.PopStyleColor();
        }

        var preview = _webPreview;
        if (preview is null)
            return;

        ImGui.Spacing();
        var previewHeight = phone ? 246f : 230f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.045f, 0.050f, 0.095f, 0.995f));
        if (ImGui.BeginChild("##WebMediaPreviewCard", new Vector2(0, previewHeight), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawTechFrame();
            ImGui.PushStyleColor(ImGuiCol.Text, AccentHover);
            ImGui.TextUnformatted("READY TO PLAY");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            var contentW = ImGui.GetContentRegionAvail().X;
            var thumbW = phone ? 118f : 150f;
            var thumbH = phone ? 86f : 108f;
            if (contentW < 330f)
                thumbW = Math.Max(100f, contentW * 0.36f);

            var start = ImGui.GetCursorPos();
            DrawWebArtwork(preview, new Vector2(thumbW, thumbH));
            ImGui.SetCursorPos(new Vector2(start.X + thumbW + 12f, start.Y));
            var metaW = Math.Max(120f, contentW - thumbW - 12f);
            if (ImGui.BeginChild("##WebMediaPreviewMeta", new Vector2(metaW, thumbH), false,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGui.TextWrapped(preview.Title);
                if (!string.IsNullOrWhiteSpace(preview.Provider))
                    ImGui.TextDisabled(preview.Provider);
                var duration = FormatWebDuration(preview.DurationSeconds);
                if (!string.IsNullOrWhiteSpace(duration))
                    ImGui.TextDisabled(duration);
            }
            ImGui.EndChild();

            ImGui.SetCursorPosY(start.Y + thumbH + 14f);
            var durationText = FormatWebDuration(preview.DurationSeconds);
            var playLabel = string.IsNullOrWhiteSpace(durationText) ? "PLAY" : $"PLAY - {durationText}";
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.32f, 0.10f, 0.58f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.48f, 0.18f, 0.78f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.58f, 0.22f, 0.92f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, AccentHover);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
            if (ImGui.Button($"▶ {playLabel}##PlayWebPreview", new Vector2(-1, 38f)))
                RunUiTask(PlayWebMediaFromUiAsync);
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawWebArtwork(WebMediaPreview preview, Vector2 size)
    {
        if (!string.IsNullOrWhiteSpace(preview.ArtworkPath) && File.Exists(preview.ArtworkPath))
        {
            var shared = TextureProvider.GetFromFileAbsolute(preview.ArtworkPath);
            if (shared.TryGetWrap(out var wrap, out _) && wrap is not null)
            {
                ImGui.Image(wrap.Handle, size);
                return;
            }
        }

        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, U32(new Vector4(0.035f, 0.040f, 0.080f, 1f)), 8f);
        draw.AddRect(min, max, U32(new Vector4(0.45f, 0.31f, 0.95f, 0.75f)), 8f, ImDrawFlags.None, 1.2f);
        DrawUiIcon(UiIcon.Link, (min + max) * 0.5f, Math.Min(size.X, size.Y) * 0.34f, AccentHover, 2.2f);
        ImGui.Dummy(size);
    }

    private async Task LoadWebMediaPreviewAsync()
    {
        var url = _url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            _webPreview = null;
            _webStatus = "Error: Enter a valid HTTP or HTTPS URL.";
            return;
        }

        _webPreviewLoading = true;
        _webPreview = null;
        _webStatus = "Resolving media...";

        try
        {
            await _deps.EnsurePlaybackAsync().ConfigureAwait(false);

            var resolved = await TryResolveWebMetadataAsync(url).ConfigureAwait(false);
            _webPreview = resolved;
            _webStatus = resolved.Provider.Equals("Direct URL", StringComparison.OrdinalIgnoreCase)
                ? "Metadata preview was unavailable. PrismCast can still attempt direct playback."
                : "Media ready.";
        }
        catch (Exception ex)
        {
            _webPreview = null;
            _webStatus = "Error: " + ex.Message;
        }
        finally
        {
            _webPreviewLoading = false;
        }
    }

    private async Task<WebMediaPreview> TryResolveWebMetadataAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _deps.YtDlpExe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--dump-single-json");
        process.StartInfo.ArgumentList.Add("--no-playlist");
        process.StartInfo.ArgumentList.Add("--skip-download");
        process.StartInfo.ArgumentList.Add("--no-warnings");
        process.StartInfo.ArgumentList.Add(url);

        try
        {
            if (!process.Start())
                return BuildDirectWebPreview(url);

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return BuildDirectWebPreview(url);

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var title = JsonString(root, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = DirectWebTitle(url);

            var provider = JsonString(root, "extractor_key");
            if (string.IsNullOrWhiteSpace(provider))
                provider = JsonString(root, "extractor");
            if (string.IsNullOrWhiteSpace(provider))
                provider = parsedHost(url);

            var duration = 0d;
            if (root.TryGetProperty("duration", out var durationElement) && durationElement.ValueKind == JsonValueKind.Number)
                durationElement.TryGetDouble(out duration);

            var thumbnail = JsonString(root, "thumbnail");
            var artworkPath = string.IsNullOrWhiteSpace(thumbnail)
                ? null
                : await CacheWebArtworkAsync(thumbnail, timeout.Token).ConfigureAwait(false);

            return new WebMediaPreview(url, title, FriendlyProvider(provider), duration, artworkPath);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return BuildDirectWebPreview(url);
        }
        catch
        {
            return BuildDirectWebPreview(url);
        }
    }

    private static string JsonString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static string FriendlyProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return "Web Media";
        if (provider.Contains("youtube", StringComparison.OrdinalIgnoreCase))
            return "YouTube";
        return provider.Replace('_', ' ').Trim();
    }

    private static string parsedHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "Web Media";
    }

    private static WebMediaPreview BuildDirectWebPreview(string url)
        => new(url, DirectWebTitle(url), "Direct URL", 0, null);

    private static string DirectWebTitle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Web Media";
        var segment = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? "");
        if (!string.IsNullOrWhiteSpace(segment))
        {
            var withoutExtension = Path.GetFileNameWithoutExtension(segment);
            if (!string.IsNullOrWhiteSpace(withoutExtension))
                return withoutExtension.Replace('-', ' ').Replace('_', ' ').Trim();
        }
        return string.IsNullOrWhiteSpace(uri.Host) ? "Web Media" : uri.Host;
    }

    private async Task<string?> CacheWebArtworkAsync(string url, CancellationToken ct)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
            var directory = Path.Combine(_pi.GetPluginConfigDirectory(), "web-artwork");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, hash + ".jpg");
            if (File.Exists(path))
                return path;

            using var response = await PosterHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = File.Create(path);
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private async Task PlayWebMediaFromUiAsync()
    {
        var preview = _webPreview ?? BuildDirectWebPreview(_url.Trim());
        _uiStatus = $"Opening {preview.Title}...";
        await HostUrlFromUiAsync(preview.Url, preview.Title).ConfigureAwait(false);
        await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
        _uiStatus = "";
    }

    private void ClearWebMediaPreview()
    {
        _url = "";
        _webPreview = null;
        _webStatus = "";
        _webPreviewLoading = false;
    }

    private static string FormatWebDuration(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    private void DrawPhoneLocalFilesContent()
    {
        EnsureLocalLibraryLoaded();
        var folders = _config.LocalMediaFolders.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            DrawEmptyState("No media folders configured", "Add one or more folders in Settings > Local Media, or use Browse File for a one-time video.", "Open Local Media Settings", () =>
            {
                _settingsPage = SettingsPage.LocalMedia;
                _phoneSettingsHome = false;
                SelectPage(Page.Settings);
            });
            return;
        }

        List<LocalMediaEntry> files;
        lock (_localLock)
            files = [.. _localFiles];

        var filtered = files
            .Where(x => string.IsNullOrWhiteSpace(_localSearch) || x.Name.Contains(_localSearch, StringComparison.OrdinalIgnoreCase))
            .Take(1000)
            .ToList();

        var scrollHeight = Math.Max(120f, ImGui.GetContentRegionAvail().Y);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 9f);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.48f, 0.18f, 0.82f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.64f, 0.30f, 1.00f, 1f));
        if (ImGui.BeginChild("##PhoneLocalMediaScroll", new Vector2(0, scrollHeight), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            ImGui.TextDisabled($"{filtered.Count} local video{(filtered.Count == 1 ? "" : "s")}");
            ImGui.Spacing();
            foreach (var entry in filtered)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
                if (ImGui.BeginChild($"##PhoneLocalDynamic{entry.Path}", new Vector2(0, 70f), true, ImGuiWindowFlags.NoScrollbar))
                {
                    DrawTechFrame();
                    ImGui.TextWrapped(TrimForDisplay(entry.Name, 58));
                    ImGui.TextDisabled($"{entry.Folder}  •  {FormatBytes(entry.SizeBytes)}");
                    ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 84f));
                    PushTechButton();
                    if (ImGui.Button($"PLAY##PhoneLocalDynamicPlay{entry.Path}", new Vector2(70f, 28f)))
                    {
                        var path = entry.Path;
                        RunUiTask(async () =>
                        {
                            _nowPlayingPlexItem = null;
                            await HostLocalFromUiAsync(path).ConfigureAwait(false);
                            SelectPage(Page.RemoteControl);
                        });
                    }
                    PopTechButton();
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    private void DrawPhonePlexLibrary()
    {
        EnsurePlexLoaded();
        DrawPhoneLibrarySearchRow(plex: true);

        if (!PlexConnected())
        {
            ImGui.Spacing();
            DrawEmptyState("Plex isn't connected", "Connect your Plex account once and PrismCast will discover your media server and libraries.", "Connect Plex", () => RunUiTask(ConnectPlexAsync));
            return;
        }

        List<PlexLibrary> libraries;
        List<PlexItem> items;
        bool canBack;
        int selectedIndex;
        lock (_plexLock)
        {
            libraries = [.. _libraries];
            items = [.. _plexItems];
            canBack = _plexHistory.Count > 0;
            selectedIndex = _libraryIndex;
        }

        ImGui.Spacing();
        DrawPhoneLibraryCategories(libraries, selectedIndex);
        ImGui.Spacing();

        if (canBack)
        {
            PushTechButton();
            if (ImGui.Button("<  BACK", new Vector2(92f, 30f)))
                PlexGoBack();
            PopTechButton();
            ImGui.Spacing();
        }

        var filtered = items
            .Where(x => string.IsNullOrWhiteSpace(_plexSearch) || PlexItemLabel(x).Contains(_plexSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No media found here.");
            return;
        }

        DrawPhonePlexTileGrid(filtered);
    }

    private void DrawPhoneLocalFiles()
    {
        EnsureLocalLibraryLoaded();
        DrawPhoneLibrarySearchRow(plex: false);

        var folders = _config.LocalMediaFolders.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            ImGui.Spacing();
            DrawEmptyState("No media folders configured", "Add one or more folders in Settings > Local Media, or use Browse File for a one-time video.", "Open Local Media Settings", () =>
            {
                _settingsPage = SettingsPage.LocalMedia;
                _phoneSettingsHome = false;
                SelectPage(Page.Settings);
            });
            return;
        }

        List<LocalMediaEntry> files;
        lock (_localLock)
            files = [.. _localFiles];

        var filtered = files
            .Where(x => string.IsNullOrWhiteSpace(_localSearch) || x.Name.Contains(_localSearch, StringComparison.OrdinalIgnoreCase))
            .Take(1000)
            .ToList();

        ImGui.Spacing();
        ImGui.TextDisabled($"{filtered.Count} local video{(filtered.Count == 1 ? "" : "s")}");
        ImGui.Spacing();

        foreach (var entry in filtered)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
            if (ImGui.BeginChild($"##PhoneLocal{entry.Path}", new Vector2(0, 70f), true, ImGuiWindowFlags.NoScrollbar))
            {
                DrawTechFrame();
                var title = TrimForDisplay(entry.Name, 58);
                ImGui.TextWrapped(title);
                ImGui.TextDisabled($"{entry.Folder}  •  {FormatBytes(entry.SizeBytes)}");
                ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 84f));
                PushTechButton();
                if (ImGui.Button($"PLAY##PhoneLocalPlay{entry.Path}", new Vector2(70f, 28f)))
                {
                    var path = entry.Path;
                    RunUiTask(async () =>
                    {
                        _nowPlayingPlexItem = null;
                        await HostLocalFromUiAsync(path).ConfigureAwait(false);
                        SelectPage(Page.RemoteControl);
                    });
                }
                PopTechButton();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawPhoneLibrarySearchRow(bool plex)
    {
        var available = ImGui.GetContentRegionAvail().X;
        const float iconSize = 36f;
        const float gap = 7f;
        var searchWidth = Math.Max(180f, available - iconSize * 2f - gap * 2f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.SetNextItemWidth(searchWidth);
        if (plex)
            ImGui.InputText("##PhonePlexSearch", ref _plexSearch, 256);
        else
            ImGui.InputText("##PhoneLocalSearch", ref _localSearch, 256);
        ImGui.PopStyleVar();

        ImGui.SameLine(0, gap);
        var sourceIcon = plex ? UiIcon.Folder : UiIcon.Library;
        if (PhoneLibraryToolbarButton(plex ? "PhoneOpenLocal" : "PhoneOpenPlex", sourceIcon, plex ? "Local Files" : "Plex Library", iconSize))
            _librarySource = plex ? LibrarySource.LocalFiles : LibrarySource.Plex;

        ImGui.SameLine(0, gap);
        if (PhoneLibraryToolbarButton("PhoneLibraryRefresh", UiIcon.Reset, "Refresh", iconSize))
        {
            if (plex)
                RunUiTask(LoadPlexLibrariesAsync);
            else
                RefreshLocalLibrary();
        }
    }

    private bool PhoneLibraryToolbarButton(string id, UiIcon icon, string tooltip, float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(size, size);
        draw.AddRectFilled(pos, max, U32(hovered ? new Vector4(0.20f, 0.10f, 0.34f, 1f) : new Vector4(0.070f, 0.075f, 0.135f, 1f)), 8f);
        draw.AddRect(pos, max, U32(hovered ? AccentHover : new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.75f)), 8f, ImDrawFlags.None, 1.3f);
        DrawUiIcon(icon, pos + new Vector2(size * 0.5f), 18f, hovered ? AccentHover : Vector4.One, 1.8f);
        if (hovered)
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void DrawPhoneLibraryCategories(List<PlexLibrary> libraries, int selectedIndex)
    {
        if (libraries.Count == 0)
            return;

        var desired = new[] { "Movies", "TV Shows", "Anime", "Anime Movies" };
        var ordered = libraries
            .Select((library, index) => (library, index))
            .OrderBy(x =>
            {
                var priority = Array.FindIndex(desired, name => name.Equals(x.library.Title, StringComparison.OrdinalIgnoreCase));
                return priority < 0 ? 100 : priority;
            })
            .ThenBy(x => x.library.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const float gap = 9f;
        var available = ImGui.GetContentRegionAvail().X;
        var width = (available - gap) * 0.5f;
        const float height = 50f;

        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            var selected = entry.index == selectedIndex && _plexHistory.Count == 0;
            if (PhoneLibraryCategoryButton(entry.library, selected, new Vector2(width, height)))
            {
                lock (_plexLock) _libraryIndex = entry.index;
                RunUiTask(() => LoadPlexLibraryAsync(entry.library));
            }

            if (i % 2 == 0 && i < ordered.Count - 1)
                ImGui.SameLine(0, gap);
        }
    }

    private bool PhoneLibraryCategoryButton(PlexLibrary library, bool selected, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##PhoneCategory{library.Key}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        var fill = selected
            ? new Vector4(0.26f, 0.09f, 0.48f, 0.98f)
            : hovered ? new Vector4(0.13f, 0.10f, 0.24f, 0.98f) : new Vector4(0.060f, 0.067f, 0.120f, 0.98f);
        var outline = selected ? AccentHover : new Vector4(0.55f, 0.34f, 0.96f, hovered ? 0.98f : 0.72f);
        draw.AddRectFilled(pos, max, U32(fill), 8f);
        if (selected)
        {
            draw.AddRect(pos - new Vector2(2f, 2f), max + new Vector2(2f, 2f), U32(new Vector4(0.58f, 0.18f, 1f, 0.20f)), 10f, ImDrawFlags.None, 5f);
            draw.AddRect(pos - new Vector2(1f, 1f), max + new Vector2(1f, 1f), U32(new Vector4(0.24f, 0.80f, 1f, 0.18f)), 9f, ImDrawFlags.None, 2.5f);
        }
        draw.AddRect(pos, max, U32(outline), 8f, ImDrawFlags.None, selected ? 2.0f : 1.35f);

        var icon = LibraryCategoryIcon(library.Title);
        var iconColor = selected ? new Vector4(0.88f, 0.75f, 1f, 1f) : new Vector4(0.72f, 0.64f, 0.94f, 1f);
        var iconCenter = pos + new Vector2(28f, size.Y * 0.5f);
        DrawUiIcon(icon, iconCenter, 21f, iconColor, 1.8f);

        var text = library.Title;
        var textSize = ImGui.CalcTextSize(text);
        var textLeft = pos.X + 51f;
        var textRight = max.X - 8f;
        var textX = textLeft + Math.Max(0, (textRight - textLeft - textSize.X) * 0.5f);
        var textY = pos.Y + (size.Y - textSize.Y) * 0.5f;
        draw.AddText(new Vector2(textX, textY), U32(Vector4.One), text);
        return clicked;
    }

    private static UiIcon LibraryCategoryIcon(string title)
    {
        if (title.Contains("anime", StringComparison.OrdinalIgnoreCase) && title.Contains("movie", StringComparison.OrdinalIgnoreCase))
            return UiIcon.FilmReel;
        if (title.Contains("anime", StringComparison.OrdinalIgnoreCase))
            return UiIcon.AnimeSpark;
        if (title.Contains("tv", StringComparison.OrdinalIgnoreCase) || title.Contains("show", StringComparison.OrdinalIgnoreCase))
            return UiIcon.Television;
        return UiIcon.Movie;
    }

    private void DrawPlexLibrary()
    {
        EnsurePlexLoaded();

        var connected = PlexConnected();
        if (!connected)
        {
            DrawEmptyState("Plex isn't connected", "Connect your Plex account once and PrismCast will discover your media server and libraries.", "Connect Plex", () => RunUiTask(ConnectPlexAsync));
            return;
        }

        ImGui.TextDisabled("SEARCH LIBRARY");
        var searchRowWidth = ImGui.GetContentRegionAvail().X;
        const float refreshWidth = 96f;
        ImGui.SetNextItemWidth(Math.Max(180f, searchRowWidth - refreshWidth - 10f));
        ImGui.InputText("##PlexSearch", ref _plexSearch, 256);
        ImGui.SameLine(0, 10f);
        if (ImGui.Button("Refresh", new Vector2(refreshWidth, 28)))
            RunUiTask(LoadPlexLibrariesAsync);

        List<PlexLibrary> libraries;
        List<PlexItem> items;
        string pageTitle;
        bool canBack;
        int selectedIndex;
        lock (_plexLock)
        {
            libraries = [.. _libraries];
            items = [.. _plexItems];
            pageTitle = _plexPageTitle;
            canBack = _plexHistory.Count > 0;
            selectedIndex = _libraryIndex;
        }

        ImGui.Spacing();
        DrawLibraryChips(libraries, selectedIndex);
        ImGui.Spacing();

        if (_selectedPlexDetailsItem is not null)
        {
            DrawPlexDetailsView(_selectedPlexDetailsItem, phone: false);
            return;
        }

        if (canBack)
        {
            if (ImGui.Button("< Back", new Vector2(80, 28)))
                PlexGoBack();
            ImGui.SameLine();
        }
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(pageTitle) ? "Library" : pageTitle);

        var filtered = items
            .Where(x => string.IsNullOrWhiteSpace(_plexSearch) || x.Title.Contains(_plexSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No media found here.");
            return;
        }

        ImGui.Spacing();
        DrawPlexTileGrid(filtered);
    }

    private void DrawLibraryChips(List<PlexLibrary> libraries, int selectedIndex)
    {
        if (libraries.Count == 0) return;
        var avail = ImGui.GetContentRegionAvail().X;
        float used = 0f;
        for (var i = 0; i < libraries.Count; i++)
        {
            var selected = i == selectedIndex && _plexHistory.Count == 0;
            var label = libraries[i].Title;
            var estimated = ImGui.CalcTextSize(label).X + 28f;
            if (used > 0f && used + estimated > avail)
                used = 0f;
            else if (used > 0f)
                ImGui.SameLine(0, 7f);

            PushTechButton(selected);
            if (ImGui.Button(label))
            {
                var index = i;
                lock (_plexLock) _libraryIndex = index;
                RunUiTask(() => LoadPlexLibraryAsync(libraries[index]));
            }
            PopTechButton();
            used += estimated + (used > 0 ? 7f : 0f);
        }
    }

    private void DrawPlexTileGrid(List<PlexItem> items)
    {
        if (EffectiveInterfaceMode() == InterfaceMode.Phone)
        {
            DrawPhonePlexTileGrid(items);
            return;
        }

        const float gap = 10f;
        var available = Math.Max(180f, ImGui.GetContentRegionAvail().X);
        var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
        var columns = phone ? 2 : Math.Max(2, (int)((available + gap) / (166f + gap)));
        var cardWidth = (available - gap * (columns - 1)) / columns;
        cardWidth = phone ? Math.Max(145f, cardWidth) : Math.Min(180f, cardWidth);
        var posterWidth = cardWidth - 14f;
        var posterHeight = posterWidth * 1.46f;
        var cardHeight = posterHeight + 88f;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var container = IsPlexContainer(item);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
            if (ImGui.BeginChild($"##PlexTile{item.RatingKey}", new Vector2(cardWidth, cardHeight), true))
            {
                DrawTechFrame(container ? S9Cyan : Accent);
                DrawPlexPoster(item, new Vector2(posterWidth, posterHeight));
                if (ImGui.IsItemClicked())
                    _selectedPlexDetailsItem = item;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("View details");
                ImGui.Spacing();
                ImGui.TextWrapped(TrimForDisplay(PlexItemLabel(item), phone ? 30 : 38));
                ImGui.SetCursorPosY(cardHeight - 38f);
                PushTechButton();
                if (ImGui.Button(container ? "OPEN" : "PLAY", new Vector2(-1, 27)))
                {
                    if (container)
                        RunUiTask(() => OpenPlexContainerAsync(item));
                    else
                        RunUiTask(() => PlayPlexItemFromUiAsync(item));
                }
                PopTechButton();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            if ((i + 1) % columns != 0 && i < items.Count - 1) ImGui.SameLine(0, gap);
        }
    }

    private void DrawPhonePlexTileGrid(List<PlexItem> items)
    {
        var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
        var available = Math.Max(300f, ImGui.GetContentRegionAvail().X);
        var columns = phone
            ? 3
            : Math.Clamp((int)((available + 9f) / 168f), 4, 7);
        var sidePadding = phone ? 10f : 14f;
        const float gap = 9f;
        var cardWidth = (available - sidePadding * 2f - gap * (columns - 1)) / columns;
        var posterWidth = Math.Max(88f, cardWidth - 10f);
        var posterHeight = posterWidth * 1.46f;
        const float titleArea = 44f;
        const float actionHeight = 31f;
        var cardHeight = posterHeight + titleArea + actionHeight + 18f;
        var rowGap = 10f;
        var startX = ImGui.GetCursorPosX();
        var startY = ImGui.GetCursorPosY();

        // Keep the full Plex result set available, but only render rows that are
        // visible (plus a small buffer) so large libraries stay responsive.
        var totalRows = (items.Count + columns - 1) / columns;
        var rowStride = cardHeight + rowGap;
        var scrollY = Math.Max(0f, ImGui.GetScrollY());
        var viewportHeight = Math.Max(1f, ImGui.GetWindowHeight());
        var maxRow = Math.Max(0, totalRows - 1);
        var firstVisibleRow = Math.Clamp((int)MathF.Floor((scrollY - startY) / rowStride) - 1, 0, maxRow);
        var lastVisibleRow = Math.Clamp((int)MathF.Ceiling((scrollY + viewportHeight - startY) / rowStride) + 1, 0, maxRow);
        var firstItem = firstVisibleRow * columns;
        var lastItemExclusive = Math.Min(items.Count, (lastVisibleRow + 1) * columns);

        for (var i = firstItem; i < lastItemExclusive; i++)
        {
            var item = items[i];
            var row = i / columns;
            var column = i % columns;
            var x = startX + sidePadding + column * (cardWidth + gap);
            var y = startY + row * (cardHeight + rowGap);
            ImGui.SetCursorPos(new Vector2(x, y));

            var container = IsPlexContainer(item);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.045f, 0.050f, 0.095f, 0.995f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5f, 5f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
            if (ImGui.BeginChild($"##PhonePlexTile{item.RatingKey}", new Vector2(cardWidth, cardHeight), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                DrawTechFrame(container ? S9Cyan : Accent);
                ImGui.SetCursorPosX(Math.Max(5f, (cardWidth - posterWidth) * 0.5f));
                DrawPlexPoster(item, new Vector2(posterWidth, posterHeight));
                if (ImGui.IsItemClicked())
                    _selectedPlexDetailsItem = item;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("View details");

                ImGui.SetCursorPosY(posterHeight + 9f);
                DrawCenteredWrappedText(PlexItemLabel(item), cardWidth - 12f, 3);

                ImGui.SetCursorPosY(cardHeight - actionHeight - 6f);
                var runtime = PlexRuntimeLabel(item);
                var label = container ? "OPEN" : string.IsNullOrWhiteSpace(runtime) ? "PLAY" : $"PLAY - {runtime}";
                if (PhonePlexActionButton($"PhonePlexAction{item.RatingKey}", label, new Vector2(cardWidth - 10f, actionHeight), container))
                {
                    if (container)
                        RunUiTask(() => OpenPlexContainerAsync(item));
                    else
                        RunUiTask(() => PlayPlexItemFromUiAsync(item));
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
        }

        ImGui.SetCursorPos(new Vector2(startX, startY + totalRows * rowStride));
    }

    private void DrawPlexDetailsView(PlexItem item, bool phone)
    {
        var availableWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var availableHeight = Math.Max(180f, ImGui.GetContentRegionAvail().Y);

        PushTechButton();
        if (ImGui.Button("<  BACK TO LIBRARY", new Vector2(phone ? 156f : 172f, 32f)))
        {
            _selectedPlexDetailsItem = null;
            PopTechButton();
            return;
        }
        PopTechButton();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.045f, 0.050f, 0.095f, 0.995f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(phone ? 14f : 18f, 16f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f);
        if (ImGui.BeginChild("##PlexDetailsView", new Vector2(0, availableHeight - 42f), true))
        {
            DrawTechFrame(AccentHover);
            if (phone)
                DrawPhonePlexDetailsContent(item, availableWidth - 28f);
            else
                DrawDesktopPlexDetailsContent(item, availableWidth - 36f);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private void DrawPhonePlexDetailsContent(PlexItem item, float contentWidth)
    {
        var posterWidth = Math.Clamp(contentWidth * 0.42f, 122f, 168f);
        var posterHeight = posterWidth * 1.46f;
        ImGui.SetCursorPosX(14f + Math.Max(0f, (contentWidth - posterWidth) * 0.5f));
        DrawPlexPoster(item, new Vector2(posterWidth, posterHeight));
        ImGui.Spacing();

        DrawCenteredWrappedText(PlexItemLabel(item), contentWidth, 3);
        DrawCenteredPlexMetadata(item, contentWidth);
        ImGui.Spacing();
        DrawPlexSynopsis(item);
        ImGui.Spacing();
        DrawPlexDetailsAction(item, contentWidth, phone: true);
    }

    private void DrawDesktopPlexDetailsContent(PlexItem item, float contentWidth)
    {
        const float posterWidth = 205f;
        const float gap = 22f;
        var posterHeight = posterWidth * 1.46f;
        DrawPlexPoster(item, new Vector2(posterWidth, posterHeight));
        ImGui.SameLine(0, gap);

        var textWidth = Math.Max(180f, contentWidth - posterWidth - gap);
        if (ImGui.BeginChild("##PlexDetailsText", new Vector2(textWidth, posterHeight), false))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
            ImGui.TextWrapped(PlexItemLabel(item));
            ImGui.PopStyleColor();
            ImGui.Spacing();
            DrawPlexMetadata(item);
            ImGui.Spacing();
            DrawPlexSynopsis(item);
            ImGui.Spacing();
            DrawPlexDetailsAction(item, textWidth, phone: false);
        }
        ImGui.EndChild();
    }

    private static void DrawPlexMetadata(PlexItem item)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, S9Cyan);
        ImGui.TextUnformatted(PlexTypeLabel(item));
        ImGui.PopStyleColor();
        var runtime = PlexRuntimeLabel(item);
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            ImGui.SameLine(0, 10f);
            ImGui.TextDisabled(runtime);
        }
    }

    private static void DrawCenteredPlexMetadata(PlexItem item, float width)
    {
        var type = PlexTypeLabel(item);
        var runtime = PlexRuntimeLabel(item);
        var text = string.IsNullOrWhiteSpace(runtime) ? type : $"{type}  •  {runtime}";
        var textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(14f + Math.Max(0f, (width - textWidth) * 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Text, S9Cyan);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void DrawPlexSynopsis(PlexItem item)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, AccentHover);
        ImGui.TextUnformatted("SYNOPSIS");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();
        if (string.IsNullOrWhiteSpace(item.Summary))
            ImGui.TextDisabled("No synopsis is available for this title.");
        else
            ImGui.TextWrapped(item.Summary.Trim());
    }

    private void DrawPlexDetailsAction(PlexItem item, float contentWidth, bool phone)
    {
        var container = IsPlexContainer(item);
        var width = phone ? Math.Clamp(contentWidth * 0.58f, 150f, 220f) : Math.Min(220f, contentWidth);
        if (phone)
            ImGui.SetCursorPosX(14f + Math.Max(0f, (contentWidth - width) * 0.5f));

        if (!PhonePlexActionButton($"PlexDetailsAction{item.RatingKey}", container ? "OPEN" : "PLAY", new Vector2(width, 36f), container))
            return;

        _selectedPlexDetailsItem = null;
        if (container)
            RunUiTask(() => OpenPlexContainerAsync(item));
        else
            RunUiTask(() => PlayPlexItemFromUiAsync(item));
    }

    private static string PlexTypeLabel(PlexItem item) => item.Type.ToLowerInvariant() switch
    {
        "movie" => "MOVIE",
        "show" => "TV SHOW",
        "season" => "SEASON",
        "episode" => "EPISODE",
        _ => "MEDIA",
    };

    private bool PhonePlexActionButton(string id, string label, Vector2 size, bool container)
    {
        var normal = container ? new Vector4(0.055f, 0.20f, 0.31f, 1f) : new Vector4(0.32f, 0.10f, 0.58f, 1f);
        var hovered = container ? new Vector4(0.08f, 0.30f, 0.42f, 1f) : new Vector4(0.48f, 0.18f, 0.78f, 1f);
        var active = container ? new Vector4(0.10f, 0.36f, 0.50f, 1f) : new Vector4(0.58f, 0.22f, 0.92f, 1f);
        var border = container ? S9Cyan : AccentHover;

        ImGui.PushStyleColor(ImGuiCol.Button, normal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        ImGui.PushStyleColor(ImGuiCol.Border, border);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        var shown = container ? label : "▶ " + label;
        var clicked = ImGui.Button($"{shown}##{id}", size);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static string PlexRuntimeLabel(PlexItem item)
    {
        if (item.DurationMs <= 0)
            return "";
        var span = TimeSpan.FromMilliseconds(item.DurationMs);
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes}:{span.Seconds:00}";
    }

    private static void DrawCenteredWrappedText(string text, float width, int maxLines)
    {
        var lines = WrapTextLines(text, width, maxLines);
        var lineHeight = ImGui.GetTextLineHeight();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < lines.Count; i++)
        {
            var lineSize = ImGui.CalcTextSize(lines[i]);
            var x = origin.X + Math.Max(0, (width - lineSize.X) * 0.5f);
            draw.AddText(new Vector2(x, origin.Y + i * lineHeight), U32(Vector4.One), lines[i]);
        }
        ImGui.Dummy(new Vector2(width, Math.Max(lineHeight, lines.Count * lineHeight)));
    }

    private static List<string> WrapTextLines(string text, float width, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [""];

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>(maxLines);
        var current = "";
        var wordIndex = 0;

        for (; wordIndex < words.Length; wordIndex++)
        {
            var candidate = current.Length == 0 ? words[wordIndex] : current + " " + words[wordIndex];
            if (ImGui.CalcTextSize(candidate).X <= width)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
                wordIndex--;
                if (lines.Count >= maxLines)
                    break;
                continue;
            }

            current = FitTextToWidth(words[wordIndex], width);
        }

        if (lines.Count < maxLines && current.Length > 0)
            lines.Add(current);

        if (wordIndex < words.Length && lines.Count > 0)
            lines[^1] = FitTextToWidth(lines[^1].TrimEnd('…') + "…", width);

        return lines.Count == 0 ? [FitTextToWidth(text, width)] : lines;
    }

    private static string FitTextToWidth(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;

        var value = text.TrimEnd('…');
        while (value.Length > 1 && ImGui.CalcTextSize(value + "…").X > width)
            value = value[..^1];
        return value + "…";
    }

    private void DrawPlexPoster(PlexItem item, Vector2 size)
    {
        var path = GetOrQueuePoster(item);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var shared = TextureProvider.GetFromFileAbsolute(path);
            if (shared.TryGetWrap(out var wrap, out _) && wrap is not null)
            {
                ImGui.Image(wrap.Handle, size);
                return;
            }
        }

        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.085f, 0.15f, 1f)), 8f);
        draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.26f)), 8f);
        var label = string.IsNullOrWhiteSpace(item.Thumb) ? "NO POSTER" : "LOADING POSTER";
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(min + (size - textSize) * 0.5f, ImGui.ColorConvertFloat4ToU32(Muted), label);
        ImGui.Dummy(size);
    }

    private string? GetOrQueuePoster(PlexItem item)
    {
        var url = _plex.GetArtworkUrl(item);
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var path = Path.Combine(_posterCacheDirectory, hash + ".jpg");
        if (File.Exists(path))
            return path;

        if (_posterDownloads.TryAdd(path, 0))
            _ = DownloadPosterAsync(url, path);

        return null;
    }

    private async Task DownloadPosterAsync(string url, string path)
    {
        var temporary = path + ".download";
        try
        {
            using var response = await PosterHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
        }
        finally
        {
            _posterDownloads.TryRemove(path, out _);
        }
    }

    private void DrawLocalFiles()
    {
        EnsureLocalLibraryLoaded();

        ImGui.SetNextItemWidth(-226);
        ImGui.InputText("Search local media", ref _localSearch, 256);
        ImGui.SameLine();
        if (ImGui.Button("Browse File", new Vector2(104, 28)))
            OpenSingleFileDialog();
        ImGui.SameLine();
        if (ImGui.Button("Refresh", new Vector2(102, 28)))
            RefreshLocalLibrary();

        var folders = _config.LocalMediaFolders.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            ImGui.Spacing();
            DrawEmptyState("No media folders configured", "Add one or more folders in Settings > Local Media, or use Browse File for a one-time video.", "Open Local Media Settings", () =>
            {
                _settingsPage = SettingsPage.LocalMedia;
                _phoneSettingsHome = false;
                SelectPage(Page.Settings);
            });
            return;
        }

        List<LocalMediaEntry> files;
        lock (_localLock)
            files = [.. _localFiles];

        var filtered = files
            .Where(x => string.IsNullOrWhiteSpace(_localSearch) || x.Name.Contains(_localSearch, StringComparison.OrdinalIgnoreCase))
            .Take(1000)
            .ToList();

        ImGui.Spacing();
        ImGui.TextDisabled($"{filtered.Count} video{(filtered.Count == 1 ? "" : "s")} shown");
        ImGui.Spacing();

        if (ImGui.BeginChild("##LocalFileList", new Vector2(0, 0), false))
        {
            foreach (var entry in filtered)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
                if (ImGui.BeginChild($"##Local{entry.Path}", new Vector2(0, 62), true))
                {
                    ImGui.TextUnformatted(entry.Name);
                    ImGui.TextDisabled($"{entry.Folder}   •   {FormatBytes(entry.SizeBytes)}");
                    ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 96));
                    if (ImGui.Button($"Play##{entry.Path}", new Vector2(72, 28)))
                    {
                        var path = entry.Path;
                        RunUiTask(async () =>
                        {
                            _nowPlayingPlexItem = null;
                            await HostLocalFromUiAsync(path).ConfigureAwait(false);
                            SelectPage(Page.RemoteControl);
                        });
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }
        ImGui.EndChild();
    }

    private void DrawJoinSession()
    {
        if (EffectiveInterfaceMode() == InterfaceMode.Phone)
        {
            DrawPhoneSessionHub();
            return;
        }

        DrawTabletSession();
    }

    private void DrawTabletSession()
    {
        DrawSectionHeading("Watch Together", "// SESSION");
        ImGui.Spacing();
        MaybeRefreshGroupStatuses();

        var available = ImGui.GetContentRegionAvail();
        const float gap = 12f;
        var leftWidth = Math.Clamp((available.X - gap) * 0.44f, 310f, 430f);

        if (ImGui.BeginChild("##TabletSessionActions", new Vector2(leftWidth, available.Y), false))
        {
            DrawPhoneHostSessionContainer();
            ImGui.Spacing();
            DrawPhoneJoinSessionContainer();
        }
        ImGui.EndChild();

        ImGui.SameLine(0, gap);
        if (ImGui.BeginChild("##TabletSessionGroups", new Vector2(0, available.Y), false))
            DrawPhoneGroupsContainer();
        ImGui.EndChild();
    }

    private void BeginDirectLobbyJoin(string rawCode)
    {
        var code = rawCode.Trim();
        if (code.StartsWith("PRIVATE:", StringComparison.OrdinalIgnoreCase))
            code = code[8..].Trim();

        RunUiTask(async () =>
        {
            if (!PrismInvite.TryDecode(code, out _, out _))
                throw new InvalidOperationException("That PrismCast lobby code is invalid.");

            await _session.JoinAsync(code).ConfigureAwait(false);
            await RegisterViewerPresenceAsync(code, LocalFirstName()).ConfigureAwait(false);
            await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
        });
    }

    private string DirectoryUrl => RelayClient.DirectoryBaseUrl;

    private void DrawPhoneSessionHub()
    {
        DrawSectionHeading("Session", "// WATCH TOGETHER");
        ImGui.Spacing();
        MaybeRefreshGroupStatuses();

        DrawPhoneHostSessionContainer();
        ImGui.Spacing();
        DrawPhoneJoinSessionContainer();
        ImGui.Spacing();
        DrawPhoneGroupsContainer();
    }

    private static void BeginSessionCardContent(float top = 10f)
    {
        ImGui.SetCursorPosY(top);
        ImGui.Indent(SessionCardInset);
    }

    private static void EndSessionCardContent() => ImGui.Unindent(SessionCardInset);

    private static float SessionContentWidth() =>
        Math.Max(80f, ImGui.GetContentRegionAvail().X - SessionCardInset);

    private static float CenteredSessionX(float itemWidth, float contentWidth) =>
        SessionCardInset + Math.Max(0f, (contentWidth - itemWidth) * 0.5f);

    private static void DrawSessionPanelHeading(string label, UiIcon icon, Vector4 color, string? meta = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = SessionContentWidth();
        DrawUiIcon(icon, origin + new Vector2(9f, 9f), 17f, color, 1.8f);
        ImGui.GetWindowDrawList().AddText(origin + new Vector2(22f, 1f), U32(color), label.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(meta))
        {
            var shown = FitTextToWidth(meta.ToUpperInvariant(), Math.Max(70f, width * 0.36f));
            var textSize = ImGui.CalcTextSize(shown);
            ImGui.GetWindowDrawList().AddText(
                origin + new Vector2(Math.Max(26f, width - textSize.X), 1f), U32(Muted), shown);
        }

        ImGui.Dummy(new Vector2(width, 20f));
    }

    private static bool DrawSessionIconButton(string id, UiIcon icon, Vector2 size, bool active = false)
    {
        var origin = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var fill = active
            ? new Vector4(0.34f, 0.14f, 0.58f, 0.98f)
            : new Vector4(0.075f, 0.085f, 0.155f, 0.98f);
        if (hovered)
            fill = new Vector4(0.24f, 0.14f, 0.42f, 1f);
        draw.AddRectFilled(origin, origin + size, U32(fill), 7f);
        draw.AddRect(origin, origin + size, U32(active ? AccentHover : S9Blue), 7f, ImDrawFlags.None, active ? 2f : 1.2f);
        DrawUiIcon(icon, origin + size * 0.5f, MathF.Min(size.X, size.Y) * 0.48f, Vector4.One, 1.8f);
        return pressed;
    }

    private static bool DrawSessionCodeInput(string id, ref string value, string hint, UiIcon icon = UiIcon.Ticket)
    {
        var cursorX = ImGui.GetCursorPosX();
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(120f, SessionContentWidth());

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 8f));
        var height = ImGui.GetFrameHeight();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), U32(new Vector4(0.055f, 0.065f, 0.125f, 1f)), 7f);

        ImGui.SetCursorPosX(cursorX + 40f);
        ImGui.SetNextItemWidth(Math.Max(70f, width - 40f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        var submitted = ImGui.InputTextWithHint($"##{id}", hint, ref value, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var hovered = ImGui.IsItemHovered();
        draw.AddRect(origin, origin + new Vector2(width, height), U32(hovered ? AccentHover : S9Blue), 7f,
            ImDrawFlags.None, hovered ? 1.8f : 1.1f);
        DrawUiIcon(icon, origin + new Vector2(20f, height * 0.5f), 20f,
            hovered ? AccentHover : new Vector4(0.76f, 0.72f, 0.96f, 1f), 1.7f);
        ImGui.PopStyleVar();
        return submitted;
    }

    private void DrawSessionMediaHero(string title, MpvPlaybackInfo info)
    {
        const float height = 86f;
        var width = SessionContentWidth();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.030f, 0.040f, 0.085f, 1f));
        if (ImGui.BeginChild("##SessionMediaHero", new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            var draw = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos() + new Vector2(1f, 1f);
            var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(1f, 1f);
            draw.AddRect(min, max, U32(new Vector4(S9Blue.X, S9Blue.Y, S9Blue.Z, 0.58f)), 8f);
            draw.AddLine(min + new Vector2(12f, 1f), min + new Vector2(Math.Max(34f, ImGui.GetWindowWidth() * 0.62f), 1f),
                U32(AccentHover), 2f);

            ImGui.SetCursorPos(new Vector2(7f, 7f));
            if (_nowPlayingPlexItem is { } item)
            {
                DrawPlexPoster(item, new Vector2(50f, 70f));
            }
            else
            {
                var artMin = ImGui.GetCursorScreenPos();
                var artSize = new Vector2(50f, 70f);
                draw.AddRectFilled(artMin, artMin + artSize, U32(new Vector4(0.11f, 0.06f, 0.20f, 1f)), 6f);
                draw.AddRect(artMin, artMin + artSize, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.72f)), 6f);
                DrawPrismGlyph(artMin + artSize * 0.5f, 15f);
                ImGui.Dummy(artSize);
            }

            ImGui.SetCursorPos(new Vector2(68f, 12f));
            ImGui.PushTextWrapPos(Math.Max(90f, ImGui.GetWindowWidth() - 10f));
            ImGui.SetWindowFontScale(1.06f);
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(title) ? "PrismCast session" : title);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopTextWrapPos();
            ImGui.SetCursorPosX(68f);
            ImGui.TextDisabled($"{FormatTime(info.PositionSeconds)}  /  {FormatTime(info.DurationSeconds)}");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static void DrawCompactSessionTimeline(MpvPlaybackInfo info)
    {
        var duration = Math.Max(0, info.DurationSeconds);
        var position = Math.Clamp(info.PositionSeconds, 0, duration > 0 ? duration : Math.Max(1, info.PositionSeconds));
        var fraction = duration > 0 ? (float)(position / duration) : 0f;
        var width = SessionContentWidth();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        const float height = 5f;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), U32(new Vector4(0.08f, 0.15f, 0.25f, 1f)), 3f);
        if (fraction > 0f)
        {
            var fill = Math.Max(2f, width * fraction);
            draw.AddRectFilled(origin, origin + new Vector2(fill, height), U32(S9Cyan), 3f);
            draw.AddLine(origin + new Vector2(fill, 0), origin + new Vector2(fill, height), U32(AccentHover), 2f);
        }
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawSessionCodeCard(string code, bool permanentGroupCode)
    {
        var availableWidth = SessionContentWidth();
        var cardWidth = Math.Clamp(availableWidth * 0.66f, 230f, 300f);
        ImGui.SetCursorPosX(CenteredSessionX(cardWidth, availableWidth));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.035f, 0.055f, 0.105f, 0.99f));
        if (ImGui.BeginChild("##ActiveSessionCode", new Vector2(cardWidth, 96f), true, ImGuiWindowFlags.NoScrollbar))
        {
            DrawTechFrame(S9Cyan);
            var draw = ImGui.GetWindowDrawList();
            var codeMin = ImGui.GetWindowPos() + new Vector2(32f, 29f);
            var codeMax = ImGui.GetWindowPos() + new Vector2(cardWidth - 48f, 65f);
            draw.AddRectFilled(codeMin, codeMax, U32(new Vector4(0.08f, 0.10f, 0.20f, 1f)), 8f);

            ImGui.SetCursorPos(new Vector2(12f, 8f));
            ImGui.TextDisabled(permanentGroupCode ? "SESSION CODE  (GROUP)" : "SESSION CODE  (FOR GUESTS)");
            var display = string.IsNullOrWhiteSpace(code) ? "------" : code.Trim().ToUpperInvariant();
            ImGui.SetWindowFontScale(1.62f);
            var codeSize = ImGui.CalcTextSize(display);
            var codeAreaCenter = (32f + cardWidth - 48f) * 0.5f;
            ImGui.SetCursorPos(new Vector2(Math.Max(12f, codeAreaCenter - codeSize.X * 0.5f), 33f));
            ImGui.TextUnformatted(display);
            ImGui.SetWindowFontScale(1f);

            ImGui.SetCursorPos(new Vector2(cardWidth - 43f, 31f));
            if (DrawSessionIconButton("CopyActiveSessionCode", UiIcon.Copy, new Vector2(32f, 32f)))
                ImGui.SetClipboardText(code);

            ImGui.SetWindowFontScale(0.78f);
            var footer = FitTextToWidth("Share this code to let others join this session.", cardWidth - 20f);
            var footerSize = ImGui.CalcTextSize(footer);
            ImGui.SetCursorPos(new Vector2(Math.Max(10f, (cardWidth - footerSize.X) * 0.5f), 72f));
            ImGui.TextDisabled(footer);
            ImGui.SetWindowFontScale(1f);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneHostSessionContainer()
    {
        var activeGroup = !string.IsNullOrWhiteSpace(_config.ActiveRoomId)
            ? _config.PrismRooms.FirstOrDefault(x => x.IsHost && string.Equals(x.RoomId, _config.ActiveRoomId, StringComparison.OrdinalIgnoreCase))
            : null;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        var height = _session.Mode == PrismMode.Hosting ? 345f : _session.Mode == PrismMode.Viewing ? 174f : (_showCreateRoom ? 284f : 178f);
        if (ImGui.BeginChild("##HostSessionContainer", new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            DrawTechFrame(_session.Mode == PrismMode.Hosting ? AccentHover : S9Blue);
            BeginSessionCardContent();

            if (_session.Mode == PrismMode.Hosting)
            {
                var viewers = ViewerPresenceRegistry.GetViewerNames();
                DrawSessionPanelHeading("You are hosting", UiIcon.Crown, AccentHover,
                    $"{Math.Max(1, viewers.Count + 1)} watching");
                ImGui.SetWindowFontScale(1.12f);
                ImGui.TextUnformatted(activeGroup?.Name ?? "Watch Party");
                ImGui.SetWindowFontScale(1f);

                var info = _video.ReadInfo();
                DrawSessionMediaHero(CurrentMediaTitle(), info);
                DrawCompactSessionTimeline(info);
                ImGui.Spacing();

                var contentWidth = SessionContentWidth();
                var half = (contentWidth - 8f) * 0.5f;
                PushTechButton();
                if (ImGui.Button($"{(info.Paused ? "RESUME" : "PAUSE")}##SessionHost", new Vector2(half, 36f)))
                    _session.PauseHost(!info.Paused);
                PopTechButton();
                ImGui.SameLine(0, 8f);
                PushDangerButton();
                if (ImGui.Button("END SESSION", new Vector2(half, 36f)))
                    RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
                ImGui.Spacing();
                DrawSessionCodeCard(activeGroup?.InviteCode ?? _session.TemporaryCode, activeGroup is not null);
            }
            else if (_session.Mode == PrismMode.Viewing)
            {
                var viewingGroup = !string.IsNullOrWhiteSpace(_activeJoinedRoomId)
                    ? _config.PrismRooms.FirstOrDefault(x => string.Equals(x.RoomId, _activeJoinedRoomId, StringComparison.OrdinalIgnoreCase))
                    : null;
                DrawSessionPanelHeading("Connected to session", UiIcon.People, Good,
                    viewingGroup is null ? "Watch Party" : "Group");
                ImGui.SetWindowFontScale(1.10f);
                ImGui.TextUnformatted(viewingGroup?.Name ?? "Watch Party");
                ImGui.SetWindowFontScale(1f);
                ImGui.TextDisabled(TrimForDisplay(_session.ViewerState?.Title ?? "PrismCast session", 48));
                ImGui.Spacing();
                var contentWidth = SessionContentWidth();
                var half = (contentWidth - 8f) * 0.5f;
                PushTechButton();
                if (ImGui.Button("OPEN REMOTE", new Vector2(half, 36f)))
                    SelectPage(Page.RemoteControl);
                PopTechButton();
                ImGui.SameLine(0, 8f);
                PushDangerButton();
                if (ImGui.Button("LEAVE SESSION", new Vector2(half, 36f)))
                    RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
            }
            else
            {
                DrawSessionPanelHeading("Host a session", UiIcon.Crown, AccentHover);
                ImGui.TextDisabled("Choose how the next movie or show you play from Library will be shared.");
                ImGui.Spacing();
                var contentWidth = SessionContentWidth();
                var half = (contentWidth - 8f) * 0.5f;
                var watchSelected = string.IsNullOrWhiteSpace(_config.ActiveRoomId);
                PushTechButton(watchSelected && !_showCreateRoom);
                if (ImGui.Button("HOST WATCH PARTY", new Vector2(half, 40f)))
                {
                    _config.ActiveRoomId = "";
                    _showCreateRoom = false;
                    SaveConfig();
                    _uiStatus = "Next media will start a one-time Watch Party.";
                }
                PopTechButton();
                ImGui.SameLine(0, 8f);
                PushTechButton(_showCreateRoom);
                if (ImGui.Button("CREATE GROUP", new Vector2(half, 40f)))
                    _showCreateRoom = !_showCreateRoom;
                PopTechButton();

                if (activeGroup is not null && !_showCreateRoom)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, S9Cyan);
                    ImGui.TextWrapped($"NEXT SESSION: {activeGroup.Name}");
                    ImGui.PopStyleColor();
                }
                else if (!_showCreateRoom)
                {
                    ImGui.TextDisabled("A fresh guest code will appear here after playback starts.");
                }

                if (_showCreateRoom)
                {
                    ImGui.Spacing();
                    DrawSessionCodeInput("NewGroupName", ref _newRoomName, "Name your group...", UiIcon.People);
                    ImGui.Spacing();
                    PushTechButton(true);
                    if (ImGui.Button("CREATE PERMANENT GROUP", new Vector2(contentWidth, 36f)))
                    {
                        if (string.IsNullOrWhiteSpace(_newRoomName))
                            _uiStatus = "Error: Enter a group name first.";
                        else
                        {
                            _uiStatus = "Creating group...";
                            RunUiTask(CreateGroupAsync);
                        }
                    }
                    PopTechButton();

                    if (!string.IsNullOrWhiteSpace(_uiStatus) &&
                        (_uiStatus.Contains("group", StringComparison.OrdinalIgnoreCase) ||
                         _uiStatus.Contains("directory", StringComparison.OrdinalIgnoreCase)))
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text,
                            _uiStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? Danger : Muted);
                        ImGui.TextWrapped(TrimForDisplay(_uiStatus, 62));
                        ImGui.PopStyleColor();
                    }
                }
            }
            EndSessionCardContent();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneJoinSessionContainer()
    {
        var showJoinError = _uiStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##JoinWatchOrGroup", new Vector2(0, showJoinError ? 160f : 132f), true, ImGuiWindowFlags.NoScrollbar))
        {
            DrawTechFrame(AccentHover);
            BeginSessionCardContent(9f);
            var headingOrigin = ImGui.GetCursorScreenPos();
            var headingWidth = SessionContentWidth();
            ImGui.GetWindowDrawList().AddText(headingOrigin + new Vector2(0, 2f), U32(S9Cyan), "JOIN A SESSION");
            var peopleCenter = headingOrigin + new Vector2(headingWidth - 11f, 10f);
            ImGui.GetWindowDrawList().AddCircle(peopleCenter, 11f, U32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.78f)), 20, 1.2f);
            DrawUiIcon(UiIcon.People, peopleCenter, 14f, new Vector4(0.78f, 0.75f, 1f, 1f), 1.4f);
            ImGui.Dummy(new Vector2(headingWidth, 23f));

            var rowWidth = Math.Clamp(headingWidth * 0.67f, 230f, 320f);
            const float pasteWidth = 40f;
            const float rowGap = 8f;
            var inputWidth = rowWidth - pasteWidth - rowGap;
            ImGui.SetCursorPosX(CenteredSessionX(rowWidth, headingWidth));
            if (DrawSessionIconButton("PasteSessionCode", UiIcon.Copy, new Vector2(pasteWidth, 36f)))
            {
                var copied = (ImGui.GetClipboardText() ?? "").Trim();
                _invite = copied.Length > 63 ? copied[..63] : copied;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Paste copied session code");
            ImGui.SameLine(0, rowGap);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 8f));
            var submitted = ImGui.InputTextWithHint("##WatchOrGroupCode", "Enter session code...", ref _invite, 64,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.PopStyleVar();
            ImGui.Spacing();

            var joinWidth = headingWidth * 0.5f;
            ImGui.SetCursorPosX(CenteredSessionX(joinWidth, headingWidth));
            PushTechButton(true);
            if (ImGui.Button("JOIN BY CODE", new Vector2(joinWidth, 37f)) || submitted)
                BeginWatchPartyOrGroupJoin(_invite);
            PopTechButton();
            if (showJoinError)
            {
                ImGui.SetCursorPosX(SessionCardInset);
                ImGui.PushStyleColor(ImGuiCol.Text, Danger);
                ImGui.TextWrapped(TrimForDisplay(_uiStatus, 78));
                ImGui.PopStyleColor();
            }
            EndSessionCardContent();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneGroupsContainer()
    {
        var remaining = Math.Max(140f, ImGui.GetContentRegionAvail().Y);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##YourGroupsContainer", new Vector2(0, remaining), true))
        {
            DrawTechFrame();
            BeginSessionCardContent(9f);
            DrawSessionPanelHeading("Your groups", UiIcon.People, S9Cyan);

            var headerX = ImGui.GetCursorPosX();
            var headerY = ImGui.GetCursorPosY();
            ImGui.SetCursorPos(new Vector2(Math.Max(SessionCardInset, ImGui.GetWindowWidth() - SessionCardInset - 95f), 7f));
            if (ImGui.Button("REFRESH##Groups", new Vector2(66f, 24f)))
            {
                _nextGroupRefresh = DateTime.UtcNow.AddSeconds(10);
                RunUiTask(RefreshGroupStatusesAsync);
            }
            ImGui.SameLine(0, 5f);
            if (DrawSessionIconButton("CreateGroupShortcut", UiIcon.Plus, new Vector2(24f, 24f), _showCreateRoom))
                _showCreateRoom = true;
            ImGui.SetCursorPos(new Vector2(headerX, headerY));

            if (_config.PrismRooms.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Groups you create or join will appear here. Use + to create your first group.");
            }
            else
            {
                foreach (var group in _config.PrismRooms.ToArray())
                {
                    ImGui.Spacing();
                    DrawGroupRow(group);
                }
            }
            EndSessionCardContent();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawGroupRow(PrismRoomBookmark group)
    {
        var status = _roomStatuses.FirstOrDefault(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
        var locallyLive = group.IsHost && _session.Mode == PrismMode.Hosting &&
                          string.Equals(_config.ActiveRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase);
        var live = locallyLive || status?.Live == true;
        var expanded = string.Equals(_manageRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase);
        var width = Math.Max(120f, SessionContentWidth());
        var size = new Vector2(width, 68f);
        var origin = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##GroupSummary{group.RoomId}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var fill = expanded
            ? new Vector4(0.105f, 0.070f, 0.185f, 1f)
            : hovered ? new Vector4(0.080f, 0.075f, 0.145f, 1f) : new Vector4(0.035f, 0.045f, 0.085f, 1f);
        draw.AddRectFilled(origin, origin + size, U32(fill), 9f);
        draw.AddRect(origin, origin + size, U32(live ? AccentHover : S9Blue), 9f, ImDrawFlags.None, expanded ? 2f : 1.1f);
        draw.AddLine(origin + new Vector2(9f, 1f), origin + new Vector2(Math.Min(width - 9f, 46f), 1f), U32(S9Cyan), 2f);

        var avatarMin = origin + new Vector2(7f, 7f);
        var avatarSize = new Vector2(50f, 54f);
        draw.AddRectFilled(avatarMin, avatarMin + avatarSize,
            U32(group.IsHost ? new Vector4(0.24f, 0.09f, 0.43f, 1f) : new Vector4(0.06f, 0.18f, 0.30f, 1f)), 7f);
        draw.AddRect(avatarMin, avatarMin + avatarSize, U32(group.IsHost ? AccentHover : S9Cyan), 7f);
        DrawPrismGlyph(avatarMin + avatarSize * 0.5f, 14f);

        var titleX = origin.X + 67f;
        if (group.IsHost)
        {
            DrawUiIcon(UiIcon.Crown, new Vector2(titleX + 7f, origin.Y + 19f), 14f, new Vector4(0.98f, 0.79f, 0.24f, 1f), 1.7f);
            titleX += 18f;
        }
        var rightReserve = 112f;
        var title = FitTextToWidth(group.Name, Math.Max(60f, origin.X + width - rightReserve - titleX));
        draw.AddText(new Vector2(titleX, origin.Y + 10f), U32(Vector4.One), title);

        var subtitle = live
            ? $"Currently hosting · {TrimForDisplay(status?.Title ?? CurrentMediaTitle(), 25)}"
            : group.IsHost ? "Hosted by you" : "No active session";
        subtitle = FitTextToWidth(subtitle, Math.Max(60f, width - 146f));
        draw.AddCircleFilled(new Vector2(origin.X + 70f, origin.Y + 47f), 4f, U32(live ? Good : Muted), 12);
        draw.AddText(new Vector2(origin.X + 79f, origin.Y + 38f), U32(live ? Good : Muted), subtitle);

        var memberCount = status?.MemberCount ?? 1;
        var memberText = memberCount == 1 ? "1 MEMBER" : $"{memberCount} MEMBERS";
        var memberSize = ImGui.CalcTextSize(memberText);
        draw.AddText(new Vector2(origin.X + width - memberSize.X - 23f, origin.Y + 11f), U32(Muted), memberText);
        var chevron = new Vector2(origin.X + width - 12f, origin.Y + 43f);
        if (expanded)
        {
            draw.AddLine(chevron - new Vector2(5f, 3f), chevron + new Vector2(0, 3f), U32(S9Cyan), 1.8f);
            draw.AddLine(chevron + new Vector2(0, 3f), chevron + new Vector2(5f, -3f), U32(S9Cyan), 1.8f);
        }
        else
        {
            DrawUiIcon(UiIcon.ChevronRight, chevron, 13f, S9Cyan, 1.8f);
        }

        if (pressed)
        {
            _manageRoomId = expanded ? "" : group.RoomId;
            _pendingLeaveGroupId = "";
            if (!expanded)
                RunUiTask(RefreshGroupStatusesAsync);
        }

        if (!expanded)
            return;

        ImGui.Spacing();
        DrawExpandedGroupPanel(group, status, live, locallyLive);
    }

    private void DrawExpandedGroupPanel(PrismRoomBookmark group, PrismRoomInfo? status, bool live, bool locallyLive)
    {
        var members = status?.Members ?? [];
        var confirmingLeave = string.Equals(_pendingLeaveGroupId, group.RoomId, StringComparison.OrdinalIgnoreCase);
        var baseHeight = group.IsHost ? 166f : 126f;
        var height = Math.Clamp(baseHeight + members.Length * 24f + (confirmingLeave ? 52f : 0f), 190f, 340f);
        var panelWidth = SessionContentWidth();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.035f, 0.045f, 0.085f, 0.99f));
        if (ImGui.BeginChild($"##ExpandedGroup{group.RoomId}", new Vector2(panelWidth, height), true))
        {
            DrawTechFrame(live ? AccentHover : S9Cyan);
            BeginSessionCardContent(9f);
            DrawSessionPanelHeading("Group details", UiIcon.People, S9Cyan,
                live ? "Live now" : "Offline");

            var contentWidth = SessionContentWidth();
            var half = (contentWidth - 8f) * 0.5f;
            if (group.IsHost)
            {
                if (locallyLive)
                {
                    PushTechButton(true);
                    if (ImGui.Button($"OPEN REMOTE##GroupDetails{group.RoomId}", new Vector2(half, 32f)))
                        SelectPage(Page.RemoteControl);
                    PopTechButton();
                }
                else
                {
                    var selected = string.Equals(_config.ActiveRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase);
                    PushTechButton(selected);
                    if (ImGui.Button($"{(selected ? "HOST NEXT SELECTED" : "HOST NEXT")}##GroupDetails{group.RoomId}", new Vector2(half, 32f)))
                    {
                        _config.ActiveRoomId = group.RoomId;
                        SaveConfig();
                        _uiStatus = $"Next media will host in {group.Name}.";
                    }
                    PopTechButton();
                }
                ImGui.SameLine(0, 8f);
                if (ImGui.Button($"COPY INVITE##GroupDetails{group.RoomId}", new Vector2(half, 32f)))
                    ImGui.SetClipboardText(group.InviteCode);

                ImGui.Spacing();
                var deleteWidth = contentWidth * 0.5f;
                ImGui.SetCursorPosX(CenteredSessionX(deleteWidth, contentWidth));
                ImGui.BeginDisabled(locallyLive);
                PushDangerButton();
                if (ImGui.Button($"{(locallyLive ? "END SESSION TO DELETE" : "DELETE GROUP")}##GroupDetails{group.RoomId}",
                        new Vector2(deleteWidth, 32f)) && !locallyLive)
                    _pendingLeaveGroupId = group.RoomId;
                PopDangerButton();
                ImGui.EndDisabled();
            }
            else
            {
                if (live && !string.IsNullOrWhiteSpace(status?.SessionInviteCode))
                {
                    PushTechButton(true);
                    if (ImGui.Button($"JOIN LIVE##GroupDetails{group.RoomId}", new Vector2(half, 32f)))
                        JoinGroupLive(status!);
                    PopTechButton();
                }
                else
                {
                    ImGui.BeginDisabled(true);
                    ImGui.Button($"GROUP OFFLINE##GroupDetails{group.RoomId}", new Vector2(half, 32f));
                    ImGui.EndDisabled();
                }

                ImGui.SameLine(0, 8f);
                PushDangerButton();
                if (ImGui.Button($"LEAVE GROUP##GroupDetails{group.RoomId}", new Vector2(half, 32f)))
                    _pendingLeaveGroupId = group.RoomId;
                PopDangerButton();
            }

            if (confirmingLeave)
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, Danger);
                ImGui.PushTextWrapPos(SessionCardInset + contentWidth);
                ImGui.TextWrapped(group.IsHost
                    ? $"Delete {group.Name}? This removes the group for every member."
                    : $"Leave {group.Name}? You will need a new invite to return.");
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
                var confirmWidth = (contentWidth - 8f) * 0.5f;
                PushDangerButton();
                if (ImGui.Button($"{(group.IsHost ? "CONFIRM DELETE" : "CONFIRM LEAVE")}##{group.RoomId}", new Vector2(confirmWidth, 30f)))
                {
                    _pendingLeaveGroupId = "";
                    RunUiTask(() => group.IsHost ? DeleteGroupAsync(group) : LeaveGroupAsync(group));
                }
                PopDangerButton();
                ImGui.SameLine(0, 8f);
                if (ImGui.Button($"CANCEL##Leave{group.RoomId}", new Vector2(confirmWidth, 30f)))
                    _pendingLeaveGroupId = "";
            }

            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.68f, 0.80f, 1f, 1f));
            ImGui.TextUnformatted("MEMBERS");
            ImGui.PopStyleColor();
            var separatorOrigin = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(separatorOrigin,
                separatorOrigin + new Vector2(contentWidth, 0), U32(new Vector4(S9PanelLine.X, S9PanelLine.Y, S9PanelLine.Z, 0.62f)), 1f);
            ImGui.Dummy(new Vector2(contentWidth, 2f));

            if (members.Length == 0)
            {
                ImGui.TextDisabled("Refresh to load this group's member list.");
            }
            else
            {
                foreach (var member in members.OrderByDescending(x => x.IsHost)
                             .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, member.IsHost ? new Vector4(0.98f, 0.79f, 0.24f, 1f) : Vector4.One);
                    ImGui.TextUnformatted($"●  {member.FirstName}");
                    ImGui.PopStyleColor();
                    if (member.IsHost)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("HOST");
                    }
                    else if (group.IsHost)
                    {
                        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 10f,
                            ImGui.GetWindowWidth() - SessionCardInset - 68f));
                        PushDangerButton();
                        if (ImGui.SmallButton($"REMOVE##{group.RoomId}{member.ViewerId}"))
                            RunUiTask(() => KickGroupMemberAsync(group, member.ViewerId));
                        PopDangerButton();
                    }
                }
            }
            EndSessionCardContent();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void MaybeRefreshGroupStatuses()
    {
        if (DateTime.UtcNow < _nextGroupRefresh || _config.PrismRooms.Count == 0)
            return;
        _nextGroupRefresh = DateTime.UtcNow.AddSeconds(12);
        RunUiTask(RefreshGroupStatusesAsync);
    }

    private async Task RefreshGroupStatusesAsync()
    {
        var result = new List<PrismRoomInfo>();
        foreach (var group in _config.PrismRooms.ToArray())
        {
            try
            {
                var status = await _relay.ResolveRoomAsync(DirectoryUrl, group.RoomId,
                    _config.RoomClientId, CancellationToken.None).ConfigureAwait(false);
                if (status is not null)
                    result.Add(status);
            }
            catch
            {
                // One stale/deleted group must not prevent the rest of the saved list from refreshing.
            }
        }
        _roomStatuses = result;
    }

    private async Task CreateGroupAsync()
    {
        var name = _newRoomName.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Enter a group name first.");

        PrismRoomInfo group;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            try
            {
                group = await _relay.CreateRoomAsync(DirectoryUrl, RelayClient.EnsureSecret(_config),
                    name, true, _config.RoomClientId, LocalFirstName(), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("PrismCast's code/group directory did not respond. Group creation requires the built-in directory service to be online.");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"PrismCast's code/group directory is unavailable: {ex.Message}");
            }
        }

        await _framework.Run(() =>
        {
            var existing = _config.PrismRooms.FirstOrDefault(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _config.PrismRooms.Add(new PrismRoomBookmark
                {
                    RoomId = group.RoomId,
                    Name = group.Name,
                    IsPrivate = true,
                    IsHost = true,
                    InviteCode = group.InviteCode,
                });
            }
            _config.ActiveRoomId = group.RoomId;
            _newRoomName = "";
            _showCreateRoom = false;
            SaveConfig();
            _uiStatus = $"Created {group.Name}. Permanent invite: {group.InviteCode}";
        }).ConfigureAwait(false);
        await RefreshGroupStatusesAsync().ConfigureAwait(false);
    }

    private void BeginWatchPartyOrGroupJoin(string rawCode)
    {
        var code = rawCode.Trim().ToUpperInvariant();
        if (code.Length == 0)
            return;

        RunUiTask(async () =>
        {
            // Backward-compatible direct invite support remains useful during alpha testing.
            if (PrismInvite.TryDecode(code, out _, out _))
            {
                await _session.JoinAsync(code).ConfigureAwait(false);
                await RegisterViewerPresenceAsync(code, LocalFirstName()).ConfigureAwait(false);
                _activeJoinedRoomId = "";
                await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
                return;
            }

            var watchInvite = await _relay.ResolveTemporaryAsync(DirectoryUrl, code, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(watchInvite))
            {
                await _session.JoinAsync(watchInvite).ConfigureAwait(false);
                await RegisterViewerPresenceAsync(watchInvite, LocalFirstName()).ConfigureAwait(false);
                _activeJoinedRoomId = "";
                await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
                return;
            }

            PrismRoomInfo group;
            try
            {
                group = await _relay.JoinRoomAsync(DirectoryUrl, code, _config.RoomClientId,
                    LocalFirstName(), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                throw new InvalidOperationException("That Watch Party or Group invite code is invalid or no longer available.");
            }

            await SaveGroupBookmarkAsync(group, false).ConfigureAwait(false);
            _invite = "";
            _uiStatus = $"Joined group: {group.Name}";
            await RefreshGroupStatusesAsync().ConfigureAwait(false);

            if (group.Live && !string.IsNullOrWhiteSpace(group.SessionInviteCode))
                await JoinGroupLiveAsync(group).ConfigureAwait(false);
        });
    }

    private async Task SaveGroupBookmarkAsync(PrismRoomInfo group, bool isHost)
    {
        await _framework.Run(() =>
        {
            var existing = _config.PrismRooms.FirstOrDefault(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _config.PrismRooms.Add(new PrismRoomBookmark
                {
                    RoomId = group.RoomId,
                    Name = group.Name,
                    IsPrivate = true,
                    IsHost = isHost,
                    InviteCode = isHost ? group.InviteCode : "",
                });
            }
            else
            {
                existing.Name = group.Name;
            }
            SaveConfig();
        }).ConfigureAwait(false);
    }

    private async Task DeleteGroupAsync(PrismRoomBookmark group)
    {
        if (!group.IsHost)
            throw new InvalidOperationException("Only the group owner can delete this group.");

        var deletedFromDirectory = await _relay.DeleteRoomAsync(DirectoryUrl, RelayClient.EnsureSecret(_config),
            group.RoomId, CancellationToken.None).ConfigureAwait(false);
        await _framework.Run(() =>
        {
            _config.PrismRooms.RemoveAll(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
            _roomStatuses.RemoveAll(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_config.ActiveRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase))
                _config.ActiveRoomId = "";
            if (string.Equals(_manageRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase))
                _manageRoomId = "";
            _pendingLeaveGroupId = "";
            SaveConfig();
            _uiStatus = deletedFromDirectory
                ? $"Deleted group: {group.Name}"
                : $"Removed group from this device: {group.Name}";
        }).ConfigureAwait(false);
    }

    private async Task LeaveGroupAsync(PrismRoomBookmark group)
    {
        if (group.IsHost)
            throw new InvalidOperationException("A group owner cannot leave their own group from this screen.");
        await _relay.LeaveRoomAsync(DirectoryUrl, group.RoomId, _config.RoomClientId, CancellationToken.None).ConfigureAwait(false);
        await _framework.Run(() =>
        {
            _config.PrismRooms.RemoveAll(x => string.Equals(x.RoomId, group.RoomId, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_activeJoinedRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase))
                _activeJoinedRoomId = "";
            if (string.Equals(_manageRoomId, group.RoomId, StringComparison.OrdinalIgnoreCase))
                _manageRoomId = "";
            if (string.Equals(_pendingLeaveGroupId, group.RoomId, StringComparison.OrdinalIgnoreCase))
                _pendingLeaveGroupId = "";
            SaveConfig();
        }).ConfigureAwait(false);
        await RefreshGroupStatusesAsync().ConfigureAwait(false);
    }

    private async Task KickGroupMemberAsync(PrismRoomBookmark group, string viewerId)
    {
        if (!group.IsHost || string.IsNullOrWhiteSpace(viewerId))
            return;
        await _relay.KickRoomMemberAsync(DirectoryUrl, RelayClient.EnsureSecret(_config), group.RoomId,
            viewerId, CancellationToken.None).ConfigureAwait(false);
        await RefreshGroupStatusesAsync().ConfigureAwait(false);
    }

    private void JoinGroupLive(PrismRoomInfo group) => RunUiTask(() => JoinGroupLiveAsync(group));

    private async Task JoinGroupLiveAsync(PrismRoomInfo group)
    {
        if (string.IsNullOrWhiteSpace(group.SessionInviteCode))
            throw new InvalidOperationException("That group is currently offline.");
        await _session.JoinAsync(group.SessionInviteCode).ConfigureAwait(false);
        await RegisterViewerPresenceAsync(group.SessionInviteCode, LocalFirstName()).ConfigureAwait(false);
        _activeJoinedRoomId = group.RoomId;
        await SaveGroupBookmarkAsync(group, false).ConfigureAwait(false);
        await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
    }

    private void SessionTabButton(SessionMobileTab tab, string label, float width)
    {
        var active = _sessionMobileTab == tab;
        PushTechButton(active);
        if (ImGui.Button($"{label}##SessionTab{tab}", new Vector2(width, 36)))
        {
            _sessionMobileTab = tab;
            if (tab == SessionMobileTab.Rooms && !_sessionRoomRefreshAttempted)
            {
                _sessionRoomRefreshAttempted = true;
                if (RelayAvailable())
                    RunUiTask(RefreshRoomStatusesAsync);
            }
        }
        PopTechButton();
    }

    private void DrawPhoneRoomsTab()
    {
        if (!RelayAvailable())
        {
            DrawSessionInfoCard("PRISMCAST RELAY REQUIRED",
                "Permanent Open and Private rooms use PrismCast Relay. Configure it once under Settings > PrismCast Relay.");
            ImGui.Spacing();
            if (ImGui.Button("OPEN RELAY SETTINGS##Rooms", new Vector2(-1, 36)))
            {
                _settingsPage = SettingsPage.Relay;
                _phoneSettingsHome = false;
                SelectPage(Page.Settings);
            }
            return;
        }

        var activeRoomId = _session.Mode == PrismMode.Hosting ? _config.ActiveRoomId : _activeJoinedRoomId;
        if (!string.IsNullOrWhiteSpace(activeRoomId) && _session.Mode != PrismMode.Idle)
        {
            var activeBookmark = _config.PrismRooms.FirstOrDefault(x =>
                string.Equals(x.RoomId, activeRoomId, StringComparison.OrdinalIgnoreCase));
            if (activeBookmark is not null)
            {
                DrawActiveRoomSession(activeBookmark);
                ImGui.Spacing();
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.78f, 1f, 1f));
        ImGui.TextUnformatted("MY ROOMS");
        ImGui.PopStyleColor();
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - 78f));
        if (ImGui.SmallButton("REFRESH##Rooms"))
            RunUiTask(RefreshRoomStatusesAsync);

        if (_config.PrismRooms.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("You have not joined any permanent rooms yet.");
        }
        else
        {
            foreach (var room in _config.PrismRooms.ToArray())
            {
                ImGui.Spacing();
                DrawSavedRoomRow(room);
            }
        }

        ImGui.Spacing();
        var half = (ImGui.GetContentRegionAvail().X - 8f) * 0.5f;
        PushTechButton(_showCreateRoom);
        if (ImGui.Button("+ CREATE ROOM", new Vector2(half, 36)))
        {
            _showCreateRoom = !_showCreateRoom;
            _showJoinPrivateRoom = false;
        }
        PopTechButton();
        ImGui.SameLine(0, 8f);
        PushTechButton(_showJoinPrivateRoom);
        if (ImGui.Button("JOIN PRIVATE", new Vector2(half, 36)))
        {
            _showJoinPrivateRoom = !_showJoinPrivateRoom;
            _showCreateRoom = false;
        }
        PopTechButton();

        if (_showCreateRoom)
            DrawCreateRoomPanel();
        else if (_showJoinPrivateRoom)
            DrawJoinPrivateRoomPanel();
    }

    private void DrawSavedRoomRow(PrismRoomBookmark room)
    {
        var status = _roomStatuses.FirstOrDefault(x =>
            string.Equals(x.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase));
        var locallyLive = room.IsHost && _session.Mode == PrismMode.Hosting &&
                          string.Equals(_config.ActiveRoomId, room.RoomId, StringComparison.OrdinalIgnoreCase);
        var live = locallyLive || status?.Live == true;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.045f, 0.050f, 0.090f, 0.99f));
        if (ImGui.BeginChild($"##RoomRow{room.RoomId}", new Vector2(0, live ? 104f : 82f), true))
        {
            DrawTechFrame(live ? Accent : S9Blue);
            ImGui.TextUnformatted(room.Name);
            ImGui.SameLine();
            ImGui.TextDisabled(room.IsPrivate ? "PRIVATE" : "OPEN");
            if (live)
            {
                ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - 54f));
                ImGui.PushStyleColor(ImGuiCol.Text, Good);
                ImGui.TextUnformatted("● LIVE");
                ImGui.PopStyleColor();
                ImGui.TextDisabled(TrimForDisplay(status?.Title ?? _session.CurrentTitle ?? "PrismCast session", 42));
            }
            else
            {
                ImGui.TextDisabled(room.IsHost ? "Hosted by you" : "Saved room");
            }

            var buttonY = live ? 64f : 44f;
            ImGui.SetCursorPosY(buttonY);
            if (live && !locallyLive && !string.IsNullOrWhiteSpace(status?.SessionInviteCode))
            {
                if (ImGui.Button($"JOIN##Room{room.RoomId}", new Vector2(76, 28)))
                    JoinRoomLive(status!);
                ImGui.SameLine();
            }
            else if (room.IsHost && _session.Mode == PrismMode.Idle)
            {
                var selected = string.Equals(_config.ActiveRoomId, room.RoomId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Button($"{(selected ? "SELECTED" : "USE ROOM")}##Room{room.RoomId}", new Vector2(86, 28)))
                {
                    _config.ActiveRoomId = room.RoomId;
                    SaveConfig();
                }
                ImGui.SameLine();
            }

            if (room.IsHost && room.IsPrivate && !string.IsNullOrWhiteSpace(room.InviteCode))
            {
                if (ImGui.Button($"COPY INVITE##Room{room.RoomId}", new Vector2(100, 28)))
                    ImGui.SetClipboardText(room.InviteCode);
                ImGui.SameLine();
            }

            if (room.IsHost)
            {
                var managing = string.Equals(_manageRoomId, room.RoomId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Button($"{(managing ? "CLOSE" : "MEMBERS")}##RoomMembers{room.RoomId}", new Vector2(78, 28)))
                    _manageRoomId = managing ? "" : room.RoomId;
            }
            else
            {
                PushDangerButton();
                if (ImGui.Button($"LEAVE##Room{room.RoomId}", new Vector2(66, 28)))
                    RunUiTask(() => LeaveRoomAsync(room));
                PopDangerButton();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (room.IsHost && string.Equals(_manageRoomId, room.RoomId, StringComparison.OrdinalIgnoreCase))
            DrawRoomMemberManager(room, status);
    }

    private void DrawRoomMemberManager(PrismRoomBookmark room, PrismRoomInfo? status)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.040f, 0.045f, 0.082f, 0.99f));
        if (ImGui.BeginChild($"##RoomMemberManager{room.RoomId}", new Vector2(0, 132), true))
        {
            DrawTechFrame(S9Cyan);
            ImGui.TextUnformatted("ROOM MEMBERS");
            ImGui.TextDisabled("Permanent access remains until a member leaves or the host removes them.");
            ImGui.Separator();

            var members = status?.Members ?? [];
            if (members.Length == 0)
            {
                ImGui.TextDisabled("Refresh the room to load its member list.");
            }
            else
            {
                foreach (var member in members.OrderByDescending(x => x.IsHost).ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase))
                {
                    ImGui.TextUnformatted(member.FirstName);
                    if (member.IsHost)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("HOST");
                    }
                    else
                    {
                        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 10f, ImGui.GetWindowWidth() - 72f));
                        PushDangerButton();
                        if (ImGui.SmallButton($"KICK##{room.RoomId}{member.ViewerId}"))
                            RunUiTask(() => KickRoomMemberAsync(room, member.ViewerId));
                        PopDangerButton();
                    }
                }
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawCreateRoomPanel()
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##CreateRoomPanel", new Vector2(0, 160), true))
        {
            DrawTechFrame();
            ImGui.TextUnformatted("CREATE PERMANENT ROOM");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##RoomName", "Room name...", ref _newRoomName, 40);
            var half = (ImGui.GetContentRegionAvail().X - 8f) * 0.5f;
            PushTechButton(!_newRoomPrivate);
            if (ImGui.Button("OPEN", new Vector2(half, 30))) _newRoomPrivate = false;
            PopTechButton();
            ImGui.SameLine(0, 8f);
            PushTechButton(_newRoomPrivate);
            if (ImGui.Button("PRIVATE", new Vector2(half, 30))) _newRoomPrivate = true;
            PopTechButton();
            ImGui.TextDisabled(_newRoomPrivate ? "Private rooms require an invite code the first time someone joins." : "Open rooms can be joined through Nearby when active.");
            PushTechButton();
            if (ImGui.Button("CREATE ROOM", new Vector2(-1, 32)))
                RunUiTask(CreateRoomAsync);
            PopTechButton();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawJoinPrivateRoomPanel()
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##JoinPrivateRoomPanel", new Vector2(0, 116), true))
        {
            DrawTechFrame();
            ImGui.TextUnformatted("JOIN PRIVATE ROOM");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##PrivateRoomCode", "Permanent room invite code...", ref _roomJoinCode, 64);
            PushTechButton();
            if (ImGui.Button("JOIN ROOM", new Vector2(-1, 32)))
                RunUiTask(JoinPrivateRoomAsync);
            PopTechButton();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawActiveRoomSession(PrismRoomBookmark room)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.050f, 0.115f, 0.99f));
        if (ImGui.BeginChild("##ActiveRoomSession", new Vector2(0, 166), true))
        {
            DrawTechFrame(AccentHover);
            ImGui.PushStyleColor(ImGuiCol.Text, Good);
            ImGui.TextUnformatted("● ROOM LIVE");
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1.12f);
            ImGui.TextUnformatted(room.Name);
            ImGui.SetWindowFontScale(1f);
            ImGui.TextDisabled(TrimForDisplay(CurrentMediaTitle(), 44));

            var players = ActiveRoomPlayerNames();
            ImGui.Spacing();
            ImGui.TextUnformatted($"WATCHING  {players.Count}");
            if (players.Count > 0)
            {
                ImGui.TextWrapped(string.Join("   •   ", players.Take(6)));
            }
            ImGui.Spacing();
            if (_session.Mode == PrismMode.Viewing)
            {
                if (ImGui.Button("OPEN REMOTE", new Vector2(120, 30))) SelectPage(Page.RemoteControl);
                ImGui.SameLine();
                PushDangerButton();
                if (ImGui.Button("LEAVE CAST", new Vector2(110, 30))) RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
            }
            else if (_session.Mode == PrismMode.Hosting)
            {
                PushDangerButton();
                if (ImGui.Button("END CAST", new Vector2(110, 30))) RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneTemporaryTab()
    {
        if (_session.Mode == PrismMode.Hosting && !string.IsNullOrWhiteSpace(_config.ActiveRoomId))
        {
            var room = _config.PrismRooms.FirstOrDefault(x =>
                string.Equals(x.RoomId, _config.ActiveRoomId, StringComparison.OrdinalIgnoreCase));
            DrawSessionInfoCard("ROOM CAST ACTIVE", $"This cast belongs to {room?.Name ?? "a permanent room"}. End it before switching to a temporary session.");
            return;
        }

        if (_session.Mode == PrismMode.Hosting)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.050f, 0.115f, 0.99f));
            if (ImGui.BeginChild("##TemporaryHostCard", new Vector2(0, 220), true))
            {
                DrawTechFrame(AccentHover);
                DrawSectionHeading("Temporary Session", "// ONE TIME");
                ImGui.TextDisabled("A new code is generated for every temporary cast.");
                var code = RelayAvailable() ? _session.TemporaryCode : _session.InviteCode;
                ImGui.Spacing();
                ImGui.SetWindowFontScale(1.55f);
                var size = ImGui.CalcTextSize(code);
                ImGui.SetCursorPosX(Math.Max(0f, (ImGui.GetContentRegionAvail().X - size.X) * 0.5f));
                ImGui.TextUnformatted(code);
                ImGui.SetWindowFontScale(1f);
                ImGui.TextDisabled(RelayAvailable() ? "Session code expires when this cast ends." : "Relay is not configured, so this is a direct invite code.");
                if (ImGui.Button("COPY CODE", new Vector2(-1, 32))) ImGui.SetClipboardText(code);
                ImGui.Spacing();
                PushDangerButton();
                if (ImGui.Button("END SESSION", new Vector2(-1, 34))) RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            return;
        }

        if (_session.Mode == PrismMode.Viewing)
        {
            DrawSessionInfoCard("CONNECTED", _session.ViewerState?.Title ?? "Temporary PrismCast session");
            ImGui.Spacing();
            PushDangerButton();
            if (ImGui.Button("LEAVE SESSION", new Vector2(-1, 36))) RunUiTask(StopSessionFromUiAsync);
            PopDangerButton();
            return;
        }

        DrawSessionInfoCard("TEMPORARY SESSION", "One-time sessions generate a fresh code every time you start a cast from Library.");
        ImGui.Spacing();
        if (!string.IsNullOrWhiteSpace(_config.ActiveRoomId))
        {
            PushTechButton();
            if (ImGui.Button("USE TEMPORARY FOR NEXT CAST", new Vector2(-1, 36)))
            {
                _config.ActiveRoomId = "";
                SaveConfig();
            }
            PopTechButton();
            ImGui.Spacing();
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild("##TemporaryJoin", new Vector2(0, 132), true))
        {
            DrawTechFrame();
            ImGui.TextUnformatted("JOIN BY SESSION CODE");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##TemporaryCode", "Enter session code...", ref _invite, 4096);
            PushTechButton();
            if (ImGui.Button("JOIN SESSION", new Vector2(-1, 34)))
                BeginDirectLobbyJoin(_invite);
            PopTechButton();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPhoneNearbyTab()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.78f, 1f, 1f));
        ImGui.TextUnformatted("NEARBY SESSIONS");
        ImGui.PopStyleColor();
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - 78f));
        if (ImGui.SmallButton("REFRESH##Nearby"))
        {
            if (RelayAvailable())
            {
                var names = NearbyPlayerNames();
                RunUiTask(() => RefreshNearbyAsync(names));
            }
        }

        ImGui.TextDisabled("Only active PrismCast hosts currently loaded in your FFXIV zone are checked. No notifications are sent.");
        ImGui.Spacing();

        if (!RelayAvailable())
        {
            DrawSessionInfoCard("PRISMCAST RELAY REQUIRED", "Nearby discovery uses PrismCast Relay. Configure it once under Settings > PrismCast Relay.");
            ImGui.Spacing();
            if (ImGui.Button("OPEN RELAY SETTINGS##Nearby", new Vector2(-1, 36)))
            {
                _settingsPage = SettingsPage.Relay;
                _phoneSettingsHome = false;
                SelectPage(Page.Settings);
            }
            return;
        }

        if (_nearbySessions.Count == 0)
        {
            DrawSessionInfoCard("NO NEARBY SESSIONS", "Refresh while other PrismCast hosts are nearby. Private rooms are never advertised here.");
            return;
        }

        foreach (var nearby in _nearbySessions)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.045f, 0.050f, 0.090f, 0.99f));
            if (ImGui.BeginChild($"##Nearby{nearby.CharacterKey}", new Vector2(0, 92), true))
            {
                DrawTechFrame(S9Cyan);
                ImGui.PushStyleColor(ImGuiCol.Text, Good);
                ImGui.TextUnformatted("● ACTIVE");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.TextUnformatted(nearby.HostName);
                ImGui.TextDisabled(string.Equals(nearby.SessionKind, "room", StringComparison.OrdinalIgnoreCase)
                    ? nearby.RoomName
                    : "Temporary Session");
                ImGui.TextDisabled(TrimForDisplay(nearby.Title, 44));
                ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 8f, ImGui.GetWindowWidth() - 76f));
                if (ImGui.Button($"JOIN##Nearby{nearby.CharacterKey}", new Vector2(62, 28)))
                    JoinNearbySession(nearby);
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawSessionInfoCard(string title, string body)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.050f, 0.055f, 0.100f, 0.99f));
        if (ImGui.BeginChild($"##SessionInfo{title}", new Vector2(0, 100), true))
        {
            DrawTechFrame();
            ImGui.TextUnformatted(title);
            ImGui.TextDisabled(body);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private bool RelayAvailable() => !string.IsNullOrWhiteSpace(_config.RelayBaseUrl);

    private async Task RefreshRoomStatusesAsync()
    {
        var result = new List<PrismRoomInfo>();
        foreach (var room in _config.PrismRooms.ToArray())
        {
            var status = await _relay.ResolveRoomAsync(_config.RelayBaseUrl, room.RoomId,
                _config.RoomClientId, CancellationToken.None).ConfigureAwait(false);
            if (status is not null)
                result.Add(status);
        }
        _roomStatuses = result;
    }

    private async Task CreateRoomAsync()
    {
        var name = _newRoomName.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Enter a room name first.");
        if (!RelayAvailable())
            throw new InvalidOperationException("Configure PrismCast Relay under Settings > PrismCast Relay first.");

        var room = await _relay.CreateRoomAsync(_config.RelayBaseUrl, RelayClient.EnsureSecret(_config),
            name, _newRoomPrivate, _config.RoomClientId, LocalFirstName(), CancellationToken.None)
            .ConfigureAwait(false);

        await _framework.Run(() =>
        {
            if (!_config.PrismRooms.Any(x => string.Equals(x.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase)))
            {
                _config.PrismRooms.Add(new PrismRoomBookmark
                {
                    RoomId = room.RoomId,
                    Name = room.Name,
                    IsPrivate = room.IsPrivate,
                    IsHost = true,
                    InviteCode = room.InviteCode,
                });
            }
            _config.ActiveRoomId = room.RoomId;
            _newRoomName = "";
            _showCreateRoom = false;
            SaveConfig();
        }).ConfigureAwait(false);
        await RefreshRoomStatusesAsync().ConfigureAwait(false);
    }

    private async Task JoinPrivateRoomAsync()
    {
        var code = _roomJoinCode.Trim();
        if (code.Length == 0)
            throw new InvalidOperationException("Enter a private-room invite code.");
        var room = await _relay.JoinRoomAsync(_config.RelayBaseUrl, code, _config.RoomClientId,
            LocalFirstName(), CancellationToken.None).ConfigureAwait(false);
        await SaveRoomBookmarkAsync(room, false).ConfigureAwait(false);
        _roomJoinCode = "";
        _showJoinPrivateRoom = false;
        await RefreshRoomStatusesAsync().ConfigureAwait(false);
        if (room.Live && !string.IsNullOrWhiteSpace(room.SessionInviteCode))
            await JoinRoomLiveAsync(room).ConfigureAwait(false);
    }

    private async Task SaveRoomBookmarkAsync(PrismRoomInfo room, bool isHost)
    {
        await _framework.Run(() =>
        {
            var existing = _config.PrismRooms.FirstOrDefault(x =>
                string.Equals(x.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _config.PrismRooms.Add(new PrismRoomBookmark
                {
                    RoomId = room.RoomId,
                    Name = room.Name,
                    IsPrivate = room.IsPrivate,
                    IsHost = isHost,
                    InviteCode = isHost ? room.InviteCode : "",
                });
            }
            SaveConfig();
        }).ConfigureAwait(false);
    }

    private async Task LeaveRoomAsync(PrismRoomBookmark room)
    {
        if (room.IsHost)
            throw new InvalidOperationException("The host cannot leave their own room from this screen.");
        await _relay.LeaveRoomAsync(_config.RelayBaseUrl, room.RoomId, _config.RoomClientId,
            CancellationToken.None).ConfigureAwait(false);
        await _framework.Run(() =>
        {
            _config.PrismRooms.RemoveAll(x => string.Equals(x.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_config.ActiveRoomId, room.RoomId, StringComparison.OrdinalIgnoreCase))
                _config.ActiveRoomId = "";
            SaveConfig();
        }).ConfigureAwait(false);
        await RefreshRoomStatusesAsync().ConfigureAwait(false);
    }

    private async Task KickRoomMemberAsync(PrismRoomBookmark room, string viewerId)
    {
        if (string.IsNullOrWhiteSpace(viewerId))
            return;
        await _relay.KickRoomMemberAsync(_config.RelayBaseUrl, RelayClient.EnsureSecret(_config), room.RoomId,
            viewerId, CancellationToken.None).ConfigureAwait(false);
        await RefreshRoomStatusesAsync().ConfigureAwait(false);
    }

    private void JoinRoomLive(PrismRoomInfo room) => RunUiTask(() => JoinRoomLiveAsync(room));

    private async Task JoinRoomLiveAsync(PrismRoomInfo room)
    {
        if (string.IsNullOrWhiteSpace(room.SessionInviteCode))
            throw new InvalidOperationException("That room is not currently casting.");
        await _session.JoinAsync(room.SessionInviteCode).ConfigureAwait(false);
        await RegisterViewerPresenceAsync(room.SessionInviteCode, LocalFirstName()).ConfigureAwait(false);
        _activeJoinedRoomId = room.RoomId;
        await SaveRoomBookmarkAsync(room, false).ConfigureAwait(false);
    }

    private void BeginTemporaryJoin(string rawCode)
    {
        var code = rawCode.Trim();
        RunUiTask(async () =>
        {
            var inviteCode = code;
            if (!PrismInvite.TryDecode(inviteCode, out _, out _))
            {
                if (!RelayAvailable())
                    throw new InvalidOperationException("That is a short session code, but PrismCast Relay is not configured under Settings > PrismCast Relay.");
                inviteCode = await _relay.ResolveTemporaryAsync(_config.RelayBaseUrl, code, CancellationToken.None)
                    .ConfigureAwait(false) ?? throw new InvalidOperationException("That temporary session code is offline or invalid.");
            }
            await _session.JoinAsync(inviteCode).ConfigureAwait(false);
            await RegisterViewerPresenceAsync(inviteCode, LocalFirstName()).ConfigureAwait(false);
            _activeJoinedRoomId = "";
            SelectPage(Page.RemoteControl);
        });
    }

    private string[] NearbyPlayerNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localName = _objects.LocalPlayer?.Name.TextValue?.Trim() ?? string.Empty;
        foreach (var obj in _objects)
        {
            if (obj is null)
                continue;
            if (!obj.ObjectKind.ToString().Equals("Player", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = obj.Name.TextValue?.Trim() ?? string.Empty;
            if (name.Length == 0 || string.Equals(name, localName, StringComparison.OrdinalIgnoreCase))
                continue;
            names.Add(name);
            if (names.Count >= 48)
                break;
        }
        return names.ToArray();
    }

    private async Task RefreshNearbyAsync(string[] names)
    {
        var tasks = names.Select(name =>
            _relay.ResolveNearbyAsync(_config.RelayBaseUrl, RelayClient.CharacterKey(name), CancellationToken.None)).ToArray();
        var resolved = await Task.WhenAll(tasks).ConfigureAwait(false);
        _nearbySessions = resolved.Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x.CharacterKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.HostName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void JoinNearbySession(PrismNearbySession nearby)
    {
        RunUiTask(async () =>
        {
            if (string.Equals(nearby.SessionKind, "room", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(nearby.RoomId))
            {
                var room = await _relay.JoinRoomAsync(_config.RelayBaseUrl, nearby.RoomId,
                    _config.RoomClientId, LocalFirstName(), CancellationToken.None).ConfigureAwait(false);
                await SaveRoomBookmarkAsync(room, false).ConfigureAwait(false);
                _activeJoinedRoomId = room.RoomId;
            }
            else
            {
                _activeJoinedRoomId = "";
            }

            await _session.JoinAsync(nearby.InviteCode).ConfigureAwait(false);
            await RegisterViewerPresenceAsync(nearby.InviteCode, LocalFirstName()).ConfigureAwait(false);
            SelectPage(Page.RemoteControl);
        });
    }

    private IReadOnlyList<string> ActiveRoomPlayerNames()
    {
        var result = new List<string>();
        if (_session.Mode == PrismMode.Hosting)
        {
            result.Add(LocalFirstName());
            result.AddRange(ViewerPresenceRegistry.GetViewerNames());
        }
        else if (_session.Mode == PrismMode.Viewing)
        {
            var hostName = _session.ViewerState?.HostName ?? "";
            if (!string.IsNullOrWhiteSpace(hostName))
                result.Add(hostName);
            if (_session.ViewerState?.Viewers is { Length: > 0 } viewers)
                result.AddRange(viewers);
            if (!result.Contains(LocalFirstName(), StringComparer.OrdinalIgnoreCase))
                result.Add(LocalFirstName());
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void DrawSettings()
    {
        if (_phoneSettingsHome)
        {
            var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
            DrawSectionHeading(phone ? "Settings" : "Control Panel", phone ? "// CONTROL PANEL" : "// SETTINGS");
            ImGui.Spacing();
            DrawPhoneSettingsHub();
            return;
        }

        DrawPhoneSettingsPageHeader();
        ImGui.Spacing();
        if (ImGui.BeginChild("##ResponsiveSettingsContent",
                new Vector2(0, Math.Max(1f, ImGui.GetContentRegionAvail().Y)), false))
            DrawSettingsContent();
        ImGui.EndChild();
    }

    private void DrawPhoneSettingsHub()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var phone = EffectiveInterfaceMode() == InterfaceMode.Phone;
        const float gap = 9f;
        var columns = phone ? 3 : 4;
        var tile = phone
            ? Math.Clamp((width - gap * (columns - 1)) / columns, 96f, 132f)
            : Math.Clamp((width - gap * (columns - 1)) / columns, 120f, 174f);
        var total = tile * columns + gap * (columns - 1);
        var left = Math.Max(0f, (width - total) * 0.5f);

        var items = new (SettingsPage Page, string Title, string Subtitle, UiIcon Icon)[]
        {
            (SettingsPage.General, "General", "App & layout", UiIcon.Gear),
            (SettingsPage.Plex, "Plex", "Server & account", UiIcon.Library),
            (SettingsPage.LocalMedia, "Local Media", "Folders & scanning", UiIcon.Folder),
            (SettingsPage.Playback, "Playback", "Volume", UiIcon.Play),
            (SettingsPage.Screen, "Screen", "Movement & scale", UiIcon.Monitor),
            (SettingsPage.Networking, "Networking", "Direct sessions", UiIcon.Remote),
            (SettingsPage.Advanced, "Advanced", "Runtime status", UiIcon.AnimeSpark),
            (SettingsPage.About, "About", "Version & source", UiIcon.FilmReel),
        };

        for (var i = 0; i < items.Length; i++)
        {
            if (i % columns == 0)
                ImGui.SetCursorPosX(left);
            else
                ImGui.SameLine(0, gap);

            var item = items[i];
            if (DrawPhoneSettingsTile(item.Page, item.Title, item.Subtitle, item.Icon, new Vector2(tile, tile)))
            {
                _settingsPage = item.Page;
                _phoneSettingsHome = false;
            }
        }
    }

    private bool DrawPhoneSettingsTile(SettingsPage page, string title, string subtitle, UiIcon icon, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##PhoneSettingsTile{page}", size);
        var clicked = ImGui.IsItemClicked();
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var max = pos + size;
        var fill = hovered ? new Vector4(0.13f, 0.10f, 0.22f, 0.99f) : new Vector4(0.065f, 0.068f, 0.115f, 0.99f);
        var border = hovered ? AccentHover : S9PanelLine;
        draw.AddRectFilled(pos, max, U32(fill), 9f);
        draw.AddRect(pos, max, U32(border), 9f, ImDrawFlags.None, hovered ? 2.2f : 1.4f);
        if (hovered)
            draw.AddLine(pos + new Vector2(8f, 2f), pos + new Vector2(size.X - 8f, 2f), U32(AccentHover), 2.4f);

        var iconColor = new Vector4(0.78f, 0.72f, 1f, 1f);
        DrawUiIcon(icon, pos + new Vector2(size.X * 0.5f, 31f), 24f, iconColor, 2f);

        var titleSize = ImGui.CalcTextSize(title);
        var titleX = pos.X + Math.Max(7f, (size.X - titleSize.X) * 0.5f);
        draw.AddText(new Vector2(titleX, pos.Y + 56f), U32(Vector4.One), title);

        var subSize = ImGui.CalcTextSize(subtitle);
        var subX = pos.X + Math.Max(7f, (size.X - subSize.X) * 0.5f);
        draw.AddText(new Vector2(subX, pos.Y + size.Y - subSize.Y - 10f), U32(Muted), subtitle);
        return clicked;
    }

    private void DrawPhoneSettingsPageHeader()
    {
        var title = _settingsPage switch
        {
            SettingsPage.General => "General",
            SettingsPage.Plex => "Plex",
            SettingsPage.LocalMedia => "Local Media",
            SettingsPage.Playback => "Playback",
            SettingsPage.Screen => "Screen",
            SettingsPage.Networking => "Networking",
            SettingsPage.Advanced => "Advanced",
            SettingsPage.About => "About",
            _ => "Settings",
        };

        if (ImGui.Button("<##SettingsBack", new Vector2(38, 32)))
            _phoneSettingsHome = true;
        ImGui.SameLine(0, 8f);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.80f, 0.72f, 1f, 1f));
        ImGui.TextUnformatted(title.ToUpperInvariant());
        ImGui.PopStyleColor();
    }

    private void DrawSettingsContent()
    {
        switch (_settingsPage)
        {
            case SettingsPage.General: DrawGeneralSettings(); break;
            case SettingsPage.Plex: DrawPlexSettings(); break;
            case SettingsPage.LocalMedia: DrawLocalMediaSettings(); break;
            case SettingsPage.Playback: DrawPlaybackSettings(); break;
            case SettingsPage.Screen: DrawScreenSettings(); break;
            case SettingsPage.Networking: DrawNetworkingSettings(); break;
            case SettingsPage.Advanced: DrawAdvancedSettings(); break;
            case SettingsPage.About: DrawAboutSettings(); break;
        }
    }

    private void SettingsButton(SettingsPage page, string label)
    {
        var active = _settingsPage == page;
        PushTechButton(active);
        if (ImGui.Button(label, new Vector2(-1, 34))) _settingsPage = page;
        PopTechButton();
    }

    private void DrawGeneralSettings()
    {
        SettingsHeading("General");
        var remember = _config.RememberLastPage;
        if (ImGui.Checkbox("Remember the last open page", ref remember))
        {
            _config.RememberLastPage = remember;
            SaveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Appearance");
        var interfaceMode = _config.InterfaceMode;
        var interfaceLabels = new[] { "Automatic", "Tablet", "Phone" };
        if (ImGui.Combo("Interface mode", ref interfaceMode, interfaceLabels, interfaceLabels.Length))
        {
            _config.InterfaceMode = interfaceMode;
            SaveConfig();
            if (interfaceMode == (int)InterfaceMode.Tablet)
                ApplyRecommendedWindowSize(InterfaceMode.Tablet);
            else if (interfaceMode == (int)InterfaceMode.Phone)
                ApplyRecommendedWindowSize(InterfaceMode.Phone);
        }

        if (ImGui.Button("Use Tablet Layout", new Vector2(150, 30)))
        {
            _config.InterfaceMode = (int)InterfaceMode.Tablet;
            SaveConfig();
            ApplyRecommendedWindowSize(InterfaceMode.Tablet);
        }
        ImGui.SameLine();
        if (ImGui.Button("Use Phone Layout", new Vector2(150, 30)))
        {
            _config.InterfaceMode = (int)InterfaceMode.Phone;
            SaveConfig();
            ApplyRecommendedWindowSize(InterfaceMode.Phone);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Phone mode uses bottom navigation. Tablet mode uses the left navigation rail.");
        ImGui.Spacing();
        ImGui.TextDisabled("PrismCast opens through /prism or /prismcast.");
    }

    private void DrawPlexSettings()
    {
        SettingsHeading("Plex");
        var connected = PlexConnected();
        if (connected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Good);
            ImGui.TextUnformatted("● Connected");
            ImGui.PopStyleColor();
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(_config.PlexServerName) ? _config.PlexBaseUrl : _config.PlexServerName);
            ImGui.TextDisabled(_config.PlexBaseUrl);
            ImGui.Spacing();
            if (ImGui.Button("Reconnect Plex", new Vector2(140, 32)))
                RunUiTask(ConnectPlexAsync);
            ImGui.SameLine();
            if (ImGui.Button("Refresh Libraries", new Vector2(150, 32)))
                RunUiTask(LoadPlexLibrariesAsync);
            ImGui.SameLine();
            PushDangerButton();
            if (ImGui.Button("Disconnect", new Vector2(110, 32)))
                DisconnectPlex();
            PopDangerButton();
        }
        else
        {
            ImGui.TextDisabled("No Plex account is connected.");
            if (ImGui.Button("Connect to Plex", new Vector2(150, 34)))
                RunUiTask(ConnectPlexAsync);
        }

        List<PlexServer> servers;
        lock (_plexLock)
            servers = [.. _plexServers];
        if (servers.Count > 1)
        {
            ImGui.Spacing();
            var selected = Math.Clamp(_plexServerIndex, 0, servers.Count - 1);
            var names = servers.Select(x => x.Name).ToArray();
            if (ImGui.Combo("Preferred server", ref selected, names, names.Length))
            {
                _plexServerIndex = selected;
                RunUiTask(async () =>
                {
                    await UsePlexServerAsync(servers[selected]).ConfigureAwait(false);
                    await LoadPlexLibrariesAsync().ConfigureAwait(false);
                });
            }
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Advanced / manual Plex connection"))
        {
            var baseUrl = _config.PlexBaseUrl;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("Plex server URL", ref baseUrl, 512))
            {
                _config.PlexBaseUrl = baseUrl;
                SaveConfig();
            }

            var token = _config.PlexToken;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("Plex token", ref token, 512, ImGuiInputTextFlags.Password))
            {
                _config.PlexToken = token;
                SaveConfig();
            }
        }
    }

    private void DrawLocalMediaSettings()
    {
        SettingsHeading("Local Media");
        ImGui.TextWrapped("Folders listed here are scanned into the Local Files library. PrismCast never uploads them anywhere unless you deliberately host a file in a session.");
        ImGui.Spacing();

        var folders = _config.LocalMediaFolders.ToList();
        foreach (var folder in folders)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
            if (ImGui.BeginChild($"##Folder{folder}", new Vector2(0, 52), true))
            {
                ImGui.TextUnformatted(folder);
                ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 96));
                PushDangerButton();
                if (ImGui.Button($"Remove##{folder}", new Vector2(72, 27)))
                {
                    _config.LocalMediaFolders.RemoveAll(x => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase));
                    SaveConfig();
                    RefreshLocalLibrary();
                }
                PopDangerButton();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (ImGui.Button("Add Folder", new Vector2(120, 32)))
            OpenAddFolderDialog();
        ImGui.SameLine();
        if (ImGui.Button("Refresh Library", new Vector2(140, 32)))
            RefreshLocalLibrary();

        ImGui.Spacing();
        var scan = _config.ScanLocalMediaOnStartup;
        if (ImGui.Checkbox("Scan configured folders when Local Files is first opened", ref scan))
        {
            _config.ScanLocalMediaOnStartup = scan;
            SaveConfig();
        }
        var recurse = _config.IncludeSubfolders;
        if (ImGui.Checkbox("Include subfolders", ref recurse))
        {
            _config.IncludeSubfolders = recurse;
            SaveConfig();
            RefreshLocalLibrary();
        }
    }

    private void DrawPlaybackSettings()
    {
        SettingsHeading("Playback");
        var volume = _config.Volume;
        ImGui.SetNextItemWidth(320);
        if (ImGui.SliderInt("Default / local volume", ref volume, 0, 100))
        {
            _config.Volume = volume;
            _video.SetVolume(volume);
            SaveConfig();
        }
        ImGui.TextDisabled("Viewer volume is local and is not synchronized to the host.");
    }

    private void DrawScreenSettings()
    {
        SettingsHeading("Screen");
        ImGui.TextWrapped("These values control how quickly the Remote Control movement buttons adjust the host's shared screen.");
        ImGui.Spacing();

        var movement = _config.ScreenMovementStep;
        if (ImGui.DragFloat("Movement step", ref movement, 0.001f, 0.001f, 1f, "%.3f"))
        {
            _config.ScreenMovementStep = Math.Clamp(movement, 0.001f, 1f);
            SaveConfig();
        }
        var fine = _config.ScreenFineMovementStep;
        if (ImGui.DragFloat("Fine step (Shift)", ref fine, 0.001f, 0.001f, 0.25f, "%.3f"))
        {
            _config.ScreenFineMovementStep = Math.Clamp(fine, 0.001f, 0.25f);
            SaveConfig();
        }
        var rotation = _config.ScreenRotationStepDegrees;
        if (ImGui.DragFloat("Rotation step (degrees)", ref rotation, 0.1f, 0.1f, 45f, "%.1f"))
        {
            _config.ScreenRotationStepDegrees = Math.Clamp(rotation, 0.1f, 45f);
            SaveConfig();
        }
        var scaleStep = _config.ScreenScaleStep;
        if (ImGui.DragFloat("Scale step", ref scaleStep, 0.005f, 0.005f, 1f, "%.3f"))
        {
            _config.ScreenScaleStep = Math.Clamp(scaleStep, 0.005f, 1f);
            SaveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Exact current host screen transform");
        if (_session.Mode == PrismMode.Viewing)
        {
            ImGui.TextDisabled("Viewer transforms are host controlled.");
            return;
        }

        var changed = false;
        if (ImGui.InputFloat3("Position XYZ", ref _screenPositionEdit, 0f, 0f, "%.3f")) changed = true;
        if (ImGui.InputFloat("Yaw", ref _screenYawDegreesEdit, 0.1f)) changed = true;
        if (ImGui.InputFloat("Pitch", ref _screenPitchDegreesEdit, 0.1f)) changed = true;
        if (ImGui.InputFloat("Roll", ref _screenRollDegreesEdit, 0.1f)) changed = true;
        if (ImGui.DragFloat("Scale", ref _screenScaleEdit, 0.01f, 0.1f, 8f, "%.3f")) changed = true;
        if (ImGui.Checkbox("Curved", ref _screenCurvedEdit)) changed = true;
        if (changed) ApplyScreenEdits();

        ImGui.Spacing();
        if (ImGui.Button("Place in Front of Me", new Vector2(170, 32)))
            PlaceInFrontOfPlayer();
        ImGui.SameLine();
        if (ImGui.Button("Reset Rotation", new Vector2(130, 32)))
            ResetScreenRotation();
    }

    private void DrawNetworkingSettings()
    {
        SettingsHeading("Networking");
        ImGui.TextWrapped("PrismCast handles Watch Party and Group code lookup automatically. There is no directory/relay option to configure in the app.");
        ImGui.Spacing();
        ImGui.TextUnformatted("How connections work");
        ImGui.BulletText("A Watch Party gets a fresh short code every time media starts.");
        ImGui.BulletText("A Group gets one permanent invite code for membership.");
        ImGui.BulletText("The built-in PrismCast directory maps those small codes to the host's temporary Cloudflare session.");
        ImGui.BulletText("The actual media still streams host-to-viewer through the temporary PrismCast tunnel.");
        ImGui.Spacing();
        ImGui.TextDisabled("No Nearby discovery or notification system is used.");
    }

    private void DrawRelaySettings()
    {
        SettingsHeading("Legacy Directory Debug");
        ImGui.TextWrapped("This legacy debug page is retained only for migration compatibility and is not linked from Settings.");
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.060f, 0.105f, 0.98f));
        if (ImGui.BeginChild("##RelayStatusCard", new Vector2(0, 90), true))
        {
            DrawTechFrame(RelayAvailable() ? S9Cyan : Warning);
            ImGui.TextUnformatted("STATUS");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, RelayAvailable() ? Good : Warning);
            ImGui.TextUnformatted(RelayAvailable() ? "CONFIGURED" : "NOT CONFIGURED");
            ImGui.PopStyleColor();
            ImGui.TextDisabled(RelayAvailable()
                ? "Rooms, Nearby, and short session codes can use the configured relay."
                : "Enter the URL of your deployed PrismCast relay below.");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var relay = _config.RelayBaseUrl;
        ImGui.TextUnformatted("Relay URL");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##RelayUrl", ref relay, 1024))
        {
            _config.RelayBaseUrl = relay.Trim();
            SaveConfig();
        }
        ImGui.TextDisabled("Example: https://your-prismcast-relay.example.workers.dev");

        ImGui.Spacing();
        ImGui.TextUnformatted("Your PrismCast Host ID");
        ImGui.TextWrapped(_session.HostId);
        if (ImGui.Button("COPY HOST ID", new Vector2(140, 32)))
            ImGui.SetClipboardText(_session.HostId);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Relay features");
        ImGui.BulletText("Permanent Open and Private Rooms");
        ImGui.BulletText("Nearby session discovery");
        ImGui.BulletText("Short one-time Temporary session codes");
        ImGui.TextDisabled("PrismCast does not send session notifications.");
    }

    private void DrawAdvancedSettings()
    {
        SettingsHeading("Advanced");
        ImGui.TextUnformatted("Runtime status");
        RuntimeLine("Playback runtime", _deps.ReadyForPlayback);
        RuntimeLine("Hosting runtime", _deps.ReadyForHosting);
        ImGui.TextDisabled(_deps.Status);
        ImGui.Spacing();
        ImGui.TextWrapped("Runtime tools are managed automatically by PrismCast. The existing build/runtime system has not been changed by this UI redesign.");
    }

    private void RuntimeLine(string label, bool ready)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(230);
        ImGui.PushStyleColor(ImGuiCol.Text, ready ? Good : Warning);
        ImGui.TextUnformatted(ready ? "Ready" : "Not ready");
        ImGui.PopStyleColor();
    }

    private void DrawAboutSettings()
    {
        SettingsHeading("About PrismCast");
        ImGui.TextWrapped("PrismCast is a standalone Dalamud plugin for synchronized world-space media playback in Final Fantasy XIV.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Version 0.2.0-alpha.35");
        ImGui.TextDisabled("AGPL-3.0-or-later");
        ImGui.Spacing();
        if (ImGui.Button("Copy Source URL", new Vector2(140, 30)))
            ImGui.SetClipboardText("https://github.com/sinthen91/PrismCast");
    }

    private void SettingsHeading(string text)
    {
        ImGui.TextUnformatted(text);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawBottomNowPlaying()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        if (ImGui.BeginChild("##BottomPlayer", new Vector2(0, BottomPlayerHeight), true))
        {
            var info = _video.ReadInfo();
            var title = CurrentMediaTitle();
            ImGui.TextUnformatted(TrimForDisplay(string.IsNullOrWhiteSpace(title) ? "PrismCast session" : title, 62));
            ImGui.TextDisabled($"{FormatTime(info.PositionSeconds)} / {FormatTime(info.DurationSeconds)}");

            if (_session.Mode == PrismMode.Hosting)
            {
                ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 190));
                if (ImGui.Button(info.Paused ? "Play" : "Pause", new Vector2(76, 28)))
                    _session.PauseHost(!info.Paused);
                ImGui.SameLine();
                PushDangerButton();
                if (ImGui.Button("Stop", new Vector2(72, 28)))
                    RunUiTask(StopSessionFromUiAsync);
                PopDangerButton();
            }
            else if (_session.Mode == PrismMode.Viewing)
            {
                ImGui.SameLine(Math.Max(0, ImGui.GetWindowWidth() - 110));
                if (ImGui.Button("Leave", new Vector2(82, 28)))
                    RunUiTask(StopSessionFromUiAsync);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private string LocalFirstName()
    {
        var full = _objects.LocalPlayer?.Name.TextValue?.Trim() ?? "Viewer";
        var first = full.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Viewer";
        return first.Length > 24 ? first[..24] : first;
    }

    private async Task RegisterViewerPresenceAsync(string inviteCode, string firstName)
    {
        if (!PrismInvite.TryDecode(inviteCode, out var baseUrl, out var token))
            return;

        try
        {
            var url = $"{baseUrl}/presence?token={Uri.EscapeDataString(token)}&viewerId={Uri.EscapeDataString(_viewerPresenceId)}&name={Uri.EscapeDataString(firstName)}";
            using var response = await PresenceHttp.GetAsync(url).ConfigureAwait(false);
            // Presence is intentionally best-effort. A viewer should never fail to join merely
            // because the cosmetic player list could not be updated.
        }
        catch
        {
        }
    }

    private float CurrentMovementStep() =>
        Math.Max(0.001f, ImGui.GetIO().KeyShift ? _config.ScreenFineMovementStep : _config.ScreenMovementStep);

    private bool RepeatButton(string label, string id, Vector2 size)
    {
        ImGui.Button($"{label}##{id}", size);
        var active = ImGui.IsItemActive();
        var now = DateTime.UtcNow;

        if (!active)
        {
            _repeatButtonNextFire.Remove(id);
            return false;
        }

        if (!_repeatButtonNextFire.TryGetValue(id, out var next))
        {
            // Move once on press, pause briefly, then repeat at a controlled rate while held.
            _repeatButtonNextFire[id] = now.AddMilliseconds(230);
            return true;
        }

        if (now < next)
            return false;

        _repeatButtonNextFire[id] = now.AddMilliseconds(48);
        return true;
    }

    private async Task PrepareScreenForNewHostAsync()
    {
        if (_session.Mode == PrismMode.Hosting)
            return;

        if (_session.Mode == PrismMode.Viewing)
            await _session.StopAsync().ConfigureAwait(false);

        await _framework.Run(PlaceInFrontOfPlayer).ConfigureAwait(false);
    }

    private async Task StopSessionFromUiAsync()
    {
        // Clear UI-owned artwork immediately so an ended session cannot leave stale Plex art behind.
        _nowPlayingPlexItem = null;
        _activeLobbyPrivate = false;
        await _session.StopAsync().ConfigureAwait(false);
    }

    private async Task HostLocalFromUiAsync(string path)
    {
        await PrepareScreenForNewHostAsync().ConfigureAwait(false);
        _activeLobbyPrivate = false;
        await _session.HostLocalAsync(path).ConfigureAwait(false);
    }

    private async Task HostUrlFromUiAsync(string url, string? title = null)
    {
        _nowPlayingPlexItem = null;
        await PrepareScreenForNewHostAsync().ConfigureAwait(false);
        _activeLobbyPrivate = false;
        await _session.HostUrlAsync(url, title).ConfigureAwait(false);
    }

    private async Task PlayPlexItemFromUiAsync(PlexItem item)
    {
        var title = PlexItemLabel(item);
        _uiStatus = $"Opening {title}...";
        var source = await _plex.ResolveMediaAsync(item.RatingKey, title).ConfigureAwait(false);
        _nowPlayingPlexItem = item;
        try
        {
            await HostPlexFromUiAsync(source).ConfigureAwait(false);
        }
        catch
        {
            _nowPlayingPlexItem = null;
            throw;
        }
        await _framework.Run(() => SelectPage(Page.RemoteControl)).ConfigureAwait(false);
        _uiStatus = "";
    }

    private async Task HostPlexFromUiAsync(PlexMediaSource source)
    {
        await PrepareScreenForNewHostAsync().ConfigureAwait(false);
        _activeLobbyPrivate = false;
        await _session.HostPlexAsync(source).ConfigureAwait(false);
    }

    private string CurrentMediaTitle()
    {
        if (_session.Mode == PrismMode.Hosting)
            return _session.CurrentTitle ?? "";
        if (_session.Mode == PrismMode.Viewing)
            return _session.ViewerState?.Title ?? "";
        return "";
    }

    private void NudgeScreen(float x, float y, float z)
    {
        _screenPositionEdit += new Vector3(x, y, z);
        ApplyScreenEdits();
    }

    private void NudgeLocalHorizontal(float rightAmount, float forwardAmount)
    {
        var yaw = DegreesToRadians(_screenYawDegreesEdit);
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        _screenPositionEdit += right * rightAmount + forward * forwardAmount;
        ApplyScreenEdits();
    }

    private void PlaceInFrontOfPlayer()
    {
        if (_objects.LocalPlayer is not { } player)
            return;

        var rotation = player.Rotation;
        var forward = new Vector3(MathF.Sin(rotation), 0, MathF.Cos(rotation));
        _screenPositionEdit = player.Position + forward * 2.4f + new Vector3(0, 1.4f, 0);
        _screenYawDegreesEdit = RadiansToDegrees(rotation + MathF.PI);
        _screenPitchDegreesEdit = 0;
        _screenRollDegreesEdit = 0;
        ApplyScreenEdits();
    }

    private void ResetScreenRotation()
    {
        if (_objects.LocalPlayer is { } player)
            _screenYawDegreesEdit = RadiansToDegrees(player.Rotation + MathF.PI);
        else
            _screenYawDegreesEdit = 0;

        _screenPitchDegreesEdit = 0;
        _screenRollDegreesEdit = 0;
        ApplyScreenEdits();
    }

    private void ApplyScreenEdits()
    {
        if (_session.Mode == PrismMode.Viewing)
            return;

        _screenScaleEdit = Math.Clamp(_screenScaleEdit, 0.1f, 8f);
        _session.UpdateTransform(
            _screenPositionEdit,
            DegreesToRadians(_screenYawDegreesEdit),
            DegreesToRadians(_screenPitchDegreesEdit),
            DegreesToRadians(_screenRollDegreesEdit),
            _screenScaleEdit,
            _screenCurvedEdit);
    }

    private void SyncViewerScreenEditors()
    {
        if (_session.ViewerState is not { } state)
            return;

        _screenPositionEdit = state.Position;
        _screenYawDegreesEdit = RadiansToDegrees(state.Yaw);
        _screenPitchDegreesEdit = RadiansToDegrees(state.Pitch);
        _screenRollDegreesEdit = RadiansToDegrees(state.Roll);
        _screenScaleEdit = state.Scale;
        _screenCurvedEdit = state.Curved;
    }

    private bool SegmentButton(string label, bool active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active ? Accent : new Vector4(0.15f, 0.14f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? AccentHover : new Vector4(0.22f, 0.20f, 0.28f, 1f));
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(2);
        return clicked;
    }

    private void DrawEmptyState(string title, string body, string buttonLabel, Action action)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        if (ImGui.BeginChild("##EmptyState" + title, new Vector2(0, 190), true))
        {
            ImGui.Dummy(new Vector2(1, 18));
            ImGui.TextUnformatted(title);
            ImGui.TextWrapped(body);
            ImGui.Spacing();
            if (ImGui.Button(buttonLabel, new Vector2(190, 34)))
                action();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void EnsurePlexLoaded()
    {
        if (_plexInitialLoadAttempted || !PlexConnected())
            return;

        _plexInitialLoadAttempted = true;
        RunUiTask(LoadPlexLibrariesAsync);
    }

    private bool PlexConnected() =>
        !string.IsNullOrWhiteSpace(_config.PlexToken) &&
        !string.IsNullOrWhiteSpace(_config.PlexBaseUrl);

    private async Task ConnectPlexAsync()
    {
        _plex.ClientIdentifier = _config.PlexClientIdentifier;
        _uiStatus = "Opening Plex sign-in in your browser...";

        var pin = await _plex.BeginSignInAsync().ConfigureAwait(false);
        await _framework.Run(() => Dalamud.Utility.Util.OpenLink(pin.AuthUrl)).ConfigureAwait(false);

        _uiStatus = "Complete the Plex sign-in in your browser. PrismCast is waiting...";
        var accountToken = await _plex.WaitForSignInAsync(pin.Id, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        var servers = await _plex.GetServersAsync(accountToken).ConfigureAwait(false);
        if (servers.Count == 0)
            throw new InvalidOperationException("Plex sign-in succeeded, but PrismCast could not reach a Plex Media Server on your account.");

        lock (_plexLock)
        {
            _plexServers = servers;
            _plexServerIndex = 0;
        }

        _config.PlexAccountToken = accountToken;
        await UsePlexServerAsync(servers[0]).ConfigureAwait(false);
        await LoadPlexLibrariesAsync().ConfigureAwait(false);
        _uiStatus = $"Connected to Plex: {servers[0].Name}";
    }

    private async Task UsePlexServerAsync(PlexServer server)
    {
        _config.PlexServerName = server.Name;
        _config.PlexBaseUrl = server.BaseUrl;
        _config.PlexToken = server.Token;
        _plex.BaseUrl = server.BaseUrl;
        _plex.Token = server.Token;
        await SaveConfigAsync().ConfigureAwait(false);
    }

    private async Task LoadPlexLibrariesAsync()
    {
        _plex.BaseUrl = _config.PlexBaseUrl;
        _plex.Token = _config.PlexToken;
        _plex.ClientIdentifier = _config.PlexClientIdentifier;

        if (!await _plex.TestServerAsync(_plex.BaseUrl, _plex.Token).ConfigureAwait(false))
            throw new InvalidOperationException("PrismCast could not connect to the configured Plex server. Use Settings > Plex to reconnect.");

        var libraries = await _plex.GetLibrariesAsync().ConfigureAwait(false);
        var mediaLibraries = libraries
            .Where(x => string.Equals(x.Type, "movie", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "show", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mediaLibraries.Count == 0)
            mediaLibraries = libraries;

        var index = mediaLibraries.Count > 0 ? 0 : -1;
        var items = index >= 0
            ? await _plex.GetLibraryItemsAsync(mediaLibraries[index]).ConfigureAwait(false)
            : [];

        lock (_plexLock)
        {
            _libraries = mediaLibraries;
            _libraryIndex = index;
            _plexItems = items;
            _plexHistory.Clear();
            _plexPageTitle = index >= 0 ? mediaLibraries[index].Title : string.Empty;
        }
        _selectedPlexDetailsItem = null;
    }

    private async Task LoadPlexLibraryAsync(PlexLibrary library)
    {
        var items = await _plex.GetLibraryItemsAsync(library).ConfigureAwait(false);
        lock (_plexLock)
        {
            _plexItems = items;
            _plexHistory.Clear();
            _plexPageTitle = library.Title;
        }
        _selectedPlexDetailsItem = null;
        _plexSearch = "";
    }

    private async Task OpenPlexContainerAsync(PlexItem item)
    {
        var children = await _plex.GetChildrenAsync(item.RatingKey).ConfigureAwait(false);
        if (children.Count == 0)
            throw new InvalidOperationException($"Plex returned no playable items inside {item.Title}.");

        lock (_plexLock)
        {
            _plexHistory.Add(new PlexBrowsePage(_plexPageTitle, [.. _plexItems]));
            _plexItems = children;
            _plexPageTitle = item.Title;
        }
        _plexSearch = "";
    }

    private void PlexGoBack()
    {
        _selectedPlexDetailsItem = null;
        lock (_plexLock)
        {
            if (_plexHistory.Count == 0)
                return;

            var last = _plexHistory[^1];
            _plexHistory.RemoveAt(_plexHistory.Count - 1);
            _plexPageTitle = last.Title;
            _plexItems = [.. last.Items];
        }
        _plexSearch = "";
    }

    private static bool IsPlexContainer(PlexItem item) =>
        item.Type.Equals("show", StringComparison.OrdinalIgnoreCase) ||
        item.Type.Equals("season", StringComparison.OrdinalIgnoreCase) ||
        item.Type.Equals("directory", StringComparison.OrdinalIgnoreCase);

    private static string PlexItemLabel(PlexItem item)
    {
        if (item.Type.Equals("episode", StringComparison.OrdinalIgnoreCase) && item.Index > 0)
            return $"{item.Index}. {item.Title}";

        return string.IsNullOrWhiteSpace(item.Year) ? item.Title : $"{item.Title} ({item.Year})";
    }

    private void DisconnectPlex()
    {
        _config.PlexServerName = "";
        _config.PlexBaseUrl = "http://127.0.0.1:32400";
        _config.PlexToken = "";
        _config.PlexAccountToken = "";
        _plex.BaseUrl = _config.PlexBaseUrl;
        _plex.Token = "";
        lock (_plexLock)
        {
            _libraries = [];
            _plexItems = [];
            _plexHistory.Clear();
            _plexPageTitle = "";
            _plexServers = [];
            _libraryIndex = -1;
            _plexServerIndex = -1;
        }
        _plexInitialLoadAttempted = false;
        SaveConfig();
        _uiStatus = "Disconnected from Plex.";
    }

    private void EnsureLocalLibraryLoaded()
    {
        if (_localInitialScanAttempted)
            return;
        _localInitialScanAttempted = true;
        if (_config.ScanLocalMediaOnStartup || _config.LocalMediaFolders.Count > 0)
            RefreshLocalLibrary();
    }

    private void RefreshLocalLibrary()
    {
        var folders = _config.LocalMediaFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var recursive = _config.IncludeSubfolders;
        RunUiTask(async () =>
        {
            var entries = await Task.Run(() => ScanLocalMedia(folders, recursive)).ConfigureAwait(false);
            lock (_localLock)
                _localFiles = entries;
            _uiStatus = $"Local library refreshed: {entries.Count} video files.";
        });
    }

    private static List<LocalMediaEntry> ScanLocalMedia(string[] folders, bool recursive)
    {
        var results = new List<LocalMediaEntry>();
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var folder in folders)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*", option))
                {
                    try
                    {
                        if (!VideoExtensions.Contains(Path.GetExtension(path)))
                            continue;
                        var info = new FileInfo(path);
                        results.Add(new LocalMediaEntry(path, Path.GetFileNameWithoutExtension(path), folder, info.Length));
                    }
                    catch { }
                }
            }
            catch { }
        }

        return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void OpenSingleFileDialog()
    {
        var start = _config.LocalMediaFolders.FirstOrDefault(Directory.Exists)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        _dialogs.OpenFileDialog(
            "Select media",
            "Video{.mp4,.mkv,.webm,.mov,.avi,.flv,.m4v,.wmv,.ts,.m2ts},.*",
            (success, paths) =>
            {
                if (!success || paths.Count == 0 || string.IsNullOrWhiteSpace(paths[0]))
                    return;
                var path = paths[0];
                RunUiTask(async () =>
                {
                    await _session.HostLocalAsync(path).ConfigureAwait(false);
                    SelectPage(Page.RemoteControl);
                });
            },
            1,
            Directory.Exists(start) ? start : null,
            false);
    }

    private void OpenAddFolderDialog()
    {
        var start = _config.LocalMediaFolders.FirstOrDefault(Directory.Exists)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        _dialogs.OpenFolderDialog(
            "Add PrismCast media folder",
            (success, path) =>
            {
                if (!success || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;
                if (!_config.LocalMediaFolders.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                {
                    _config.LocalMediaFolders.Add(path);
                    SaveConfig();
                }
                RefreshLocalLibrary();
            },
            Directory.Exists(start) ? start : null,
            false);
    }

    private void RunUiTask(Func<Task> action)
    {
        if (_uiTask is { IsCompleted: false } current)
        {
            _uiStatus = "Working...";
            _uiTask = QueueUiTaskAsync(current, action);
            return;
        }
        _uiStatus = "Working...";
        _uiTask = ExecuteUiTaskAsync(action);
    }

    private async Task QueueUiTaskAsync(Task current, Func<Task> action)
    {
        try { await current.ConfigureAwait(false); }
        catch { }
        await ExecuteUiTaskAsync(action).ConfigureAwait(false);
    }

    private async Task ExecuteUiTaskAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            if (_uiStatus == "Working...")
                _uiStatus = "";
        }
        catch (Exception ex)
        {
            _uiStatus = "Error: " + ex.Message;
        }
    }

    private void SaveConfig() => _pi.SavePluginConfig(_config);
    private Task SaveConfigAsync() => _framework.Run(() => _pi.SavePluginConfig(_config));

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):F1} MB";
        return $"{bytes / (1024d * 1024 * 1024):F2} GB";
    }

    private static string TrimForDisplay(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..Math.Max(1, max - 1)] + "…";
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
    private static float RadiansToDegrees(float radians) => radians * (180f / MathF.PI);

    private static void PushDangerButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.44f, 0.11f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.62f, 0.16f, 0.22f, 1f));
    }

    private static void PopDangerButton() => ImGui.PopStyleColor(2);

    private static void PushTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 24f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.36f, 0.62f, 0.70f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.055f, 0.060f, 0.110f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.09f, 0.09f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.13f, 0.09f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.075f, 0.075f, 0.14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.13f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.38f, 0.17f, 0.65f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, S9Cyan);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentHover);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, S9Cyan);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.23f, 0.10f, 0.40f, 0.60f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.34f, 0.14f, 0.56f, 0.78f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.42f, 0.17f, 0.68f, 0.90f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.025f, 0.025f, 0.055f, 0.80f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.20f, 0.34f, 0.58f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(S9Cyan.X, S9Cyan.Y, S9Cyan.Z, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(S9Cyan.X, S9Cyan.Y, S9Cyan.Z, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, S9Cyan);
    }

    private static void PopTheme()
    {
        ImGui.PopStyleColor(19);
        ImGui.PopStyleVar(8);
    }
}
