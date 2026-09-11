using Dalamud.Plugin.Services;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Buffer = SharpDX.Direct3D11.Buffer;
using D3D11Device = SharpDX.Direct3D11.Device;
using GameControl = FFXIVClientStructs.FFXIV.Client.Game.Control;
using GfxKernel = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace PrismCast.Playback;

internal sealed unsafe class WorldScreenRenderer : IDisposable
{
    private const float BaseWidth = 1.0f;
    private const float BaseHeight = 0.5625f;
    private const int CurveSegments = 24;
    private const int VertexCount = (CurveSegments + 1) * 2;
    private const float CurveDepth = 0.12f;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenParams
    {
        public Matrix4x4 WorldViewProj;
        public float Curvature;
        public Vector3 Padding;
    }

    private readonly IPluginLog _log;
    private readonly D3D11Device _device;
    private readonly VertexShader _vs;
    private readonly PixelShader _ps;
    private readonly SamplerState _sampler;
    private readonly RasterizerState _rasterizer;
    private readonly DepthStencilState _depthState;
    private readonly Buffer _constantBuffer;

    private Texture2D? _texture;
    private ShaderResourceView? _srv;

    internal Vector3 Position { get; private set; }
    internal float Yaw { get; private set; }
    internal float Pitch { get; private set; }
    internal float Roll { get; private set; }
    internal float Scale { get; private set; } = 1f;
    internal bool Curved { get; set; } = true;
    internal bool Visible { get; set; } = true;

