using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PrismCast.Hosting;
using PrismCast.Playback;
using PrismCast.Protocol;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrismCast;

internal enum PrismMode
{
    Idle,
    Hosting,
    Viewing
}

internal sealed class SessionController : IDisposable
{
    private readonly IDalamudPluginInterface _pi;
    private readonly Configuration _config;
    private readonly DependencyManager _deps;
    private readonly LocalMediaServer _server;
    private readonly CloudflareQuickTunnel _tunnel;
    private readonly VideoEngine _video;
    private readonly RelayClient _relay;
    private readonly DesktopCaptureStreamer _capture;
    private readonly IPluginLog _log;
    private readonly IObjectTable _objects;
    private readonly IFramework _framework;
    private readonly HttpClient _http = new();
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true
    };

    private PrismSessionState? _hostState;
    private string? _viewerBase;
    private string? _viewerToken;
    private string? _viewerSessionId;
    private PrismSessionState? _viewerState;
    private DateTime _nextViewerPoll = DateTime.MinValue;
    private DateTime _nextRelayRefresh = DateTime.MinValue;
    private DateTime _nextDiscoveryPoll = DateTime.MinValue;
    private DateTime _nextViewerPresenceRefresh = DateTime.MinValue;
    private Task? _backgroundTick;
    private int _viewerPollFailures;
    private string _lastAutoInvite = string.Empty;
    private readonly string _viewerPresenceId = Guid.NewGuid().ToString("N");
    private string _viewerFirstName = "Viewer";

    internal PrismMode Mode { get; private set; } = PrismMode.Idle;
    internal string Status { get; private set; } = "Idle";
    internal string InviteCode { get; private set; } = "";
    internal string TemporaryCode { get; private set; } = "";
    internal string? CurrentTitle => _hostState?.Title;
    internal PrismSessionState? ViewerState => _viewerState;
    internal bool IsScreenShareSession =>
        string.Equals((_hostState ?? _viewerState)?.PlaybackKind, "screen", StringComparison.OrdinalIgnoreCase);
    internal IReadOnlyList<DesktopCaptureTarget> CaptureTargets => DesktopCaptureStreamer.GetTargets();
    internal IReadOnlyList<WasapiEndpoint> CaptureAudioEndpoints => DesktopCaptureStreamer.GetAudioEndpoints();
    internal IReadOnlyList<ScreenShareEncodingProfile> CaptureQualityProfiles => DesktopCaptureStreamer.QualityProfiles;

    internal string HostId => RelayClient.HostIdFromSecret(RelayClient.EnsureSecret(_config));
    private string DirectoryUrl => RelayClient.DirectoryBaseUrl;

    public SessionController(
        IDalamudPluginInterface pi,
        Configuration config,
        DependencyManager deps,
        LocalMediaServer server,
        CloudflareQuickTunnel tunnel,
        VideoEngine video,
        RelayClient relay,
        DesktopCaptureStreamer capture,
        IPluginLog log,
        IObjectTable objects,
        IFramework framework)
    {
        _pi = pi;
        _config = config;
        _deps = deps;
        _server = server;
        _tunnel = tunnel;
        _video = video;
        _relay = relay;
        _capture = capture;
        _log = log;
        _objects = objects;
        _framework = framework;

    }

    internal async Task HostScreenAsync(DesktopCaptureTarget target, WasapiEndpoint audioEndpoint,
        ScreenShareEncodingProfile profile, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        Status = "Preparing screen share";
        try
        {
            await _video.EnsureInitializedAsync(_config.Volume, ct, profile.Width, profile.Height).ConfigureAwait(false);
            await EnsureInitialScreenPlacementAsync(ct).ConfigureAwait(false);
            var hostName = await GetLocalFirstNameAsync(ct).ConfigureAwait(false);

            _server.Start();
            _server.MediaPath = null;
            _server.MediaProxyUrl = null;
            _server.LiveMediaDirectory = _capture.OutputDirectory;
            _server.StateProvider = SnapshotHostState;

            await _capture.StartAsync(target, audioEndpoint, profile, ct).ConfigureAwait(false);
            var localMediaUrl = $"http://127.0.0.1:{_server.Port}/live/index.m3u8?token={Uri.EscapeDataString(_server.Token)}";
            var publicUrl = await _tunnel.StartAsync(_server.Port, ct).ConfigureAwait(false);
            var mediaUrl = $"/live/index.m3u8?token={Uri.EscapeDataString(_server.Token)}";

            // Window titles can contain document names, account names, or private messages.
            // Keep the selected title local and publish only a generic session label.
            var publicTitle = target.Id == "desktop" ? "Entire Desktop Share" : "Application Window Share";
            _hostState = MakeInitialState(mediaUrl, publicTitle, hostName, true) with
            {
                PlaybackKind = "screen",
                VideoWidth = profile.Width,
                VideoHeight = profile.Height,
            };
            InviteCode = PrismInvite.Encode(publicUrl, _server.Token);
            Mode = PrismMode.Hosting;
            InitializeBroadcastIdentity();

            if (!_video.Play(localMediaUrl, 0, true))
                throw new InvalidOperationException("The media player could not open the screen-share stream.");
            ApplyScreenTransform(_hostState);
            Status = "Sharing screen";
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            Status = "Screen share setup failed";
            throw;
        }
    }

    internal async Task HostLocalAsync(string path, string? title = null, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Media file not found.", path);

        await StopAsync().ConfigureAwait(false);
        Status = "Preparing host";

        try
        {
            await _video.EnsureInitializedAsync(_config.Volume, ct).ConfigureAwait(false);
            await EnsureInitialScreenPlacementAsync(ct).ConfigureAwait(false);
            var hostName = await GetLocalFirstNameAsync(ct).ConfigureAwait(false);

            _server.Start();
            _server.MediaPath = path;
            _server.MediaProxyUrl = null;
            _server.StateProvider = SnapshotHostState;

            var publicUrl = await _tunnel.StartAsync(_server.Port, ct).ConfigureAwait(false);
            var mediaUrl = $"/media?token={Uri.EscapeDataString(_server.Token)}";

            _hostState = MakeInitialState(mediaUrl, title ?? Path.GetFileNameWithoutExtension(path), hostName);
            InviteCode = PrismInvite.Encode(publicUrl, _server.Token);
            Mode = PrismMode.Hosting;
            InitializeBroadcastIdentity();

            if (!_video.Play(path, 0, true))
                throw new InvalidOperationException("The media player refused the selected file.");

            ApplyScreenTransform(_hostState);
            Status = "Hosting";
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            Status = "Host setup failed";
            throw;
        }
    }

    internal async Task HostUrlAsync(string url, string? title = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL.", nameof(url));

        await StopAsync().ConfigureAwait(false);
        Status = "Preparing host";

        try
        {
            await _video.EnsureInitializedAsync(_config.Volume, ct).ConfigureAwait(false);
            await EnsureInitialScreenPlacementAsync(ct).ConfigureAwait(false);
            var hostName = await GetLocalFirstNameAsync(ct).ConfigureAwait(false);

            _server.Start();
            _server.MediaPath = null;
            _server.MediaProxyUrl = null;
            _server.StateProvider = SnapshotHostState;
            var publicUrl = await _tunnel.StartAsync(_server.Port, ct).ConfigureAwait(false);

            _hostState = MakeInitialState(url, title ?? url, hostName, IsTwitchUrl(parsed));
            InviteCode = PrismInvite.Encode(publicUrl, _server.Token);
            Mode = PrismMode.Hosting;
            InitializeBroadcastIdentity();

            if (!_video.Play(url, 0, true))
                throw new InvalidOperationException("The media player refused the supplied URL.");

            ApplyScreenTransform(_hostState);
            Status = "Hosting";
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            Status = "Host setup failed";
            throw;
        }
    }

    internal async Task HostPlexAsync(PlexMediaSource source, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Plex returned an invalid media URL.", nameof(source));

        await StopAsync().ConfigureAwait(false);
        Status = "Preparing Plex host";

        try
        {
            await _video.EnsureInitializedAsync(_config.Volume, ct).ConfigureAwait(false);
            await EnsureInitialScreenPlacementAsync(ct).ConfigureAwait(false);
            var hostName = await GetLocalFirstNameAsync(ct).ConfigureAwait(false);

            _server.Start();
            _server.MediaPath = null;
            _server.MediaProxyUrl = source.Url;
            _server.StateProvider = SnapshotHostState;

            var publicUrl = await _tunnel.StartAsync(_server.Port, ct).ConfigureAwait(false);
            var mediaUrl = $"/media?token={Uri.EscapeDataString(_server.Token)}";

            _hostState = MakeInitialState(mediaUrl, source.Title, hostName);
            InviteCode = PrismInvite.Encode(publicUrl, _server.Token);
            Mode = PrismMode.Hosting;
            InitializeBroadcastIdentity();

            // The host plays directly from Plex using the private server token.
            // Viewers receive only the temporary PrismCast proxy URL, never the Plex token.
            if (!_video.Play(source.Url, 0, true))
                throw new InvalidOperationException("The media player refused the selected Plex item.");

            ApplyScreenTransform(_hostState);
            Status = "Hosting Plex";
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            Status = "Plex host setup failed";
            throw;
        }
    }

    internal async Task JoinAsync(string code, CancellationToken ct = default)
    {
        if (!PrismInvite.TryDecode(code, out var baseUrl, out var token))
            throw new ArgumentException("That PrismCast invite code is invalid.");

        await StopAsync().ConfigureAwait(false);
        Status = "Joining";

        try
        {
            _viewerFirstName = await GetLocalFirstNameAsync(ct).ConfigureAwait(false);

            _viewerBase = baseUrl;
            _viewerToken = token;
            _viewerPollFailures = 0;
            _nextViewerPresenceRefresh = DateTime.MinValue;
            Mode = PrismMode.Viewing;
            await PollViewerAsync(force: true, ct).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            Status = "Join failed";
            throw;
        }
    }

    internal void PauseHost(bool paused)
    {
        if (Mode != PrismMode.Hosting || _hostState is null)
            return;

        var info = ReadPlaybackInfo();
        if (IsScreenShareSession)
            return;
        _video.Pause(paused);
        _hostState = _hostState with
        {
            Playing = !paused,
            PositionSeconds = info.PositionSeconds,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    internal void SeekHost(double seconds)
    {
        if (Mode != PrismMode.Hosting || _hostState is null)
            return;

        if (IsScreenShareSession)
            return;
        _video.Seek(seconds);
        _hostState = _hostState with
        {
            PositionSeconds = Math.Max(0, seconds),
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    internal MpvPlaybackInfo ReadPlaybackInfo()
    {
        return _video.ReadInfo();
    }

    internal void UpdateTransform(Vector3 position, float yaw, float pitch, float roll, float scale, bool curved)
    {
        // A viewer always uses the host's world-space transform. Local viewer edits would
        // otherwise flash briefly and then be replaced on the next host-state poll.
        if (Mode == PrismMode.Viewing)
            return;

        _config.ScreenPosition = position;
        _config.ScreenYaw = yaw;
        _config.ScreenPitch = pitch;
        _config.ScreenRoll = roll;
        _config.ScreenScale = Math.Clamp(scale, 0.1f, 8f);
        _config.CurvedScreen = curved;
        _config.HasScreenPlacement = true;
        _pi.SavePluginConfig(_config);

        _video.Screen.SetTransform(position, yaw, pitch, roll, _config.ScreenScale);
        _video.Screen.Curved = curved;

        if (_hostState is not null)
        {
            _hostState = _hostState with
            {
                Position = position,
                Yaw = yaw,
                Pitch = pitch,
                Roll = roll,
                Scale = _config.ScreenScale,
                Curved = curved
            };
        }
    }

    internal void Tick()
    {
        if (_backgroundTick is { IsCompleted: false })
            return;

        if (Mode == PrismMode.Hosting)
        {
            UpdateHostClock();
            if (DateTime.UtcNow >= _nextRelayRefresh)
            {
                _nextRelayRefresh = DateTime.UtcNow.AddSeconds(45);
                _backgroundTick = Task.Run(() => AnnounceRelayBestEffortAsync(CancellationToken.None));
            }
            return;
        }

        if (Mode == PrismMode.Viewing && DateTime.UtcNow >= _nextViewerPoll)
        {
            _nextViewerPoll = DateTime.UtcNow.AddMilliseconds(500);
            _backgroundTick = Task.Run(async () =>
            {
                try
                {
                    await PollViewerAsync(false, CancellationToken.None).ConfigureAwait(false);
                    Interlocked.Exchange(ref _viewerPollFailures, 0);
                }
                catch (Exception ex)
                {
                    var failures = Interlocked.Increment(ref _viewerPollFailures);
                    Status = failures < 6 ? "Reconnecting to host" : "Host went offline";
                    _log.Debug("PrismCast viewer poll failed ({FailureType})", ex.GetType().Name);

                    if (failures >= 6)
                    {
                        await StopAsync().ConfigureAwait(false);
                        Status = "Host went offline";
                    }
                }
            });

            return;
        }

    }

    private PrismSessionState MakeInitialState(string mediaUrl, string title, string hostName,
        bool isLiveStream = false) => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        MediaUrl = mediaUrl,
        Playing = true,
        IsLiveStream = isLiveStream,
        PositionSeconds = 0,
        HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Position = _config.ScreenPosition,
        Yaw = _config.ScreenYaw,
        Pitch = _config.ScreenPitch,
        Roll = _config.ScreenRoll,
        Scale = _config.ScreenScale,
        Curved = _config.CurvedScreen,
        Title = title,
        HostName = hostName,
        Viewers = []
    };

    private PrismSessionState? SnapshotHostState()
    {
        UpdateHostClock();
        if (_hostState is not null)
            _hostState = _hostState with { Viewers = ViewerPresenceRegistry.GetViewerNames().ToArray() };
        return _hostState;
    }

    private void UpdateHostClock()
    {
        if (_hostState is null)
            return;

        var info = ReadPlaybackInfo();
        _hostState = _hostState with
        {
            PositionSeconds = Math.Max(0, info.PositionSeconds),
            DurationSeconds = Math.Max(0, info.DurationSeconds),
            Playing = !info.Paused,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private async Task PollViewerAsync(bool force, CancellationToken ct)
    {
        if (_viewerBase is null || _viewerToken is null || Mode != PrismMode.Viewing)
            return;

        var url = $"{_viewerBase}/state?token={Uri.EscapeDataString(_viewerToken)}";
        var state = await _http.GetFromJsonAsync<PrismSessionState>(url, SessionJsonOptions, ct).ConfigureAwait(false);
        if (state is null)
            throw new InvalidOperationException("The host returned an empty session state.");

        _viewerState = state;

        var mediaUrl = state.MediaUrl.StartsWith("/", StringComparison.Ordinal)
            ? _viewerBase + state.MediaUrl
            : state.MediaUrl;

        var expected = state.PositionSeconds;
        if (state.Playing)
        {
            var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - state.HostUnixMilliseconds;
            expected += Math.Max(0, elapsedMs) / 1000.0;
        }

        if (state.PlaybackKind.Equals("companion", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This PrismCast build does not support retired browser-companion sessions. Ask the host to use Share Screen / Window.");

        if (force || !string.Equals(_viewerSessionId, state.SessionId, StringComparison.Ordinal))
        {
            _viewerSessionId = state.SessionId;
            await _video.EnsureInitializedAsync(_config.Volume, ct, state.VideoWidth, state.VideoHeight).ConfigureAwait(false);
            var initialPosition = state.IsLiveStream ? 0 : expected;
            if (!_video.Play(mediaUrl, initialPosition, state.Playing))
                throw new InvalidOperationException("The media player could not open the host stream.");
            _video.SetSpeed(1.0);
        }
        else if (state.IsLiveStream)
        {
            // A Twitch/HLS live window has no stable seek origin shared by two separate
            // players. Chasing the host's moving timestamp every 500 ms prevents the
            // viewer from buffering video and can leave audio minutes behind. Let each
            // player stay at its live edge and synchronize only pause/resume.
            _video.SetSpeed(1.0);
            _video.Pause(!state.Playing);
        }
        else
        {
            var local = _video.ReadInfo();
            var drift = expected - local.PositionSeconds;

            if (Math.Abs(drift) > 1.5)
            {
                _video.Seek(expected);
                _video.SetSpeed(1.0);
            }
            else if (state.Playing)
            {
                _video.SetSpeed(drift > 0.25 ? 1.03 : drift < -0.25 ? 0.97 : 1.0);
            }
            else
            {
                _video.SetSpeed(1.0);
            }

            _video.Pause(!state.Playing);
        }

        ApplyScreenTransform(state);
        await RefreshViewerPresenceAsync(ct).ConfigureAwait(false);
        Interlocked.Exchange(ref _viewerPollFailures, 0);
        Status = $"Viewing: {state.Title}";
    }

    private async Task RefreshViewerPresenceAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow < _nextViewerPresenceRefresh)
            return;
        _nextViewerPresenceRefresh = DateTime.UtcNow.AddSeconds(10);
        await SendViewerPresenceBestEffortAsync(leave: false, ct).ConfigureAwait(false);
    }

    private async Task SendViewerPresenceBestEffortAsync(bool leave, CancellationToken ct)
    {
        var baseUrl = _viewerBase;
        var token = _viewerToken;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var action = leave ? "&action=leave" : "";
            var url = $"{baseUrl}/presence?token={Uri.EscapeDataString(token)}" +
                      $"&viewerId={Uri.EscapeDataString(_viewerPresenceId)}" +
                      $"&name={Uri.EscapeDataString(_viewerFirstName)}{action}";
            using var response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Presence never blocks playback. The next heartbeat will try again.
            _log.Debug("PrismCast viewer presence update failed ({FailureType})", ex.GetType().Name);
        }
    }

    private async Task DiscoverTrustedHostAsync(CancellationToken ct)
    {
        if (Mode != PrismMode.Idle)
            return;

        foreach (var hostId in ParseTrustedHostIds())
        {
            var invite = await _relay.ResolveAsync(_config.RelayBaseUrl, hostId, ct).ConfigureAwait(false);
            if (Mode != PrismMode.Idle)
                return;

            if (string.IsNullOrWhiteSpace(invite) ||
                string.Equals(invite, _lastAutoInvite, StringComparison.Ordinal))
                continue;

            try
            {
                await JoinAsync(invite, ct).ConfigureAwait(false);
                _lastAutoInvite = invite;
                return;
            }
            catch (Exception ex)
            {
                _log.Debug("PrismCast could not auto-join trusted host {HostId} ({FailureType})", hostId, ex.GetType().Name);
            }
        }
    }

    private static bool IsTwitchUrl(Uri uri) =>
        uri.Host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> ParseTrustedHostIds() =>
        _config.TrustedHostIdsText
            .Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 8)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private async Task EnsureInitialScreenPlacementAsync(CancellationToken ct)
    {
        await _framework.Run(() =>
        {
            if (_config.HasScreenPlacement || _objects.LocalPlayer is not { } player)
                return;

            var rotation = player.Rotation;
            var forward = new Vector3(MathF.Sin(rotation), 0, MathF.Cos(rotation));
            var position = player.Position + forward * 2.4f + new Vector3(0, 1.4f, 0);

            _config.ScreenPosition = position;
            _config.ScreenYaw = rotation + MathF.PI;
            _config.ScreenPitch = 0;
            _config.ScreenRoll = 0;
            _config.HasScreenPlacement = true;
            _pi.SavePluginConfig(_config);

            _video.Screen.SetTransform(position, _config.ScreenYaw, 0, 0, _config.ScreenScale);
            _video.Screen.Curved = _config.CurvedScreen;
        }, ct).ConfigureAwait(false);
    }

    private void ApplyScreenTransform(PrismSessionState state)
    {
        _video.Screen.SetTransform(state.Position, state.Yaw, state.Pitch, state.Roll, state.Scale);
        _video.Screen.Curved = state.Curved;
        _video.Screen.Visible = true;
    }

    private async Task AnnounceRelayBestEffortAsync(CancellationToken ct)
    {
        try
        {
            await AnnounceRelayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Debug("PrismCast directory announcement failed; direct invite remains available ({FailureType})", ex.GetType().Name);
        }
    }

    private async Task AnnounceRelayAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(InviteCode))
            return;

        var secret = RelayClient.EnsureSecret(_config);
        var hostName = _hostState?.HostName ?? "Host";
        var title = _hostState?.Title ?? "PrismCast session";
        var activeGroup = _config.PrismRooms.FirstOrDefault(x =>
            x.IsHost && !string.IsNullOrWhiteSpace(_config.ActiveRoomId) &&
            string.Equals(x.RoomId, _config.ActiveRoomId, StringComparison.OrdinalIgnoreCase));

        if (activeGroup is not null)
        {
            await _relay.AnnounceRoomSessionAsync(DirectoryUrl, secret, activeGroup.RoomId,
                InviteCode, title, hostName, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(TemporaryCode))
        {
            await _relay.AnnounceTemporaryAsync(DirectoryUrl, secret, TemporaryCode,
                InviteCode, title, hostName, ct).ConfigureAwait(false);
        }
    }

    private void InitializeBroadcastIdentity()
    {
        // A group uses its permanent identity; an ordinary Watch Party gets a fresh short code.
        TemporaryCode = string.IsNullOrWhiteSpace(_config.ActiveRoomId) ? GenerateTemporaryCode() : "";
        _nextRelayRefresh = DateTime.MinValue;
        _ = AnnounceRelayBestEffortAsync(CancellationToken.None);
    }

    private static string GenerateTemporaryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(6);
        var sb = new StringBuilder(6);
        foreach (var b in bytes)
            sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }

    private async Task<string> GetLocalFirstNameAsync(CancellationToken ct = default)
    {
        var firstName = "Host";
        await _framework.Run(() => firstName = LocalFirstName(), ct).ConfigureAwait(false);
        return firstName;
    }

    private string LocalFirstName()
    {
        var full = _objects.LocalPlayer?.Name.TextValue?.Trim() ?? "Host";
        var first = full.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Host";
        return first.Length > 24 ? first[..24] : first;
    }

    internal async Task StopAsync()
    {
        var wasHosting = Mode == PrismMode.Hosting;
        var wasViewing = Mode == PrismMode.Viewing;
        var closingCode = TemporaryCode;
        var closingGroupId = wasHosting ? _config.ActiveRoomId : "";
        var secret = RelayClient.EnsureSecret(_config);

        if (wasViewing)
            await SendViewerPresenceBestEffortAsync(leave: true, CancellationToken.None).ConfigureAwait(false);

        Mode = PrismMode.Idle;
        Status = "Idle";
        InviteCode = "";
        TemporaryCode = "";
        _viewerBase = null;
        _viewerToken = null;
        _viewerSessionId = null;
        _viewerState = null;
        _hostState = null;
        _nextRelayRefresh = DateTime.MinValue;
        _nextViewerPresenceRefresh = DateTime.MinValue;
        Interlocked.Exchange(ref _viewerPollFailures, 0);
        _video.Stop();
        _video.SetSpeed(1.0);
        _video.Screen.Visible = false;
        _tunnel.Stop();
        _server.Stop();
        await _capture.StopAsync().ConfigureAwait(false);

        if (wasHosting)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(closingGroupId))
                    await _relay.CloseRoomSessionAsync(DirectoryUrl, secret, closingGroupId, CancellationToken.None).ConfigureAwait(false);
                else if (!string.IsNullOrWhiteSpace(closingCode))
                    await _relay.CloseTemporaryAsync(DirectoryUrl, secret, closingCode, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Debug("PrismCast directory close failed; TTL cleanup will expire the session ({FailureType})", ex.GetType().Name);
            }
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _http.Dispose();
    }
}
