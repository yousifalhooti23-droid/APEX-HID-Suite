using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 1: Multi-Revision Physical Sony Controller Detection, SetupAPI Instance Resolution &amp; WM_DEVICECHANGE / WM_INPUT Hook.
/// Detects real PS4 V1 (05C4), PS4 V2 (09CC), DualSense BDM-010..050 (0CE6), and DualSense Edge (0DF2).
/// Zero fake or simulated profiles.
/// </summary>
public sealed class SonyControllerDetector : IDisposable
{
    private IntPtr _notificationHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;

    public event EventHandler<IReadOnlyList<ControllerDevice>>? ControllersChanged;
    public event Action<IntPtr>? RawInputPacketArrived;

    public void AttachWindowHotplugAndRawInputHook(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        // 1. Register for WM_DEVICECHANGE HID hotplug events
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        var dbi = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = hidGuid,
            dbcc_name = string.Empty
        };

        IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
        try
        {
            Marshal.StructureToPtr(dbi, buffer, false);
            _notificationHandle = NativeMethods.RegisterDeviceNotification(hwnd, buffer, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // 2. Register for real WM_INPUT RawInput packets (HID Gamepad 0x01/0x05 & Joystick 0x01/0x04)
        var rawDevices = new NativeMethods.RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x01,
                usUsage = 0x05, // Gamepad
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = hwnd
            },
            new()
            {
                usUsagePage = 0x01,
                usUsage = 0x04, // Joystick
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = hwnd
            }
        };

        NativeMethods.RegisterRawInputDevices(
            rawDevices,
            (uint)rawDevices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    public IReadOnlyList<ControllerDevice> ScanSonyControllers()
    {
        return EnumeratePhysicalSonyHidDevices();
    }

    private static List<ControllerDevice> EnumeratePhysicalSonyHidDevices()
    {
        var results = new List<ControllerDevice>();
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);

        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return results;

        try
        {
            uint index = 0;
            while (true)
            {
                var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    break;
                }

                index++;
                var (devicePath, instanceId) = GetDeviceInterfacePathAndInstanceId(deviceInfoSet, ref interfaceData);
                if (string.IsNullOrWhiteSpace(devicePath))
                    continue;

                var controller = TryInspectSonyDevice(devicePath, instanceId);
                if (controller != null)
                {
                    results.Add(controller);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static (string? DevicePath, string InstanceId) GetDeviceInterfacePathAndInstanceId(
        IntPtr deviceInfoSet,
        ref NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        var devInfoData = new NativeMethods.SP_DEVINFO_DATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
        };

        NativeMethods.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out uint requiredSize,
            ref devInfoData);

        if (requiredSize == 0 || requiredSize > 4096)
            return (null, string.Empty);

        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailBuffer,
                    requiredSize,
                    out _,
                    ref devInfoData))
            {
                return (null, string.Empty);
            }

            IntPtr pDevicePath = IntPtr.Add(detailBuffer, 4);
            string? devicePath = Marshal.PtrToStringUni(pDevicePath);

            char[] instanceBuf = new char[512];
            string instanceId = string.Empty;
            if (NativeMethods.SetupDiGetDeviceInstanceId(
                    deviceInfoSet,
                    ref devInfoData,
                    instanceBuf,
                    (uint)instanceBuf.Length,
                    out uint instLen) && instLen > 1)
            {
                instanceId = new string(instanceBuf, 0, (int)instLen - 1);
            }

            return (devicePath, instanceId);
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static ControllerDevice? TryInspectSonyDevice(string devicePath, string instanceId)
    {
        using var handle = NativeMethods.CreateFile(
            devicePath,
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        var attr = new NativeMethods.HIDD_ATTRIBUTES
        {
            Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>()
        };

        if (!NativeMethods.HidD_GetAttributes(handle, ref attr))
            return null;

        if (attr.VendorID != NativeMethods.SONY_VID)
            return null;

        if (attr.ProductID is not (
            NativeMethods.PID_DS4_V1 or
            NativeMethods.PID_DS4_V2 or
            NativeMethods.PID_DUALSENSE or
            NativeMethods.PID_DUALSENSE_EDGE))
        {
            return null;
        }

        string serial = ReadPhysicalSerialNumber(handle, attr.ProductID, instanceId);
        var (kind, displayName, boardRevision, fwVersion) = ClassifySonyRevision(handle, attr.ProductID, attr.VersionNumber);

        return new ControllerDevice
        {
            DevicePath = devicePath,
            DeviceInstanceId = instanceId,
            VendorId = attr.VendorID,
            ProductId = attr.ProductID,
            BcdDevice = attr.VersionNumber,
            Kind = kind,
            DisplayName = displayName,
            BoardRevision = boardRevision,
            SerialNumber = serial,
            FirmwareVersion = fwVersion,
            IsPhysicalHardware = true
        };
    }

    private static string ReadPhysicalSerialNumber(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        ushort pid,
        string instanceId)
    {
        byte[] serialBuf = new byte[256];
        if (NativeMethods.HidD_GetSerialNumberString(handle, serialBuf, serialBuf.Length))
        {
            string raw = Encoding.Unicode.GetString(serialBuf).TrimEnd('\0').Trim();
            if (!string.IsNullOrWhiteSpace(raw))
                return FormatMacOrSerial(raw);
        }

        // DualShock 4 Feature Report 0x12 (Hardware MAC address)
        if (pid is NativeMethods.PID_DS4_V1 or NativeMethods.PID_DS4_V2)
        {
            byte[] feature12 = new byte[16];
            feature12[0] = 0x12;
            if (NativeMethods.HidD_GetFeature(handle, feature12, feature12.Length))
            {
                return $"{feature12[6]:X2}:{feature12[5]:X2}:{feature12[4]:X2}:{feature12[3]:X2}:{feature12[2]:X2}:{feature12[1]:X2}";
            }
        }

        // DualSense Feature Report 0x09 (Bluetooth MAC address)
        if (pid is NativeMethods.PID_DUALSENSE or NativeMethods.PID_DUALSENSE_EDGE)
        {
            byte[] feature09 = new byte[20];
            feature09[0] = 0x09;
            if (NativeMethods.HidD_GetFeature(handle, feature09, feature09.Length))
            {
                return $"{feature09[6]:X2}:{feature09[5]:X2}:{feature09[4]:X2}:{feature09[3]:X2}:{feature09[2]:X2}:{feature09[1]:X2}";
            }
        }

        // Extract USB instance token if controller firmware restricted string descriptor
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            int lastSlash = instanceId.LastIndexOf('\\');
            if (lastSlash >= 0 && lastSlash + 1 < instanceId.Length)
                return instanceId[(lastSlash + 1)..].ToUpperInvariant();
        }

        return "UNKNOWN-SERIAL";
    }

