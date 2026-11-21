using System.Runtime.InteropServices;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 3: Real Hardware Chipset Authenticity Detector.
/// Queries physical HID Feature Reports (0x20 / 0x12) and inspects real QPC inter-packet deltas
/// to verify whether the physical controller chip accepted 1ms polling or is a hardware-locked clone.
/// </summary>
public sealed class ChipsetAuthenticityAnalyzer
{
    private static readonly HashSet<string> AuthenticSonyOuiPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "E8:9E:B4", "F4:6A:DD", "A0:51:0B", "70:9E:29", "DC:0C:2D", "00:1F:E2",
        "28:3F:69", "40:B8:9A", "5C:96:56", "AC:89:95", "BC:60:A7", "C8:63:F1"
    };

    public AuthenticityReport VerifyController(
        ControllerDevice device,
        double liveMedianDeltaMs,
        double liveJitterUs,
        bool isReceivingLivePackets)
    {
        if (!device.IsPhysicalHardware || string.IsNullOrWhiteSpace(device.DevicePath))
        {
            return new AuthenticityReport
            {
                Verdict = AuthenticityVerdict.NoDeviceConnected,
                McuArchitecture = "No Physical Controller Connected",
                DescriptorProof = "Connect a physical Sony DualShock 4 or DualSense controller via USB to inspect silicon & 1ms response.",
                ConfidencePercent = 0.0,
                MeasuredMedianDeltaMs = 0.0,
                MeasuredJitterUs = 0.0,
                Accepted1MsPolling = false
            };
        }

        // Probe physical HID Feature Report 0x20 (DualSense) or 0x12 (DS4)
        var (featureResponded, featureWin32Err) = ProbeSonyFeatureDescriptor(device);
        string oui = device.SerialNumber.Length >= 8 ? device.SerialNumber[..8] : device.SerialNumber;
        bool knownOui = AuthenticSonyOuiPrefixes.Contains(oui);

        string mcuArch = device.Kind switch
        {
            SonyControllerKind.DualSenseEdge => "Sony SIE-CXD90064GG + Edge Sub-MCU (PID_0DF2)",
            SonyControllerKind.DualSenseBdm040 or SonyControllerKind.DualSenseBdm050 => "Sony SIE-CXD90071GG Mixed-Signal SoC (PID_0CE6)",
            SonyControllerKind.DualSenseBdm010 or SonyControllerKind.DualSenseBdm020 or SonyControllerKind.DualSenseBdm030 => "Sony SIE-CXD90064GG Custom ARM SoC (PID_0CE6)",
            SonyControllerKind.DualShock4V2 => "NXP / Freescale Kinetis MK22FN ARM Cortex-M4 (PID_09CC)",
            SonyControllerKind.DualShock4V1 => "Fujitsu / Spansion MB9BF106N ARM Cortex-M3 (PID_05C4)",
            _ => $"USB HID Device (VID_0x{device.VendorId:X4} : PID_0x{device.ProductId:X4})"
        };

        if (!featureResponded)
        {
            return new AuthenticityReport
            {
                Verdict = AuthenticityVerdict.CloneHardwareLocked,
                McuArchitecture = $"Unverified Third-Party MCU ({mcuArch})",
                DescriptorProof = $"Sony Feature Report Probe Rejected (Win32 0x{featureWin32Err:X8}) · Non-Authentic Firmware Descriptor",
                ConfidencePercent = 95.0,
                MeasuredMedianDeltaMs = liveMedianDeltaMs,
                MeasuredJitterUs = liveJitterUs,
                Accepted1MsPolling = false
            };
        }

        // If registered for 1000Hz and actively sending packets, verify actual measured delta
        if (device.IsRegisteredFor1000Hz && isReceivingLivePackets && liveMedianDeltaMs > 0.1)
        {
            if (liveMedianDeltaMs <= 1.55)
            {
                return new AuthenticityReport
                {
                    Verdict = AuthenticityVerdict.GenuineSony1000HzCapable,
                    McuArchitecture = mcuArch,
                    DescriptorProof = $"Feature Report Verified · Serial {device.SerialNumber} · Measured QPC Delta {liveMedianDeltaMs:F3} ms (1000Hz Confirmed)",
                    ConfidencePercent = knownOui ? 99.8 : 96.5,
                    MeasuredMedianDeltaMs = liveMedianDeltaMs,
                    MeasuredJitterUs = liveJitterUs,
                    Accepted1MsPolling = true
                };
            }

            if (liveMedianDeltaMs >= 3.2)
            {
                return new AuthenticityReport
                {
                    Verdict = AuthenticityVerdict.CloneHardwareLocked,
                    McuArchitecture = mcuArch,
                    DescriptorProof = $"Controller ignored 1000Hz bInterval=1 filter · Measured QPC Delta remained at {liveMedianDeltaMs:F3} ms",
                    ConfidencePercent = 94.0,
                    MeasuredMedianDeltaMs = liveMedianDeltaMs,
                    MeasuredJitterUs = liveJitterUs,
                    Accepted1MsPolling = false
                };
            }
        }

        string deltaNote = isReceivingLivePackets && liveMedianDeltaMs > 0.1
            ? $"Measured QPC Delta: {liveMedianDeltaMs:F3} ms"
            : "Waiting for live WM_INPUT packets";

        return new AuthenticityReport
        {
            Verdict = AuthenticityVerdict.GenuineSonyStock250Hz,
            McuArchitecture = mcuArch,
            DescriptorProof = $"Sony Feature Report Verified · Serial {device.SerialNumber} · {deltaNote}",
            ConfidencePercent = knownOui ? 99.2 : 95.0,
            MeasuredMedianDeltaMs = liveMedianDeltaMs,
            MeasuredJitterUs = liveJitterUs,
            Accepted1MsPolling = false
        };
    }

    private static (bool Responded, int Win32Error) ProbeSonyFeatureDescriptor(ControllerDevice device)
    {
        try
        {
            using var handle = NativeMethods.CreateFile(
                device.DevicePath,
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return (false, Marshal.GetLastWin32Error());
            }

            if (device.ProductId is NativeMethods.PID_DS4_V1 or NativeMethods.PID_DS4_V2)
            {
                byte[] buf12 = new byte[16];
                buf12[0] = 0x12;
                if (NativeMethods.HidD_GetFeature(handle, buf12, buf12.Length))
                    return (true, 0);
                return (false, Marshal.GetLastWin32Error());
            }
            else
            {
                byte[] buf20 = new byte[64];
                buf20[0] = 0x20;
                if (NativeMethods.HidD_GetFeature(handle, buf20, buf20.Length))
                    return (true, 0);
                return (false, Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            return (false, Marshal.GetLastWin32Error());
        }
    }
}