    public WorldScreenRenderer(PrismDx dx, IPluginLog log)
    {
        _device = dx.Device;
        _log = log;

        const string shader = @"
#define SEGMENTS 24
cbuffer Params : register(b0) { row_major float4x4 wvp; float curvature; float3 padding; };
struct VOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
VOut VS(uint id : SV_VertexID)
{
    uint col = id / 2; uint row = id % 2;
    float x = -1.0 + 2.0 * (float)col / (float)SEGMENTS;
    float y = row == 0 ? -1.0 : 1.0;
    float z = curvature * x * x;
    VOut o;
    o.pos = mul(float4(x, y, z, 1), wvp);
    o.uv = float2((x + 1.0) * 0.5, row == 0 ? 1.0 : 0.0);
    return o;
}
Texture2D tex : register(t0);
SamplerState smp : register(s0);
float4 PS(VOut i, bool front : SV_IsFrontFace) : SV_TARGET
{
    if (!front) return float4(0.05, 0.05, 0.05, 1);
    return tex.Sample(smp, i.uv);
}";

        using var vsb = ShaderBytecode.Compile(shader, "VS", "vs_4_0");
        using var psb = ShaderBytecode.Compile(shader, "PS", "ps_4_0");

        _vs = new VertexShader(_device, vsb);
        _ps = new PixelShader(_device, psb);
        _sampler = new SamplerState(_device, new SamplerStateDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = Comparison.Never,
            MinimumLod = 0,
            MaximumLod = float.MaxValue
        });
        _rasterizer = new RasterizerState(_device, new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            IsDepthClipEnabled = true
        });
        _depthState = new DepthStencilState(_device, new DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthComparison = Comparison.GreaterEqual
        });
        _constantBuffer = new Buffer(_device, Marshal.SizeOf<ScreenParams>(), ResourceUsage.Default,
            BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
    }

    internal void SetTarget(Texture2D? texture)
    {
        if (ReferenceEquals(texture, _texture))
            return;

        _srv?.Dispose();
        _srv = null;
        _texture = texture;

        if (texture is not null)
            _srv = new ShaderResourceView(_device, texture);
    }

    internal void SetTransform(Vector3 position, float yaw, float pitch, float roll, float scale)
    {
        Position = position;
        Yaw = yaw;
        Pitch = pitch;
        Roll = roll;
        Scale = Math.Clamp(scale, 0.1f, 8f);
    }

    internal void Draw()
    {
        if (!Visible || _srv is null)
            return;

        if (!TryGetSceneTargets(out var target))
            return;

        var wvp = ComputeWvp();
        if (wvp is null)
            return;

        try
        {
            Marshal.AddRef(target.ColorTexture);
            Marshal.AddRef(target.DepthTexture);

            using var color = new Texture2D(target.ColorTexture);
            using var depth = new Texture2D(target.DepthTexture);
            using var rtv = new RenderTargetView(_device, color);
            using var dsv = TryCreateDepthView(_device, depth);
            if (dsv is null)
                return;

            var ctx = _device.ImmediateContext;
            var previousRtvs = ctx.OutputMerger.GetRenderTargets(1, out var previousDsv);
            var previousVs = ctx.VertexShader.Get();
            var previousPs = ctx.PixelShader.Get();
            var previousLayout = ctx.InputAssembler.InputLayout;
            var previousTopology = ctx.InputAssembler.PrimitiveTopology;
            var previousRasterizer = ctx.Rasterizer.State;
            var previousDepth = ctx.OutputMerger.DepthStencilState;

            try
            {
                var p = new ScreenParams
                {
                    WorldViewProj = wvp.Value,
                    Curvature = Curved ? CurveDepth : 0
                };

                ctx.UpdateSubresource(ref p, _constantBuffer);
                ctx.OutputMerger.SetRenderTargets(dsv, rtv);
                ctx.OutputMerger.DepthStencilState = _depthState;
                ctx.Rasterizer.SetViewport(0, 0, target.Width, target.Height, 0, 1);
                ctx.Rasterizer.State = _rasterizer;
                ctx.InputAssembler.InputLayout = null;
                ctx.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
                ctx.VertexShader.Set(_vs);
                ctx.VertexShader.SetConstantBuffer(0, _constantBuffer);
                ctx.PixelShader.Set(_ps);
                ctx.PixelShader.SetShaderResource(0, _srv);
                ctx.PixelShader.SetSampler(0, _sampler);
                ctx.Draw(VertexCount, 0);
                ctx.PixelShader.SetShaderResource(0, null);
            }
            finally
            {
                ctx.OutputMerger.SetRenderTargets(previousDsv, previousRtvs);
                foreach (var view in previousRtvs)
                    view?.Dispose();

                previousDsv?.Dispose();
                ctx.VertexShader.Set(previousVs);
                previousVs?.Dispose();
                ctx.PixelShader.Set(previousPs);
                previousPs?.Dispose();
                ctx.InputAssembler.InputLayout = previousLayout;
                previousLayout?.Dispose();
                ctx.InputAssembler.PrimitiveTopology = previousTopology;
                ctx.Rasterizer.State = previousRasterizer;
                previousRasterizer?.Dispose();
                ctx.OutputMerger.DepthStencilState = previousDepth;
                previousDepth?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "PrismCast world screen draw skipped");
        }
    }

    private Matrix4x4? ComputeWvp()
    {
        var manager = GameControl.CameraManager.Instance();
        if (manager == null)
            return null;

        var active = manager->GetActiveCamera();
        if (active == null)
            return null;

        var scene = &active->CameraBase.SceneCamera;
        var render = scene->RenderCamera;
        if (render == null)
            return null;

        var view = Matrix4x4.CreateLookAt(
            ToNumerics(scene->Position),
            ToNumerics(scene->LookAtVector),
            Vector3.UnitY);

        var projection = CreateReversedZ(
            render->FoV,
            render->AspectRatio,
            render->NearPlane,
            render->FarPlane);

        var world =
            Matrix4x4.CreateScale(BaseWidth * Scale, BaseHeight * Scale, Scale) *
            Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch, Roll) *
            Matrix4x4.CreateTranslation(Position);

        return world * view * projection;
    }

    private static Matrix4x4 CreateReversedZ(float fov, float aspect, float near, float far)
    {
        var y = 1f / MathF.Tan(fov / 2f);
        var x = y / aspect;
        return new Matrix4x4(
            x, 0, 0, 0,
            0, y, 0, 0,
            0, 0, near / (far - near), -1,
            0, 0, near * far / (far - near), 0);
    }

    private static Vector3 ToNumerics(FFXIVClientStructs.FFXIV.Common.Math.Vector3 value) =>
        Unsafe.As<FFXIVClientStructs.FFXIV.Common.Math.Vector3, Vector3>(ref value);

    private readonly record struct SceneTargets(nint ColorTexture, nint DepthTexture, uint Width, uint Height);

    private static bool TryGetSceneTargets(out SceneTargets target)
    {
        target = default;

        var device = GfxKernel.Device.Instance();
        if (device == null || device->SwapChain == null || device->SwapChain->BackBuffer == null)
            return false;

        var rtm = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance();
        if (rtm == null || rtm->DepthStencil == null)
            return false;

        var color = (nint)device->SwapChain->BackBuffer->D3D11Texture2D;
        var depth = (nint)rtm->DepthStencil->D3D11Texture2D;

        if (color == 0 || depth == 0 || device->SwapChain->Width == 0 || device->SwapChain->Height == 0)
            return false;

        target = new SceneTargets(color, depth, device->SwapChain->Width, device->SwapChain->Height);
        return true;
    }

    private static DepthStencilView? TryCreateDepthView(D3D11Device device, Texture2D texture)
    {
        try
        {
            var format = texture.Description.Format switch
            {
                Format.R32G8X24_Typeless or Format.D32_Float_S8X24_UInt => Format.D32_Float_S8X24_UInt,
                Format.R32_Typeless or Format.D32_Float => Format.D32_Float,
                Format.R24G8_Typeless or Format.D24_UNorm_S8_UInt => Format.D24_UNorm_S8_UInt,
                Format.R16_Typeless or Format.D16_UNorm => Format.D16_UNorm,
                var f => f
            };

            var description = new DepthStencilViewDescription
            {
                Dimension = DepthStencilViewDimension.Texture2D,
                Format = format
            };
            description.Texture2D.MipSlice = 0;
            return new DepthStencilView(device, texture, description);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _srv?.Dispose();
        _constantBuffer.Dispose();
        _depthState.Dispose();
        _rasterizer.Dispose();
        _sampler.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }
}
