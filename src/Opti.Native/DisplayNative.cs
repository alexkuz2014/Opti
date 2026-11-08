using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Opti.Native;

public sealed record DisplayMode(int Width, int Height, int Hz, int BitsPerPixel);

public sealed record DisplayDevice(string DeviceName, string AdapterName, bool IsPrimary, bool AttachedToDesktop);

public sealed record DisplayTargetInfo(string GdiDeviceName, string? FriendlyName, uint OutputTechnology, bool IsInternal,
    bool? HdrSupported, bool? HdrEnabled, double RefreshHz);

[SupportedOSPlatform("windows")]
public static class DisplayNative
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    private const uint CDS_UPDATEREGISTRY = 0x1, CDS_TEST = 0x2;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1, DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    private static DEVMODE NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    public static IReadOnlyList<DisplayDevice> GetDisplays()
    {
        var list = new List<DisplayDevice>();
        for (uint i = 0; ; i++)
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevicesW(null, i, ref d, 0)) break;
            bool attached = (d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            if (!attached) continue;
            list.Add(new DisplayDevice(d.DeviceName, d.DeviceString, (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0, attached));
        }
        return list;
    }

    public static DisplayMode? GetCurrentMode(string deviceName)
    {
        var dm = NewDevMode();
        return EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref dm)
            ? new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency, (int)dm.dmBitsPerPel)
            : null;
    }

    public static IReadOnlyList<DisplayMode> GetModes(string deviceName)
    {
        var list = new List<DisplayMode>();
        for (int i = 0; ; i++)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettingsW(deviceName, i, ref dm)) break;
            list.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency, (int)dm.dmBitsPerPel));
        }
        return list;
    }

    /// <summary>Меняет частоту при текущем разрешении. Сначала проверяет режим (CDS_TEST), затем применяет и сохраняет в реестр.</summary>
    public static void SetRefreshRate(string deviceName, int hz)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            throw new InvalidOperationException($"Не удалось прочитать режим {deviceName}");
        dm.dmDisplayFrequency = (uint)hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        int test = ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Режим {dm.dmPelsWidth}×{dm.dmPelsHeight}@{hz} Гц не поддерживается ({test})");
        int r = ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (r != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"ChangeDisplaySettingsEx вернул {r}");
    }

    // ---------- QueryDisplayConfig: встроенный ли экран, имя монитора, HDR ----------

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header; public uint flags; public uint outputTechnology;
        public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADVANCED_COLOR_INFO { public DEVICE_INFO_HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, GET_ADVANCED_COLOR_INFO = 9;
    private const uint TECH_INTERNAL = 0x80000000, TECH_LVDS = 6, TECH_DP_EMBEDDED = 11, TECH_UDI_EMBEDDED = 13;

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO info);

    public static IReadOnlyList<DisplayTargetInfo> GetTargets()
    {
        var result = new List<DisplayTargetInfo>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return result;
        var paths = new PATH_INFO[np];
        var modes = new MODE_INFO[nm];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;

        for (int i = 0; i < np; i++)
        {
            var path = paths[i];
            var src = new SOURCE_DEVICE_NAME { viewGdiDeviceName = "" };
            src.header = new DEVICE_INFO_HEADER { type = GET_SOURCE_NAME, size = (uint)Marshal.SizeOf<SOURCE_DEVICE_NAME>(), adapterId = path.sourceInfo.adapterId, id = path.sourceInfo.id };
            if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;

            var tgt = new TARGET_DEVICE_NAME { monitorFriendlyDeviceName = "", monitorDevicePath = "" };
            tgt.header = new DEVICE_INFO_HEADER { type = GET_TARGET_NAME, size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>(), adapterId = path.targetInfo.adapterId, id = path.targetInfo.id };
            string? friendly = DisplayConfigGetDeviceInfo(ref tgt) == 0 && tgt.monitorFriendlyDeviceName.Length > 0 ? tgt.monitorFriendlyDeviceName : null;

            bool? hdrSupported = null, hdrEnabled = null;
            var aci = new ADVANCED_COLOR_INFO();
            aci.header = new DEVICE_INFO_HEADER { type = GET_ADVANCED_COLOR_INFO, size = (uint)Marshal.SizeOf<ADVANCED_COLOR_INFO>(), adapterId = path.targetInfo.adapterId, id = path.targetInfo.id };
            if (DisplayConfigGetDeviceInfo(ref aci) == 0)
            {
                hdrSupported = (aci.value & 0x1) != 0;
                hdrEnabled = (aci.value & 0x2) != 0;
            }

            uint tech = path.targetInfo.outputTechnology;
            bool isInternal = tech is TECH_INTERNAL or TECH_LVDS or TECH_DP_EMBEDDED or TECH_UDI_EMBEDDED;
            var rr = path.targetInfo.refreshRate;
            double hz = rr.Denominator == 0 ? 0 : (double)rr.Numerator / rr.Denominator;
            result.Add(new DisplayTargetInfo(src.viewGdiDeviceName, friendly, tech, isInternal, hdrSupported, hdrEnabled, hz));
        }
        return result;
    }
}
