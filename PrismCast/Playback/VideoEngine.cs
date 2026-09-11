using Dalamud.Plugin.Services;
using PrismCast.Hosting;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace PrismCast.Playback;

internal sealed class VideoEngine : IDisposable
{
    internal const int Width = 1280;
    internal const int Height = 720;

    private readonly PrismDx _dx;
    private readonly DependencyManager _deps;
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly WorldScreenRenderer _screen;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);

    private MpvSoftwareRenderer? _mpv;
    private Texture2D? _texture;
    private byte[] _frame = new byte[Width * Height * 4];
    private int _uploadedVersion = -1;

    internal WorldScreenRenderer Screen => _screen;
    internal bool Ready => _mpv is not null;

    public VideoEngine(PrismDx dx, DependencyManager deps, IPluginLog log, IFramework framework)
    {
        _dx = dx;
        _deps = deps;
        _log = log;
        _framework = framework;
        _screen = new WorldScreenRenderer(dx, log) { Visible = false };
    }

    internal async Task EnsureInitializedAsync(int volume, CancellationToken ct = default)
    {
        if (_mpv is not null)
            return;

        await _initializeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_mpv is not null)
                return;

            await _deps.EnsurePlaybackAsync(ct).ConfigureAwait(false);
            _deps.PrepareRuntimePath();

            // Dalamud/game graphics objects must be created from the framework thread.
            // The old alpha initialized these from Task.Run(), which caused the
            // "Not on main thread!" failure when hosting a file.
            await _framework.Run(() => InitializeGraphics(volume), ct).ConfigureAwait(false);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private void InitializeGraphics(int volume)
    {
        if (_mpv is not null)
            return;

        _texture = new Texture2D(_dx.Device, new Texture2DDescription
        {
            Width = Width,
            Height = Height,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });

        _screen.SetTarget(_texture);

        var renderer = new MpvSoftwareRenderer(_log);
        try
        {
            renderer.Initialize(Width, Height, File.Exists(_deps.YtDlpExe) ? _deps.YtDlpExe : null, volume);
            _mpv = renderer;
        }
        catch
        {
            renderer.Dispose();
            _screen.SetTarget(null);
            _texture.Dispose();
            _texture = null;
            throw;
        }
    }

    internal bool Play(string source, double seconds, bool playing) =>
        _mpv?.Play(source, seconds, playing) ?? false;

    internal void Pause(bool paused) => _mpv?.Pause(paused);
    internal void Seek(double seconds) => _mpv?.Seek(seconds);
    internal void Stop() => _mpv?.Stop();
    internal void SetVolume(int volume) => _mpv?.SetVolume(volume);
    internal void SetSpeed(double speed) => _mpv?.SetSpeed(speed);
    internal MpvPlaybackInfo ReadInfo() => _mpv?.ReadInfo() ?? default;

    internal void Tick()
    {
        if (_mpv is null || _texture is null)
            return;

        var version = _mpv.FrameVersion;
        if (version == _uploadedVersion)
            return;

        if (!_mpv.TryCopyLatestFrame(_frame))
            return;

        _uploadedVersion = version;
        unsafe
        {
            fixed (byte* p = _frame)
            {
                _dx.Device.ImmediateContext.UpdateSubresource(
                    _texture, 0, null, (IntPtr)p, Width * 4, 0);
            }
        }
    }

    public void Dispose()
    {
        _mpv?.Dispose();
        _screen.SetTarget(null);
        _screen.Dispose();
        _texture?.Dispose();
        _initializeGate.Dispose();
    }
}