    private static (SonyControllerKind Kind, string Name, string BoardRev, string Firmware) ClassifySonyRevision(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        ushort pid,
        ushort bcdDevice)
    {
        if (pid == NativeMethods.PID_DS4_V1)
        {
            return (SonyControllerKind.DualShock4V1,
                "Sony DualShock 4 Wireless Controller V1 (CUH-ZCT1)",
                "JDM-001 / JDM-011 / JDM-020",
                $"FW 0x{bcdDevice:X4}");
        }

        if (pid == NativeMethods.PID_DS4_V2)
        {
            return (SonyControllerKind.DualShock4V2,
                "Sony DualShock 4 Wireless Controller V2 (CUH-ZCT2)",
                "JDM-040 / JDM-050 / JDM-055",
                $"FW 0x{bcdDevice:X4}");
        }

        if (pid == NativeMethods.PID_DUALSENSE_EDGE)
        {
            return (SonyControllerKind.DualSenseEdge,
                "Sony DualSense Edge Pro Controller (CFI-ZCP1)",
                "BDM-EDGE-010 (Modular Stick Architecture)",
                $"FW 0x{bcdDevice:X4}");
        }

        byte[] feature20 = new byte[64];
        feature20[0] = 0x20;
        byte hwMajor = (byte)((bcdDevice >> 8) & 0xFF);
        ushort fwBuild = bcdDevice;

        if (NativeMethods.HidD_GetFeature(handle, feature20, feature20.Length))
        {
            hwMajor = feature20[25];
            fwBuild = BitConverter.ToUInt16(feature20, 28);
        }

        return hwMajor switch
        {
            <= 1 => (SonyControllerKind.DualSenseBdm010, "Sony DualSense Wireless Controller", "BDM-010 (SIE CXD90064GG · Gen 1)", $"FW {fwBuild:X4}"),
            2 => (SonyControllerKind.DualSenseBdm020, "Sony DualSense Wireless Controller", "BDM-020 (SIE CXD90064GG · Gen 2)", $"FW {fwBuild:X4}"),
            3 => (SonyControllerKind.DualSenseBdm030, "Sony DualSense Wireless Controller", "BDM-030 (SIE CXD90064GG · Gen 3)", $"FW {fwBuild:X4}"),
            4 => (SonyControllerKind.DualSenseBdm040, "Sony DualSense Wireless Controller", "BDM-040 (SIE CXD90071GG · Gen 4)", $"FW {fwBuild:X4}"),
            _ => (SonyControllerKind.DualSenseBdm050, "Sony DualSense Wireless Controller", "BDM-050 (SIE CXD90071GG · Gen 5)", $"FW {fwBuild:X4}")
        };
    }

    private static string FormatMacOrSerial(string raw)
    {
        string clean = raw.Replace("-", "").Replace(":", "").ToUpperInvariant();
        if (clean.Length == 12 && clean.All(Uri.IsHexDigit))
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
        }
        return raw.ToUpperInvariant();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            RawInputPacketArrived?.Invoke(lParam);
        }
        else if (msg == NativeMethods.WM_DEVICECHANGE)
        {
            int eventType = wParam.ToInt32();
            if (eventType is NativeMethods.DBT_DEVICEARRIVAL
                or NativeMethods.DBT_DEVICEREMOVECOMPLETE
                or NativeMethods.DBT_DEVNODES_CHANGED)
            {
                var devices = ScanSonyControllers();
                ControllersChanged?.Invoke(this, devices);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_notificationHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }
        _hwndSource?.RemoveHook(WndProc);
    }
}
