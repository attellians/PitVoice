using System.Globalization;
using System.Runtime.InteropServices;
using PitVoice.Core.Vr;

namespace PitVoice.VrOverlay;

/// <summary>
/// The picture of the overlay as a Direct3D 11 texture (add-vr-overlay D5): created once on the GPU that SteamVR draws
/// with, its pixels replaced for every frame with <c>UpdateSubresource</c>. SteamVR reads it on every compositor frame,
/// so a new frame never leaves the overlay without a picture, unlike <c>SetOverlayRaw</c>. The calls go through the
/// COM tables of ID3D11Device, ID3D11DeviceContext and IDXGIFactory1 by slot number, like the OpenVR function tables.
/// </summary>
internal sealed unsafe partial class D3d11Texture : IDisposable
{
    // IUnknown
    private const int Release = 2;

    // ID3D11Device
    private const int DeviceCreateTexture2D = 5;

    // ID3D11DeviceContext
    private const int ContextUpdateSubresource = 48;
    private const int ContextFlush = 111;

    // IDXGIFactory1
    private const int FactoryEnumAdapters1 = 12;

    private const int DriverTypeUnknown = 0;
    private const int DriverTypeHardware = 1;
    private const uint SdkVersion = 7;
    private const uint FormatR8G8B8A8Unorm = 28;
    private const uint BindShaderResource = 8;

    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private nint _device;
    private nint _context;
    private nint _texture;
    private int _width;
    private int _height;

    private D3d11Texture(nint device, nint context)
    {
        _device = device;
        _context = context;
    }

    /// <summary>The ID3D11Texture2D with the last frame, for <c>Texture_t.handle</c>.</summary>
    public nint Handle => _texture;

    /// <summary>A device on the adapter SteamVR reports, or on the default one when it reports none.</summary>
    /// <param name="adapterIndex">Index from <c>IVRSystem::GetDXGIOutputInfo</c>; -1 for the default adapter.</param>
    /// <param name="failure">Why the device could not be made.</param>
    public static D3d11Texture? Create(int adapterIndex, out string failure)
    {
        failure = string.Empty;
        var adapter = adapterIndex >= 0 ? Adapter(adapterIndex) : 0;
        try
        {
            nint device, context;
            int level;
            var hr = Native.D3D11CreateDevice(adapter, adapter != 0 ? DriverTypeUnknown : DriverTypeHardware, 0, 0, null, 0, SdkVersion, &device, &level, &context);
            if (hr < 0)
            {
                failure = Code("d3d11-device", hr);
                return null;
            }

            return new D3d11Texture(device, context);
        }
        finally
        {
            Free(adapter);
        }
    }

    /// <summary>Puts the frame into the texture, made again when the size changed; null on success.</summary>
    public string? Upload(VrFrame frame)
    {
        if (_texture == 0 || frame.Width != _width || frame.Height != _height)
        {
            Free(_texture);
            _texture = 0;
            var desc = new TextureDesc
            {
                Width = (uint)frame.Width,
                Height = (uint)frame.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = FormatR8G8B8A8Unorm,
                SampleCount = 1,
                BindFlags = BindShaderResource,
            };
            nint texture;
            var hr = ((delegate* unmanaged<nint, TextureDesc*, void*, nint*, int>)Slot(_device, DeviceCreateTexture2D))(_device, &desc, null, &texture);
            if (hr < 0)
            {
                return Code("d3d11-texture", hr);
            }

            (_texture, _width, _height) = (texture, frame.Width, frame.Height);
        }

        fixed (byte* pixels = frame.Rgba)
        {
            ((delegate* unmanaged<nint, nint, uint, void*, void*, uint, uint, void>)Slot(_context, ContextUpdateSubresource))(
                _context, _texture, 0, null, pixels, (uint)frame.Width * 4, 0);
        }

        // The update must reach the GPU before SteamVR reads the texture.
        ((delegate* unmanaged<nint, void>)Slot(_context, ContextFlush))(_context);
        return null;
    }

    public void Dispose()
    {
        Free(_texture);
        Free(_context);
        Free(_device);
        (_texture, _context, _device) = (0, 0, 0);
    }

    private static nint Adapter(int index)
    {
        nint factory;
        var id = FactoryId;
        if (Native.CreateDXGIFactory1(&id, &factory) < 0)
        {
            return 0;
        }

        try
        {
            nint adapter;
            return ((delegate* unmanaged<nint, uint, nint*, int>)Slot(factory, FactoryEnumAdapters1))(factory, (uint)index, &adapter) < 0 ? 0 : adapter;
        }
        finally
        {
            Free(factory);
        }
    }

    private static nint Slot(nint comObject, int slot) => (*(nint**)comObject)[slot];

    private static void Free(nint comObject)
    {
        if (comObject != 0)
        {
            ((delegate* unmanaged<nint, uint>)Slot(comObject, Release))(comObject);
        }
    }

    private static string Code(string what, int hr) => string.Create(CultureInfo.InvariantCulture, $"{what}-{hr:x8}");

    /// <summary>D3D11_TEXTURE2D_DESC.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    private static partial class Native
    {
        [LibraryImport("d3d11.dll")]
        public static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, uint* featureLevels, uint featureLevelCount, uint sdkVersion, nint* device, int* featureLevel, nint* context);

        [LibraryImport("dxgi.dll")]
        public static partial int CreateDXGIFactory1(Guid* riid, nint* factory);
    }
}
