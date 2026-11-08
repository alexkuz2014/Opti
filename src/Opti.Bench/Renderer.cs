using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Opti.Bench;

[StructLayout(LayoutKind.Sequential)]
internal struct FrameConstants
{
    public Matrix4x4 ViewProj;
    public float Time;
    public float Load;
    public uint GridX;
    public uint GridY;
}

/// <summary>D3D11-сцена: экземпляры вращающихся кубов, flip-модель, вывод без VSync (с разрывами, если поддерживается).</summary>
internal sealed class Renderer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _ctx;
    private readonly IDXGISwapChain1 _swap;
    private readonly ID3D11RenderTargetView _rtv;
    private readonly ID3D11Texture2D _depth;
    private readonly ID3D11DepthStencilView _dsv;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11Buffer _cb;
    private readonly ID3D11RasterizerState _raster;
    private readonly ID3D11DepthStencilState _depthState;
    private readonly bool _tearing;
    private readonly int _w, _h;

    public string AdapterName { get; }
    public bool Tearing => _tearing;

    public Renderer(IntPtr hwnd, int width, int height, bool preferHighPerformance)
    {
        _w = width; _h = height;
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();

        IDXGIAdapter1? adapter = null;
        using (var f6 = factory.QueryInterfaceOrNull<IDXGIFactory6>())
        {
            if (f6 is not null)
                adapter = f6.EnumAdapterByGpuPreference<IDXGIAdapter1>(0, preferHighPerformance ? GpuPreference.HighPerformance : GpuPreference.Unspecified);
        }
        if (adapter is null) factory.EnumAdapters1(0, out adapter).CheckError();
        AdapterName = adapter!.Description1.Description;

        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out _device!, out _ctx!).CheckError();
        adapter.Dispose();

        using (var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
            _tearing = f5?.PresentAllowTearing ?? false;

        var desc = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 3,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipDiscard,
            Scaling = Scaling.Stretch,
            AlphaMode = AlphaMode.Ignore,
            Flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        };
        _swap = factory.CreateSwapChainForHwnd(_device, hwnd, desc);
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

        using (var back = _swap.GetBuffer<ID3D11Texture2D>(0)) _rtv = _device.CreateRenderTargetView(back);
        _depth = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = width, Height = height, MipLevels = 1, ArraySize = 1, Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
        });
        _dsv = _device.CreateDepthStencilView(_depth);

        var src = Shaders.Source();
        var vsCode = Compiler.Compile(src, "VSMain", "bench.hlsl", "vs_5_0");
        var psCode = Compiler.Compile(src, "PSMain", "bench.hlsl", "ps_5_0");
        _vs = _device.CreateVertexShader(vsCode.Span);
        _ps = _device.CreatePixelShader(psCode.Span);

        _cb = _device.CreateBuffer(new BufferDescription(Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
        _raster = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);
    }

    public void Render(float time, int instances, float load, uint gridX, uint gridY)
    {
        var aspect = _w / (float)_h;
        var eye = new Vector3(MathF.Sin(time * 0.15f) * 60f, 25f, -95f);
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, aspect, 0.5f, 600f);
        var c = new FrameConstants { ViewProj = view * proj, Time = time, Load = load, GridX = gridX, GridY = gridY };
        _ctx.UpdateSubresource(in c, _cb);

        _ctx.OMSetRenderTargets(_rtv, _dsv);
        _ctx.ClearRenderTargetView(_rtv, new Vortice.Mathematics.Color4(0.05f, 0.06f, 0.08f, 1f));
        _ctx.ClearDepthStencilView(_dsv, DepthStencilClearFlags.Depth, 1f, 0);
        _ctx.RSSetViewport(0, 0, _w, _h);
        _ctx.RSSetState(_raster);
        _ctx.OMSetDepthStencilState(_depthState);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.VSSetConstantBuffer(0, _cb);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetConstantBuffer(0, _cb);
        _ctx.DrawInstanced(36, instances, 0, 0);
    }

    public void Present() => _swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None);

    public void Dispose()
    {
        _ctx.ClearState();
        _depthState.Dispose(); _raster.Dispose(); _cb.Dispose(); _ps.Dispose(); _vs.Dispose();
        _dsv.Dispose(); _depth.Dispose(); _rtv.Dispose(); _swap.Dispose(); _ctx.Dispose(); _device.Dispose();
    }
}
