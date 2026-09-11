using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using PrismCast.Hosting;
using PrismCast.Playback;
using PrismCast.UI;

namespace PrismCast;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager _commands;
    private readonly WindowSystem _windows = new("PrismCast");

    private readonly Configuration _config;
    private readonly DependencyManager _deps;
    private readonly LocalMediaServer _server;
    private readonly CloudflareQuickTunnel _tunnel;
    private readonly PlexClient _plex;
    private readonly PrismDx _dx;
    private readonly VideoEngine _video;
    private readonly RelayClient _relay;
    private readonly SessionController _session;
    private readonly PrismCastWindow _window;

    public Plugin(
        IDalamudPluginInterface pi,
        ICommandManager commands,
        IPluginLog log,
        IObjectTable objects,
        IFramework framework)
    {
        _pi = pi;
        _commands = commands;

        _config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        var configChanged = false;
        if (string.IsNullOrWhiteSpace(_config.PlexClientIdentifier))
        {
            _config.PlexClientIdentifier = Guid.NewGuid().ToString("N");
            configChanged = true;
        }
        if (string.IsNullOrWhiteSpace(_config.RoomClientId))
        {
            _config.RoomClientId = Guid.NewGuid().ToString("N");
            configChanged = true;
        }
        if (configChanged)
            pi.SavePluginConfig(_config);

        _deps = new DependencyManager(pi.ConfigDirectory.FullName, log);
        _server = new LocalMediaServer(log);
        _tunnel = new CloudflareQuickTunnel(_deps, log);
        _plex = new PlexClient
        {
            BaseUrl = _config.PlexBaseUrl,
            Token = _config.PlexToken,
            ClientIdentifier = _config.PlexClientIdentifier
        };

        _dx = new PrismDx(pi);
        _video = new VideoEngine(_dx, _deps, log, framework);
        _relay = new RelayClient();
        _session = new SessionController(pi, _config, _deps, _server, _tunnel, _video, _relay, log, objects, framework);

        _video.Screen.SetTransform(
            _config.ScreenPosition,
            _config.ScreenYaw,
            _config.ScreenPitch,
            _config.ScreenRoll,
            _config.ScreenScale);
        _video.Screen.Curved = _config.CurvedScreen;

        _window = new PrismCastWindow(pi, _config, _session, _deps, _plex, _video, _relay, objects, framework);
        _windows.AddWindow(_window);

        commands.AddHandler("/prismcast", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PrismCast."
        });
        commands.AddHandler("/prism", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PrismCast."
        });

        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenConfigUi += Open;
        pi.UiBuilder.OpenMainUi += Open;

        _ = Task.Run(async () =>
        {
            try { await _deps.EnsurePlaybackAsync().ConfigureAwait(false); }
            catch (Exception ex) { log.Warning(ex, "PrismCast runtime warmup failed"); }
        });
    }

    private void OnCommand(string command, string arguments) => Open();

    private void Open() => _window.IsOpen = true;

    private void Draw()
    {
        _session.Tick();
        _video.Tick();
        _video.Screen.Draw();
        _windows.Draw();
    }

    public void Dispose()
    {
        _pi.UiBuilder.Draw -= Draw;
        _pi.UiBuilder.OpenConfigUi -= Open;
        _pi.UiBuilder.OpenMainUi -= Open;

        _commands.RemoveHandler("/prismcast");
        _commands.RemoveHandler("/prism");

        _windows.RemoveAllWindows();
        _session.Dispose();
        _video.Dispose();
        _tunnel.Dispose();
        _server.Dispose();
        _plex.Dispose();
        _relay.Dispose();
        _deps.Dispose();
        _dx.Dispose();
    }
}
