using System.Globalization;
using System.Runtime.InteropServices;
using PitVoice.Core.Vr;

namespace PitVoice.VrOverlay;

/// <summary>
/// The real OpenVR (add-vr-overlay D2): the exports of openvr_api.dll and the function tables <c>FnTable:IVRSystem_022</c>
/// and <c>FnTable:IVROverlay_027</c> of OpenVR SDK 2.5.1. Slot numbers are the positions of the functions in
/// <c>VR_IVRSystem_FnTable</c> and <c>VR_IVROverlay_FnTable</c> of headers/openvr_capi.h of that version. Frames reach the
/// overlay as a Direct3D 11 texture (<see cref="D3d11Texture"/>, D5).
/// </summary>
internal sealed unsafe partial class OpenVrApi : IOpenVr
{
    public const string SystemVersion = "IVRSystem_022";
    public const string OverlayVersion = "IVROverlay_027";

    private const string OverlayKey = "pitvoice.indicator";
    private const string OverlayName = "PitVoice";

    // EVRApplicationType: VRApplication_Background never starts SteamVR (D3); it creates overlays (stand, 2026-10-24).
    private const int ApplicationBackground = 3;
    private const uint TrackedDeviceHmd = 0;
    private const uint EventQuit = 700;

    // ETextureType TextureType_DirectX, EColorSpace ColorSpace_Auto (8-bit UNORM is taken as gamma).
    private const int TextureTypeDirectX = 0;
    private const int ColorSpaceAuto = 0;

    // sizeof(VREvent_t) on Windows: three 4-byte fields, padding to 8, a union of 48 bytes.
    private const int EventSize = 64;

    // Slots in VR_IVRSystem_FnTable.
    private const int SystemGetDxgiOutputInfo = 7;
    private const int SystemPollNextEvent = 29;
    private const int SystemAcknowledgeQuitExiting = 43;

    // Slots in VR_IVROverlay_FnTable.
    private const int OverlayCreate = 1;
    private const int OverlayDestroy = 2;
    private const int OverlaySetWidthInMeters = 21;
    private const int OverlaySetTransformTrackedDeviceRelative = 34;
    private const int OverlayShow = 41;
    private const int OverlayHide = 42;
    private const int OverlaySetTexture = 58;

    private nint* _system;
    private nint* _overlay;
    private ulong _handle;
    private D3d11Texture? _texture;
    private bool _initialized;

    public VrFailure? Init()
    {
        try
        {
            Native.InitInternal2(out var error, ApplicationBackground, null);
            if (error != 0)
            {
                return new VrFailure(VrExitCode.InitFailed, Code("init", error));
            }

            _initialized = true;
            foreach (var version in new[] { SystemVersion, OverlayVersion })
            {
                if (Native.IsInterfaceVersionValid(version) == 0)
                {
                    return new VrFailure(VrExitCode.InterfaceVersion, "version-" + version);
                }
            }

            _system = (nint*)Native.GetGenericInterface("FnTable:" + SystemVersion, out error);
            if (_system is null || error != 0)
            {
                return new VrFailure(VrExitCode.InterfaceVersion, Code("system", error));
            }

            _overlay = (nint*)Native.GetGenericInterface("FnTable:" + OverlayVersion, out error);
            if (_overlay is null || error != 0)
            {
                return new VrFailure(VrExitCode.InterfaceVersion, Code("overlay", error));
            }

            return null;
        }
        catch (DllNotFoundException)
        {
            return new VrFailure(VrExitCode.NoLibrary, "no-openvr-api");
        }
        catch (EntryPointNotFoundException)
        {
            return new VrFailure(VrExitCode.NoLibrary, "bad-openvr-api");
        }
    }

    public VrFailure? CreateOverlay()
    {
        // The texture must live on the GPU the compositor uses, or SteamVR cannot read it.
        var adapter = -1;
        ((delegate* unmanaged<int*, void>)_system[SystemGetDxgiOutputInfo])(&adapter);
        _texture = D3d11Texture.Create(adapter, out var d3dFailure);
        if (_texture is null)
        {
            return new VrFailure(VrExitCode.OverlayFailed, d3dFailure);
        }

        ulong handle = 0;
        var key = Utf8(OverlayKey);
        var name = Utf8(OverlayName);
        int error;
        fixed (byte* k = key, n = name)
        {
            error = ((delegate* unmanaged<byte*, byte*, ulong*, int>)_overlay[OverlayCreate])(k, n, &handle);
        }

        if (error != 0)
        {
            return OverlayFailure("create", error);
        }

        _handle = handle;
        return null;
    }

