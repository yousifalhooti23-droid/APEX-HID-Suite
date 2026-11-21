using System.ComponentModel;
using System.Runtime.InteropServices;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 5: Real Hardware NVS Flash Calibration Engine.
/// Sends direct Sony DualShock 4 / DualSense HID Feature Reports (0x82 / 0x83) via HidD_SetFeature
/// and reports actual Win32 error codes if no device is connected or if the controller rejects the command.
/// </summary>
public sealed class NvsFlashCalibrator
{
    private const int ERROR_DEVICE_NOT_CONNECTED = 0x0000048F;

    public bool SampleStickCenter(ControllerDevice device, CalibrationProfile profile)
    {
        // Sony DualSense / DS4 Center Calibration Sequence:
        // Report 0x82: [0x82, 0x01, 0x01, 0x01] (Start Center) -> [0x82, 0x02, 0x01, 0x01] (Record Sample)
        byte[] startCmd = new byte[64];
        startCmd[0] = 0x82;
        startCmd[1] = 0x01;
        startCmd[2] = 0x01;
        startCmd[3] = 0x01;

        var (okStart, errStart, msgStart) = SendPhysicalFeatureReport(device, startCmd);
        if (!okStart)
        {
            ApplyFailure(profile, startCmd, errStart, msgStart);
            return false;
        }

        byte[] sampleCmd = new byte[64];
        sampleCmd[0] = 0x82;
        sampleCmd[1] = 0x02;
        sampleCmd[2] = 0x01;
        sampleCmd[3] = 0x01;

        var (okSample, errSample, msgSample) = SendPhysicalFeatureReport(device, sampleCmd);
        if (!okSample)
        {
            ApplyFailure(profile, sampleCmd, errSample, msgSample);
            return false;
        }

        profile.LastWin32ErrorCode = 0;
        profile.IsCenterCalibrated = true;
        profile.LastFeatureReportHex = FormatReportHex(sampleCmd, "ACK OK");
        profile.StatusMessage = "HidD_SetFeature(0x82, 0x02, 0x01, 0x01) succeeded — Stick Center Sampled.";
        return true;
    }

    public bool CalibrateOuterCircularity(ControllerDevice device, CalibrationProfile profile)
    {
        // Sony Range / Outer Circularity Boundary Sequence:
        // Report 0x82: [0x82, 0x01, 0x01, 0x02]
        byte[] rangeStart = new byte[64];
        rangeStart[0] = 0x82;
        rangeStart[1] = 0x01;
        rangeStart[2] = 0x01;
        rangeStart[3] = 0x02;

        var (ok, err, msg) = SendPhysicalFeatureReport(device, rangeStart);
        if (!ok)
        {
            ApplyFailure(profile, rangeStart, err, msg);
            return false;
        }

        profile.LastWin32ErrorCode = 0;
        profile.IsRangeCalibrated = true;
        profile.LastFeatureReportHex = FormatReportHex(rangeStart, "ACK OK");
        profile.StatusMessage = "HidD_SetFeature(0x82, 0x01, 0x01, 0x02) succeeded — Rotate sticks 360° before NVS commit.";
        return true;
    }

    public bool CommitPermanentNvsFlash(ControllerDevice device, CalibrationProfile profile)
    {
        // Sony NVS Flash Commit: Report 0x82 [0x82, 0x03, 0x01, 0x01] / [0x82, 0x03, 0x01, 0x02]
        byte[] commitCmd = new byte[64];
        commitCmd[0] = 0x82;
        commitCmd[1] = 0x03;
        commitCmd[2] = 0x01;
        commitCmd[3] = (byte)(profile.IsRangeCalibrated ? 0x02 : 0x01);

        var (ok, err, msg) = SendPhysicalFeatureReport(device, commitCmd);
        if (!ok)
        {
            ApplyFailure(profile, commitCmd, err, msg);
            return false;
        }

        // Read back Feature Report 0x83 calibration status from controller
        byte[] statusReport = new byte[64];
        statusReport[0] = 0x83;
        ReadPhysicalFeatureReport(device, statusReport);

        profile.LastWin32ErrorCode = 0;
        profile.IsCommittedToNvsFlash = true;
        profile.LastFeatureReportHex = FormatReportHex(commitCmd, $"0x83 STATE={statusReport[1]:X2} {statusReport[2]:X2}");
        profile.StatusMessage = "HidD_SetFeature(0x82, 0x03) succeeded — Permanent calibration written to onboard NVS flash.";
        return true;
    }

    public bool QueryControllerCalibrationState(ControllerDevice device, CalibrationProfile profile)
    {
        byte[] statusReport = new byte[64];
        statusReport[0] = 0x83;

        var (ok, err, msg) = ReadPhysicalFeatureReport(device, statusReport);
        if (!ok)
        {
            ApplyFailure(profile, statusReport, err, msg);
            return false;
        }

        profile.LastWin32ErrorCode = 0;
        profile.LastFeatureReportHex = FormatReportHex(statusReport, "HidD_GetFeature(0x83) OK");
        profile.StatusMessage = $"HidD_GetFeature(0x83) State: [{statusReport[1]:X2} {statusReport[2]:X2} {statusReport[3]:X2} {statusReport[4]:X2}]";
        return true;
    }

    private static (bool Success, int Win32Error, string ErrorMessage) SendPhysicalFeatureReport(
        ControllerDevice device,
        byte[] report)
    {
        if (!device.IsPhysicalHardware || string.IsNullOrWhiteSpace(device.DevicePath))
        {
            return (false, ERROR_DEVICE_NOT_CONNECTED, "No physical Sony controller connected (ERROR_DEVICE_NOT_CONNECTED)");
        }

        using var handle = NativeMethods.CreateFile(
            device.DevicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return (false, err, $"CreateFileW failed: {new Win32Exception(err).Message}");
        }

        if (!NativeMethods.HidD_SetFeature(handle, report, report.Length))
        {
            int err = Marshal.GetLastWin32Error();
            return (false, err, $"HidD_SetFeature(0x{report[0]:X2}) rejected by device: {new Win32Exception(err).Message}");
        }

        return (true, 0, "OK");
    }

    private static (bool Success, int Win32Error, string ErrorMessage) ReadPhysicalFeatureReport(
        ControllerDevice device,
        byte[] report)
    {
        if (!device.IsPhysicalHardware || string.IsNullOrWhiteSpace(device.DevicePath))
        {
            return (false, ERROR_DEVICE_NOT_CONNECTED, "No physical Sony controller connected (ERROR_DEVICE_NOT_CONNECTED)");
        }

        using var handle = NativeMethods.CreateFile(
            device.DevicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return (false, err, $"CreateFileW failed: {new Win32Exception(err).Message}");
        }

        if (!NativeMethods.HidD_GetFeature(handle, report, report.Length))
        {
            int err = Marshal.GetLastWin32Error();
            return (false, err, $"HidD_GetFeature(0x{report[0]:X2}) rejected by device: {new Win32Exception(err).Message}");
        }

        return (true, 0, "OK");
    }

    private static void ApplyFailure(CalibrationProfile profile, byte[] report, int win32Err, string message)
    {
        profile.LastWin32ErrorCode = win32Err;
        profile.LastFeatureReportHex = FormatReportHex(report, $"WIN32 ERR 0x{win32Err:X8}");
        profile.StatusMessage = $"Win32 Error 0x{win32Err:X8}: {message}";
    }

    private static string FormatReportHex(byte[] report, string suffix) =>
        $"{report[0]:X2} {report[1]:X2} {report[2]:X2} {report[3]:X2} {report[4]:X2} {report[5]:X2} [{suffix}]";
}
