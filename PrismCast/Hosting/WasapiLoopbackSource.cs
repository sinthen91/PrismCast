using System.Runtime.InteropServices;

namespace PrismCast.Hosting;

internal sealed record WasapiFormat(int SampleRate, int Channels, int BitsPerSample, string FfmpegSampleFormat, int BlockAlign);
internal sealed record WasapiEndpoint(string Id, string Name)
{
    internal bool IsDefault => string.IsNullOrWhiteSpace(Id);
}

internal sealed class WasapiLoopbackSource : IDisposable
{
    private const uint ClsctxAll = 23;
    private const uint StreamFlagsLoopback = 0x00020000;
    private const uint BufferFlagsSilent = 0x00000002;
    private const int ShareModeShared = 0;
    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientIid = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid PcmSubformat = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid FloatSubformat = new("00000003-0000-0010-8000-00AA00389B71");

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    internal WasapiFormat Format { get; }

    internal WasapiLoopbackSource(string? deviceId = null)
    {
        object? enumeratorObject = null;
        object? audioClientObject = null;
        object? captureClientObject = null;
        IntPtr formatPointer = IntPtr.Zero;
        try
        {
            var type = Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)
                       ?? throw new InvalidOperationException("Windows Core Audio is unavailable.");
            enumeratorObject = Activator.CreateInstance(type)
                               ?? throw new InvalidOperationException("Windows audio-device discovery failed.");
            _enumerator = enumeratorObject as IMMDeviceEnumerator
                          ?? throw new InvalidOperationException("Windows audio-device discovery returned an incompatible interface.");
            if (string.IsNullOrWhiteSpace(deviceId))
                Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device));
            else
                Marshal.ThrowExceptionForHR(_enumerator.GetDevice(deviceId, out _device));
            var audioClientIid = AudioClientIid;
            Marshal.ThrowExceptionForHR(_device.Activate(ref audioClientIid, ClsctxAll, IntPtr.Zero, out audioClientObject));
            _audioClient = audioClientObject as IAudioClient
                           ?? throw new InvalidOperationException("Windows returned an incompatible audio-client interface.");
            Marshal.ThrowExceptionForHR(_audioClient.GetMixFormat(out formatPointer));
            Format = ReadFormat(formatPointer);

            // A 100 ms shared-mode buffer gives FFmpeg enough tolerance without adding
            // another full second of latency to the HLS pipeline.
            Marshal.ThrowExceptionForHR(_audioClient.Initialize(ShareModeShared, StreamFlagsLoopback,
                1_000_000, 0, formatPointer, Guid.Empty));
            var captureClientIid = CaptureClientIid;
            Marshal.ThrowExceptionForHR(_audioClient.GetService(ref captureClientIid, out captureClientObject));
            _captureClient = captureClientObject as IAudioCaptureClient
                             ?? throw new InvalidOperationException("Windows returned an incompatible loopback-capture interface.");
        }
        catch
        {
            ReleaseCom(captureClientObject);
            ReleaseCom(audioClientObject);
            ReleaseCom(_device);
            ReleaseCom(enumeratorObject);
            _captureClient = null;
            _audioClient = null;
            _device = null;
            _enumerator = null;
            throw;
        }
        finally
        {
            if (formatPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(formatPointer);
        }
    }

    internal static IReadOnlyList<WasapiEndpoint> GetRenderEndpoints()
    {
        var result = new List<WasapiEndpoint> { new("", "Default Windows output") };
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            var type = Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)
                       ?? throw new InvalidOperationException("Windows Core Audio is unavailable.");
            enumerator = Activator.CreateInstance(type) as IMMDeviceEnumerator
                         ?? throw new InvalidOperationException("Windows audio-device discovery returned an incompatible interface.");
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.Render, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                IPropertyStore? properties = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    try
                    {
                        var name = value.VarType == 31 && value.PointerValue != IntPtr.Zero
                            ? Marshal.PtrToStringUni(value.PointerValue) : null;
                        result.Add(new WasapiEndpoint(id, string.IsNullOrWhiteSpace(name) ? id : name));
                    }
                    finally { _ = PropVariantClear(ref value); }
                }
                finally
                {
                    ReleaseCom(properties);
                    ReleaseCom(device);
                }
            }
        }
        catch
        {
            // Optional endpoint discovery must never prevent screen/window sharing.
            // The default Windows output remains usable if a driver or COM host
            // refuses to enumerate the additional playback endpoints.
            return result;
        }
        finally
        {
            ReleaseCom(collection);
            ReleaseCom(enumerator);
        }
        return result.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal void Start(Action<byte[]> onData)
    {
        if (_audioClient is null || _captureClient is null || _captureTask is not null)
            throw new InvalidOperationException("Windows loopback audio is not ready.");
        _cts = new CancellationTokenSource();
        Marshal.ThrowExceptionForHR(_audioClient.Start());
        _captureTask = Task.Run(() => CaptureLoop(onData, _cts.Token));
    }

    private async Task CaptureLoop(Action<byte[]> onData, CancellationToken ct)
    {
        var client = _captureClient!;
        while (!ct.IsCancellationRequested)
        {
            Marshal.ThrowExceptionForHR(client.GetNextPacketSize(out var packetFrames));
            if (packetFrames == 0)
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                continue;
            }

            Marshal.ThrowExceptionForHR(client.GetBuffer(out var data, out var frames, out var flags,
                out _, out _));
            try
            {
                var length = checked((int)frames * Format.BlockAlign);
                var bytes = new byte[length];
                if ((flags & BufferFlagsSilent) == 0 && data != IntPtr.Zero)
                    Marshal.Copy(data, bytes, 0, length);
                onData(bytes);
            }
            finally
            {
                Marshal.ThrowExceptionForHR(client.ReleaseBuffer(frames));
            }
        }
    }

    internal void Stop()
    {
        _cts?.Cancel();
        try { _captureTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _captureTask = null;
        try { _audioClient?.Stop(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        ReleaseCom(_captureClient);
        ReleaseCom(_audioClient);
        ReleaseCom(_device);
        ReleaseCom(_enumerator);
        _captureClient = null;
        _audioClient = null;
        _device = null;
        _enumerator = null;
    }

    private static WasapiFormat ReadFormat(IntPtr pointer)
    {
        var tag = (ushort)Marshal.ReadInt16(pointer, 0);
        var channels = (ushort)Marshal.ReadInt16(pointer, 2);
        var sampleRate = Marshal.ReadInt32(pointer, 4);
        var blockAlign = (ushort)Marshal.ReadInt16(pointer, 12);
        var bits = (ushort)Marshal.ReadInt16(pointer, 14);
        Guid? subformat = tag == 0xFFFE ? Marshal.PtrToStructure<Guid>(pointer + 24) : null;
        var isFloat = tag == 3 || subformat == FloatSubformat;
        var isPcm = tag == 1 || subformat == PcmSubformat;
        var ffmpeg = isFloat && bits == 32 ? "f32le" : isFloat && bits == 64 ? "f64le" :
            isPcm && bits == 8 ? "u8" : isPcm && bits == 16 ? "s16le" :
            isPcm && bits == 24 ? "s24le" : isPcm && bits == 32 ? "s32le" : null;
        if (channels is 0 or > 32 || sampleRate is < 8000 or > 384000 || blockAlign == 0 || ffmpeg is null)
            throw new NotSupportedException($"The Windows output audio format is unsupported ({channels} channels, {sampleRate} Hz, {bits}-bit). ");
        return new WasapiFormat(sampleRate, channels, bits, ffmpeg, blockAlign);
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            try { Marshal.FinalReleaseComObject(value); } catch { }
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        internal Guid FormatId;
        internal uint PropertyId;
        internal PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] internal ushort VarType;
        [FieldOffset(8)] internal IntPtr PointerValue;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsctx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity,
            IntPtr format, Guid sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags,
            out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
