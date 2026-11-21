using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ApexHid.Core.Models;

public enum SonyControllerKind
{
    None,
    DualShock4V1,
    DualShock4V2,
    DualSenseBdm010,
    DualSenseBdm020,
    DualSenseBdm030,
    DualSenseBdm040,
    DualSenseBdm050,
    DualSenseEdge
}

public enum AuthenticityVerdict
{
    NoDeviceConnected,
    GenuineSony1000HzCapable,
    GenuineSonyStock250Hz,
    CloneHardwareLocked
}

public sealed class ControllerDevice : INotifyPropertyChanged
{
    private bool _isRegisteredFor1000Hz;
    private int _activeBInterval = 4;
    private int _targetPollingHz = 250;

    public string DevicePath { get; set; } = string.Empty;
    public string DeviceInstanceId { get; set; } = string.Empty;
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public ushort BcdDevice { get; set; }
    public SonyControllerKind Kind { get; set; } = SonyControllerKind.None;
    public string DisplayName { get; set; } = "No Controller Connected";
    public string BoardRevision { get; set; } = "Waiting for USB/HID Device...";
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = "—";
    public bool IsPhysicalHardware { get; set; }
    public string DriverBindingStatus { get; set; } = "Idle";

    public string VidPidFormatted => IsPhysicalHardware
        ? $"0x{VendorId:X4} : 0x{ProductId:X4}"
        : "—";

    public bool IsRegisteredFor1000Hz
    {
        get => _isRegisteredFor1000Hz;
        set
        {
            if (_isRegisteredFor1000Hz == value) return;
            _isRegisteredFor1000Hz = value;
            ActiveBInterval = value ? 1 : 4;
            TargetPollingHz = value ? 1000 : 250;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveBIntervalFormatted));
            OnPropertyChanged(nameof(TargetPollingFormatted));
        }
    }

    public int ActiveBInterval
    {
        get => _activeBInterval;
        set
        {
            if (_activeBInterval == value) return;
            _activeBInterval = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveBIntervalFormatted));
        }
    }

    public int TargetPollingHz
    {
        get => _targetPollingHz;
        set
        {
            if (_targetPollingHz == value) return;
            _targetPollingHz = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetPollingFormatted));
        }
    }

    public string ActiveBIntervalFormatted => !IsPhysicalHardware
        ? "Waiting for Hardware"
        : $"bInterval = {ActiveBInterval} ({(ActiveBInterval == 1 ? "1.00 ms · hidusbf SelectedRate=1000" : "4.00 ms · Stock 250Hz")})";

    public string TargetPollingFormatted => !IsPhysicalHardware
        ? "0 Hz (Disconnected)"
        : $"{TargetPollingHz} Hz ({1000.0 / TargetPollingHz:F2} ms)";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AuthenticityReport
{
    public AuthenticityVerdict Verdict { get; init; } = AuthenticityVerdict.NoDeviceConnected;
    public string McuArchitecture { get; init; } = "No Physical MCU Connected";
    public string DescriptorProof { get; init; } = "Connect a Sony DualShock 4 or DualSense controller via USB to verify silicon & bInterval timing.";
    public double ConfidencePercent { get; init; }
    public double MeasuredMedianDeltaMs { get; init; }
    public double MeasuredJitterUs { get; init; }
    public bool Accepted1MsPolling { get; init; }
}

public sealed class CalibrationProfile
{
    public short LeftCenterOffsetX { get; set; }
    public short LeftCenterOffsetY { get; set; }
    public short RightCenterOffsetX { get; set; }
    public short RightCenterOffsetY { get; set; }
    public double LeftCircularityErrorPercent { get; set; }
    public double RightCircularityErrorPercent { get; set; }
    public double LeftDriftPercent { get; set; }
    public double RightDriftPercent { get; set; }
    public bool IsCenterCalibrated { get; set; }
    public bool IsRangeCalibrated { get; set; }
    public bool IsCommittedToNvsFlash { get; set; }
    public int LastWin32ErrorCode { get; set; }
    public string LastFeatureReportHex { get; set; } = "Idle — No Feature Report Sent";
    public string StatusMessage { get; set; } = "Waiting for physical Sony controller connection...";
}

public sealed class SecurityShieldStatus
{
    public bool SecureBootEnabled { get; init; }
    public bool HvciEnabled { get; init; }
    public bool TestSigningDisabled { get; init; }
    public string KernelVerificationSource { get; init; } = "NtQuerySystemInformation(0x67) + HKLM\\SecureBoot\\State";
    public bool VanguardCompliant => SecureBootEnabled && TestSigningDisabled;
    public bool FaceItCompliant => SecureBootEnabled && HvciEnabled && TestSigningDisabled;
    public bool EacBattlEyeCompliant => TestSigningDisabled;
}
