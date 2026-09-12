using Dalamud.Plugin.Services;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PrismCast.Playback;

internal readonly record struct MpvPlaybackInfo(
    double PositionSeconds,
    double DurationSeconds,
    bool Paused,
    bool Idle);

internal sealed class MpvSoftwareRenderer : IDisposable
{
    private const string Library = "libmpv-2";
    private const int MpvFormatFlag = 3;
    private const int MpvFormatDouble = 5;

    private const int RenderParamInvalid = 0;
    private const int RenderParamApiType = 1;
    private const int RenderParamSwSize = 17;
    private const int RenderParamSwFormat = 18;
    private const int RenderParamSwStride = 19;
    private const int RenderParamSwPointer = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderParam
    {
        public int Type;
        public IntPtr Data;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UpdateCallback(IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Library, EntryPoint = "mpv_command", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command_native(IntPtr ctx, IntPtr[] args);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_create(ref IntPtr render, IntPtr ctx, IntPtr parameters);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong mpv_render_context_update(IntPtr render);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_render_context_render(IntPtr render, IntPtr parameters);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_set_update_callback(
        IntPtr render, UpdateCallback callback, IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_render_context_free(IntPtr render);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(
        IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(
        IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out int data);

    private readonly IPluginLog _log;
    private readonly object _commandLock = new();
    private readonly object _frameLock = new();
    private readonly AutoResetEvent _frameSignal = new(false);

    private IntPtr _mpv;
    private IntPtr _render;
    private IntPtr _frame;
    private IntPtr _renderParams;
    private IntPtr _size;
    private IntPtr _stride;
    private IntPtr _format;

    private UpdateCallback? _callback;
    private Thread? _renderThread;
    private Thread? _eventThread;
    private volatile bool _disposed;
    private int _width;
    private int _height;
    private int _bytes;
    private bool _subtitlesEnabled;
    private byte[]? _frontFrame;
    private byte[]? _backFrame;
    private int _frameVersion;

    internal int FrameVersion => Volatile.Read(ref _frameVersion);

    public MpvSoftwareRenderer(IPluginLog log)
    {
        _log = log;
    }

    internal void Initialize(int width, int height, string? ytDlpPath, int volume)
    {
        if (_mpv != IntPtr.Zero)
            return;

        _width = width;
        _height = height;
        _bytes = checked(width * height * 4);
        _frame = Marshal.AllocHGlobal(_bytes);
        _frontFrame = new byte[_bytes];
        _backFrame = new byte[_bytes];

        _mpv = mpv_create();
        if (_mpv == IntPtr.Zero)
            throw new InvalidOperationException("libmpv could not be created.");

        SetOption("vo", "libmpv");
        SetOption("hwdec", "auto-safe");
        SetOption("profile", "sw-fast");
        SetOption("idle", "yes");
        SetOption("keep-open", "no");
        SetOption("terminal", "no");
        SetOption("volume", Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));
        SetOption("ytdl", "yes");
        SetOption("ytdl-raw-options", "ignore-config=");
        // PrismCast renders at 1280x720. Avoid making slower viewers decode a 1080p/4K
        // live rendition that cannot improve the shared screen but can multiply latency.
        SetOption("ytdl-format", "best[height<=720]/best");
        SetOption("stream-lavf-o",
            "reconnect=1,reconnect_streamed=1,reconnect_on_network_error=1,reconnect_on_http_error=5xx,reconnect_delay_max=30");

        if (!string.IsNullOrWhiteSpace(ytDlpPath))
            SetOption("script-opts", $"ytdl_hook-ytdl_path={ytDlpPath}");

        var init = mpv_initialize(_mpv);
        if (init < 0)
            throw new InvalidOperationException($"libmpv initialization failed ({init}).");

        CreateSoftwareRenderContext();

        _callback = _ => _frameSignal.Set();
        mpv_render_context_set_update_callback(_render, _callback, IntPtr.Zero);

        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "PrismCast-mpv-events"
        };
        _eventThread.Start();

        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "PrismCast-mpv-render"
        };
        _renderThread.Start();
    }

    private void SetOption(string name, string value)
    {
        var result = mpv_set_option_string(_mpv, name, value);
        if (result < 0)
            _log.Warning("PrismCast mpv option {Name} was refused ({Code})", name, result);
    }

    private void CreateSoftwareRenderContext()
    {
        var paramSize = Marshal.SizeOf<RenderParam>();
        var api = Marshal.StringToHGlobalAnsi("sw");
        var createParams = Marshal.AllocHGlobal(paramSize * 2);

        try
        {
            Marshal.StructureToPtr(new RenderParam { Type = RenderParamApiType, Data = api }, createParams, false);
            Marshal.StructureToPtr(new RenderParam { Type = RenderParamInvalid }, createParams + paramSize, false);

            var result = mpv_render_context_create(ref _render, _mpv, createParams);
            if (result < 0)
                throw new InvalidOperationException($"libmpv software render context failed ({result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(api);
            Marshal.FreeHGlobal(createParams);
        }

        _size = Marshal.AllocHGlobal(8);
        Marshal.WriteInt32(_size, _width);
        Marshal.WriteInt32(_size + 4, _height);

        _stride = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(_stride, (IntPtr)(_width * 4));

        _format = Marshal.StringToHGlobalAnsi("bgra");

        _renderParams = Marshal.AllocHGlobal(paramSize * 5);
        Marshal.StructureToPtr(new RenderParam { Type = RenderParamSwSize, Data = _size }, _renderParams, false);
        Marshal.StructureToPtr(new RenderParam { Type = RenderParamSwFormat, Data = _format }, _renderParams + paramSize, false);
        Marshal.StructureToPtr(new RenderParam { Type = RenderParamSwStride, Data = _stride }, _renderParams + paramSize * 2, false);
        Marshal.StructureToPtr(new RenderParam { Type = RenderParamSwPointer, Data = _frame }, _renderParams + paramSize * 3, false);
        Marshal.StructureToPtr(new RenderParam { Type = RenderParamInvalid }, _renderParams + paramSize * 4, false);
    }

    private static int Command(IntPtr ctx, params string?[] values)
    {
        var ptrs = new IntPtr[values.Length];
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is not null)
                    ptrs[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            }

            return mpv_command_native(ctx, ptrs);
        }
        finally
        {
            foreach (var p in ptrs)
            {
                if (p != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(p);
            }
        }
    }

    internal bool Play(string source, double seconds, bool playing)
    {
        lock (_commandLock)
        {
            if (_mpv == IntPtr.Zero)
                return false;

            _ = Command(_mpv, "set", "speed", "1", null);
            var start = Math.Max(0, seconds).ToString("F3", CultureInfo.InvariantCulture);
            var subtitleOptions = _subtitlesEnabled
                ? "sid=auto,sub-visibility=yes"
                : "sid=no,sub-visibility=no";
            var result = Command(_mpv, "loadfile", source, "replace", "0",
                $"start={start},pause={(playing ? "no" : "yes")},{subtitleOptions}", null);
            return result >= 0;
        }
    }

    internal void Pause(bool paused) => SetProperty("pause", paused ? "yes" : "no");

    internal void SetVolume(int volume) =>
        SetProperty("volume", Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));