    public VrFailure? Apply(VrMessage message)
    {
        int error;
        switch (message)
        {
            case VrFrame frame:
                if (_texture!.Upload(frame) is { } uploadFailure)
                {
                    return new VrFailure(VrExitCode.OverlayFailed, uploadFailure);
                }

                var texture = new Texture { Handle = _texture.Handle, Type = TextureTypeDirectX, ColorSpace = ColorSpaceAuto };
                error = ((delegate* unmanaged<ulong, Texture*, int>)_overlay[OverlaySetTexture])(_handle, &texture);
                return error == 0 ? null : OverlayFailure("frame", error);

            case VrVisible visible:
                error = ((delegate* unmanaged<ulong, int>)_overlay[visible.Visible ? OverlayShow : OverlayHide])(_handle);
                return error == 0 ? null : OverlayFailure("visible", error);

            case VrPlace place:
                error = ((delegate* unmanaged<ulong, float, int>)_overlay[OverlaySetWidthInMeters])(_handle, OverlayPose.WidthInMeters(place.Width));
                if (error != 0)
                {
                    return OverlayFailure("width", error);
                }

                var matrix = OverlayPose.Matrix(place.Yaw, place.Pitch);
                fixed (float* m = matrix)
                {
                    error = ((delegate* unmanaged<ulong, uint, float*, int>)_overlay[OverlaySetTransformTrackedDeviceRelative])(_handle, TrackedDeviceHmd, m);
                }

                return error == 0 ? null : OverlayFailure("place", error);

            default:
                return null;
        }
    }

    public bool PollQuit()
    {
        var quit = false;
        var buffer = stackalloc byte[EventSize];
        var poll = (delegate* unmanaged<byte*, uint, byte>)_system[SystemPollNextEvent];
        while (poll(buffer, EventSize) != 0)
        {
            quit |= *(uint*)buffer == EventQuit;
        }

        return quit;
    }

    public void AcknowledgeQuit() => ((delegate* unmanaged<void>)_system[SystemAcknowledgeQuitExiting])();

    public void Shutdown()
    {
        if (_handle != 0 && _overlay is not null)
        {
            ((delegate* unmanaged<ulong, int>)_overlay[OverlayDestroy])(_handle);
            _handle = 0;
        }

        _texture?.Dispose();
        _texture = null;
        if (_initialized)
        {
            _initialized = false;
            Native.ShutdownInternal();
        }
    }

    private static VrFailure OverlayFailure(string what, int error) => new(VrExitCode.OverlayFailed, Code(what, error));

    private static string Code(string what, int error) => string.Create(CultureInfo.InvariantCulture, $"{what}-{error}");

    private static byte[] Utf8(string text) => [.. System.Text.Encoding.UTF8.GetBytes(text), 0];

    /// <summary>Texture_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Texture
    {
        public nint Handle;
        public int Type;
        public int ColorSpace;
    }

    private static partial class Native
    {
        private const string Library = "openvr_api";

        [LibraryImport(Library, EntryPoint = "VR_InitInternal2", StringMarshalling = StringMarshalling.Utf8)]
        public static partial uint InitInternal2(out int error, int applicationType, string? startupInfo);

        [LibraryImport(Library, EntryPoint = "VR_ShutdownInternal")]
        public static partial void ShutdownInternal();

        /// <summary>A C++ bool: one byte.</summary>
        [LibraryImport(Library, EntryPoint = "VR_IsInterfaceVersionValid", StringMarshalling = StringMarshalling.Utf8)]
        public static partial byte IsInterfaceVersionValid(string version);

        [LibraryImport(Library, EntryPoint = "VR_GetGenericInterface", StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint GetGenericInterface(string version, out int error);
    }
}
