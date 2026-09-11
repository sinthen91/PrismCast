using Dalamud.Plugin;
using SharpDX.Direct3D11;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace PrismCast.Playback;

internal sealed class PrismDx : IDisposable
{
    internal D3D11Device Device { get; }

    public PrismDx(IDalamudPluginInterface pi)
    {
        Device = new D3D11Device(pi.UiBuilder.DeviceHandle);
    }

    public void Dispose()
    {
        Device.Dispose();
    }
}