    internal void SetSpeed(double speed) =>
        SetProperty("speed", Math.Clamp(speed, 0.5, 2.0).ToString("F3", CultureInfo.InvariantCulture));

    internal void SetSubtitlesEnabled(bool enabled)
    {
        _subtitlesEnabled = enabled;
        SetProperty("sid", enabled ? "auto" : "no");
        SetProperty("sub-visibility", enabled ? "yes" : "no");
    }

    internal void Seek(double seconds)
    {
        lock (_commandLock)
        {
            if (_mpv != IntPtr.Zero)
                _ = Command(_mpv, "seek", Math.Max(0, seconds).ToString("F3", CultureInfo.InvariantCulture), "absolute", null);
        }
    }

    internal void Stop()
    {
        lock (_commandLock)
        {
            if (_mpv != IntPtr.Zero)
                _ = Command(_mpv, "stop", null);
        }
    }

    private void SetProperty(string name, string value)
    {
        lock (_commandLock)
        {
            if (_mpv != IntPtr.Zero)
                _ = Command(_mpv, "set", name, value, null);
        }
    }

    internal MpvPlaybackInfo ReadInfo()
    {
        lock (_commandLock)
        {
            if (_mpv == IntPtr.Zero)
                return default;

            _ = mpv_get_property(_mpv, "time-pos", MpvFormatDouble, out double pos);
            _ = mpv_get_property(_mpv, "duration", MpvFormatDouble, out double duration);
            _ = mpv_get_property(_mpv, "pause", MpvFormatFlag, out int paused);
            _ = mpv_get_property(_mpv, "core-idle", MpvFormatFlag, out int idle);

            return new MpvPlaybackInfo(pos, duration, paused == 1, idle == 1);
        }
    }

    internal bool TryCopyLatestFrame(byte[] destination)
    {
        lock (_frameLock)
        {
            if (_frontFrame is null || FrameVersion <= 0 || destination.Length < _frontFrame.Length)
                return false;

            Buffer.BlockCopy(_frontFrame, 0, destination, 0, _frontFrame.Length);
            return true;
        }
    }

    private void EventLoop()
    {
        try
        {
            while (!_disposed && _mpv != IntPtr.Zero)
                _ = mpv_wait_event(_mpv, 0.2);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _log.Debug(ex, "PrismCast mpv event loop stopped");
        }
    }

    private void RenderLoop()
    {
        while (!_disposed)
        {
            _frameSignal.WaitOne(250);
            if (_disposed || _render == IntPtr.Zero)
                continue;

            var flags = mpv_render_context_update(_render);
            if ((flags & 1UL) == 0)
                continue;

            try
            {
                if (mpv_render_context_render(_render, _renderParams) < 0)
                    continue;

                var back = _backFrame;
                if (back is null)
                    continue;

                Marshal.Copy(_frame, back, 0, _bytes);
                lock (_frameLock)
                {
                    (_frontFrame, _backFrame) = (_backFrame, _frontFrame);
                    Interlocked.Increment(ref _frameVersion);
                }
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "PrismCast mpv render skipped");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _frameSignal.Set();
        _renderThread?.Join(1000);
        _eventThread?.Join(1000);

        if (_render != IntPtr.Zero)
        {
            mpv_render_context_free(_render);
            _render = IntPtr.Zero;
        }

        lock (_commandLock)
        {
            if (_mpv != IntPtr.Zero)
            {
                mpv_terminate_destroy(_mpv);
                _mpv = IntPtr.Zero;
            }
        }

        Free(ref _frame);
        Free(ref _renderParams);
        Free(ref _size);
        Free(ref _stride);
        Free(ref _format);

        _frontFrame = null;
        _backFrame = null;
        _frameSignal.Dispose();
    }

    private static void Free(ref IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return;

        Marshal.FreeHGlobal(pointer);
        pointer = IntPtr.Zero;
    }
}
