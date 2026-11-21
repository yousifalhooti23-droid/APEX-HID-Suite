using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ApexHid.Core.Interop;
using ApexHid.Core.Localization;
using ApexHid.Core.Models;
using ApexHid.Core.Services;
using ApexHid.Core.Themes;

namespace ApexHid.App;

public partial class MainWindow : Window
{
    private readonly SonyControllerDetector _detector = new();
    private readonly ChipsetAuthenticityAnalyzer _authenticityAnalyzer = new();
    private readonly UsbPowerAndMmcssOptimizer _usbOptimizer = new();
    private readonly NvsFlashCalibrator _nvsCalibrator = new();
    private readonly MicrosecondOscilloscope _oscilloscope = new();
    private readonly AntiCheatPreFlightShield _securityShield = new();
    private readonly GitHubReleaseUpdater _releaseUpdater = new();
    private readonly CalibrationProfile _calibrationProfile = new();

    private ControllerDevice _activeDevice = new();
    private int _activeTabIndex;

    public LocalizationManager Loc => LocalizationManager.Instance;
    public ThemeManager Theme => ThemeManager.Instance;
    public SerialLockPollingEngine SerialEngine { get; } = new();

    public MainWindow()
    {
        Theme.InitializeDefaultTheme();
        InitializeComponent();
        DataContext = this;

        OscilloscopeWaveformView.Oscilloscope = _oscilloscope;
        CircularityHeatmapView.Oscilloscope = _oscilloscope;
        _oscilloscope.ActiveCalibration = _calibrationProfile;

        Loaded += OnMainWindowLoaded;
        Closed += OnMainWindowClosed;
        Loc.PropertyChanged += (_, _) => RefreshLocalizedDynamicUI();
        Theme.ThemeChanged += (_, _) =>
        {
            RefreshLocalizedDynamicUI();
            RunAuthenticityAnalysis();
            RefreshSecurityBadges();
        };
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.ApplyArcticWindowFrame(this, Theme.IsDarkMode);
        _detector.AttachWindowHotplugAndRawInputHook(this);

        // Wire real WM_INPUT packets directly to the QueryPerformanceCounter oscilloscope
        _detector.RawInputPacketArrived += hRawInput =>
        {
            _oscilloscope.ProcessRawInputMessage(hRawInput);
        };

        _detector.ControllersChanged += (_, list) =>
        {
            Dispatcher.Invoke(() => ApplyConnectedControllersList(list));
        };

        _usbOptimizer.ApplyLowLatencyUsbPowerProfile();
        RefreshSecurityBadges();

        var initialList = _detector.ScanSonyControllers();
        ApplyConnectedControllersList(initialList);

        CompositionTarget.Rendering += OnCompositionTargetRendering;
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _oscilloscope.Dispose();
        _usbOptimizer.Dispose();
        _detector.Dispose();
    }

    private void ApplyConnectedControllersList(IReadOnlyList<ControllerDevice> devices)
    {
        if (devices.Count == 0)
        {
            BindActiveController(new ControllerDevice
            {
                IsPhysicalHardware = false,
                DisplayName = "No Controller Connected",
                BoardRevision = "Waiting for USB/HID Device...",
                SerialNumber = string.Empty,
                FirmwareVersion = "—"
            });
            return;
        }

        // Apply per-controller Serial Number locking across all connected physical controllers:
        // Registered serials get hidusbf (SelectedRate=1000), unregistered get hidusbf removed (Stock 250Hz)
        foreach (var ctrl in devices)
        {
            SerialEngine.ApplySerialPolicy(ctrl);
        }

        BindActiveController(devices[0]);
    }

    private void BindActiveController(ControllerDevice device)
    {
        _activeDevice = device;
        SerialEngine.ApplySerialPolicy(_activeDevice);
        _oscilloscope.ActiveController = _activeDevice;
        _oscilloscope.ResetBuffers();

        ControllerModelValue.Text = _activeDevice.DisplayName;
        BoardRevisionValue.Text = _activeDevice.BoardRevision;
        VidPidValue.Text = _activeDevice.VidPidFormatted;
        SerialNumberValue.Text = string.IsNullOrWhiteSpace(_activeDevice.SerialNumber) ? "—" : _activeDevice.SerialNumber;
        FirmwareValue.Text = string.IsNullOrWhiteSpace(_activeDevice.DeviceInstanceId)
            ? _activeDevice.FirmwareVersion
            : $"{_activeDevice.FirmwareVersion} · {_activeDevice.DeviceInstanceId}";

        if (!string.IsNullOrWhiteSpace(_activeDevice.SerialNumber))
        {
            CustomSerialInputBox.Text = _activeDevice.SerialNumber;
        }

        RefreshLocalizedDynamicUI();
        RunAuthenticityAnalysis();
    }

    private void RefreshLocalizedDynamicUI()
    {
        bool dark = Theme.IsDarkMode;
        var greenBrush = new SolidColorBrush(dark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105));
        var amberBrush = new SolidColorBrush(dark ? Color.FromRgb(251, 191, 36) : Color.FromRgb(217, 119, 6));
        var blueBrush = new SolidColorBrush(dark ? Color.FromRgb(56, 189, 248) : Color.FromRgb(2, 132, 199));

        HardwareSourceText.Text = _activeDevice.IsPhysicalHardware
            ? Loc["ConnectionModePhysical"]
            : Loc["ConnectionModeDisconnected"];
        HardwareSourceText.Foreground = _activeDevice.IsPhysicalHardware ? greenBrush : amberBrush;

        UsbBIntervalValue.Text = _activeDevice.ActiveBIntervalFormatted;

        if (!_activeDevice.IsPhysicalHardware)
        {
            SerialLockBannerText.Text = Loc["StatusDisconnectedLock"];
            SerialLockBannerText.Foreground = amberBrush;
            SerialLockDot.Fill = amberBrush;
            SerialLockHashText.Text = SerialEngine.LastDriverActionLog;
            TopActiveModelText.Text = "No Controller Connected";
            TopPulseDot.Fill = amberBrush;
            MetricPollingLockSub.Text = Loc["WaitingForControllerBadge"];
        }
        else if (_activeDevice.IsRegisteredFor1000Hz)
        {
            SerialLockBannerText.Text = Loc["StatusRegistered1000Hz"];
            SerialLockBannerText.Foreground = greenBrush;
            SerialLockDot.Fill = greenBrush;
            SerialLockHashText.Text = $"{_activeDevice.DriverBindingStatus} · SHA256:{SerialLockPollingEngine.ComputeSerialLockSignature(_activeDevice.SerialNumber)}";
            TopActiveModelText.Text = $"{_activeDevice.Kind} ({_activeDevice.SerialNumber})";
            TopPulseDot.Fill = greenBrush;
            MetricPollingLockSub.Text = "Locked 1000Hz · LowerFilters=hidusbf";
        }
        else
        {
            SerialLockBannerText.Text = Loc["StatusUnregistered250Hz"];
            SerialLockBannerText.Foreground = blueBrush;
            SerialLockDot.Fill = blueBrush;
            SerialLockHashText.Text = $"{_activeDevice.DriverBindingStatus}";
            TopActiveModelText.Text = $"{_activeDevice.Kind} ({_activeDevice.SerialNumber})";
            TopPulseDot.Fill = blueBrush;
            MetricPollingLockSub.Text = "Stock 250Hz · Unregistered Serial";
        }

        PauseResumeOscBtn.Content = _oscilloscope.IsPaused
            ? Loc["ResumeTelemetryBtn"]
            : Loc["PauseTelemetryBtn"];

        UsbSuspendStatusText.Text = _usbOptimizer.IsUsbSelectiveSuspendDisabled
            ? Loc["UsbSuspendDisabledBadge"]
            : Loc["UsbSuspendEnabledBadge"];

        UsbSuspendStatusText.Foreground = _usbOptimizer.IsUsbSelectiveSuspendDisabled ? greenBrush : amberBrush;
        TimerResolutionValueText.Text = _usbOptimizer.ActiveTimerResolution;

        RefreshCalibrationUI();
    }

    private void RefreshSecurityBadges()
    {
        bool dark = Theme.IsDarkMode;
        var greenBrush = new SolidColorBrush(dark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105));
        var amberBrush = new SolidColorBrush(dark ? Color.FromRgb(251, 191, 36) : Color.FromRgb(217, 119, 6));
        var blueBrush = new SolidColorBrush(dark ? Color.FromRgb(56, 189, 248) : Color.FromRgb(2, 132, 199));

        var status = _securityShield.InspectSystemIntegrity();

        SidebarSecureBootText.Text = status.SecureBootEnabled ? "ON · VERIFIED" : "OFF · UNVERIFIED";
        SidebarSecureBootText.Foreground = status.SecureBootEnabled ? greenBrush : amberBrush;
        CardSecureBootText.Text = status.SecureBootEnabled ? "SECURE BOOT = ON" : "SECURE BOOT = OFF";
        CardSecureBootText.Foreground = status.SecureBootEnabled ? greenBrush : amberBrush;

        SidebarHvciText.Text = status.HvciEnabled ? "ON · ACTIVE" : "OFF · INACTIVE";
        SidebarHvciText.Foreground = status.HvciEnabled ? greenBrush : amberBrush;
        CardHvciText.Text = status.HvciEnabled ? "HVCI = ON" : "HVCI = OFF";
        CardHvciText.Foreground = status.HvciEnabled ? greenBrush : amberBrush;

        SidebarTestSignText.Text = status.TestSigningDisabled ? "OFF · WHQL" : "ON · TESTMODE";
        SidebarTestSignText.Foreground = status.TestSigningDisabled ? blueBrush : amberBrush;
        CardTestSignText.Text = status.TestSigningDisabled ? "TEST SIGNING = OFF" : "TEST SIGNING = ON";
        CardTestSignText.Foreground = status.TestSigningDisabled ? blueBrush : amberBrush;
    }

    private void RunAuthenticityAnalysis()
    {
        bool dark = Theme.IsDarkMode;
        var report = _authenticityAnalyzer.VerifyController(
            _activeDevice,
            _oscilloscope.CurrentDeltaMs,
            _oscilloscope.CurrentJitterUs,
            _oscilloscope.IsReceivingPackets);

        McuSignatureValue.Text = report.McuArchitecture;
        DescriptorProofValue.Text = report.DescriptorProof;
        ConfidenceScoreValue.Text = $"{report.ConfidencePercent:F1}%";

        switch (report.Verdict)
        {
            case AuthenticityVerdict.NoDeviceConnected:
                AuthenticityVerdictText.Text = Loc["VerdictNoDevice"];
                AuthenticityVerdictText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(148, 179, 208) : Color.FromRgb(59, 85, 114));
                break;

            case AuthenticityVerdict.GenuineSony1000HzCapable:
                AuthenticityVerdictText.Text = Loc["VerdictGenuine1000Hz"];
                AuthenticityVerdictText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105));
                break;

            case AuthenticityVerdict.GenuineSonyStock250Hz:
                AuthenticityVerdictText.Text = Loc["VerdictGenuineStock"];
                AuthenticityVerdictText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(56, 189, 248) : Color.FromRgb(2, 132, 199));
                break;

            case AuthenticityVerdict.CloneHardwareLocked:
                AuthenticityVerdictText.Text = Loc["VerdictCloneLocked"];
                AuthenticityVerdictText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(248, 113, 113) : Color.FromRgb(220, 38, 38));
                break;
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        _oscilloscope.CheckPacketTimeout();
        bool receiving = _oscilloscope.IsReceivingPackets;

        SidebarPacketCounterText.Text = $"WM_INPUT Packets: {_oscilloscope.TotalPacketsCaptured:N0}";

        if (!receiving)
        {
            TopLiveRateText.Text = Loc["WaitingForControllerBadge"];
            MetricPollingHzText.Text = "0 Hz";
            MetricDeltaMsText.Text = "0.000 ms";
            MetricJitterUsText.Text = "±0.0 µs";
            MetricP99MsText.Text = "0.000 ms";
            LiveCircularityErrorText.Text = "0.00%";
        }
        else
        {
            double hz = _oscilloscope.CurrentHz;
            double deltaMs = _oscilloscope.CurrentDeltaMs;
            double jitterUs = _oscilloscope.CurrentJitterUs;
            double p99Ms = _oscilloscope.P99DeltaMs;

            TopLiveRateText.Text = $"{hz:F1} Hz · {deltaMs:F3} ms";
            MetricPollingHzText.Text = $"{hz:F1} Hz";
            MetricDeltaMsText.Text = $"{deltaMs:F3} ms";
            MetricJitterUsText.Text = $"±{jitterUs:F1} µs";
            MetricP99MsText.Text = $"{p99Ms:F3} ms";
            LiveCircularityErrorText.Text = $"{_oscilloscope.LiveCircularityErrorPercent:F2}%";
        }

        if (_activeTabIndex == 1)
        {
            OscilloscopeWaveformView.RefreshFrame();
            CircularityHeatmapView.RefreshFrame();
        }
    }

    private void OnSidebarNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tagStr } || !int.TryParse(tagStr, out int index))
            return;

        if (index == _activeTabIndex)
            return;

        _activeTabIndex = index;

        double targetY = index * 52.0;
        var pillAnim = new DoubleAnimationUsingKeyFrames();
        pillAnim.KeyFrames.Add(new SplineDoubleKeyFrame(
            targetY,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
            new KeySpline(0.22, 1.0, 0.36, 1.0)));
        PillTranslateTransform.BeginAnimation(TranslateTransform.YProperty, pillAnim);

        Page0Dashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page1Oscilloscope.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2Calibration.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3SystemShield.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        PageViewportHost.Opacity = 0.0;
        PageSlideTransform.Y = 14.0;

        var fadeAnim = new DoubleAnimationUsingKeyFrames();
        fadeAnim.KeyFrames.Add(new SplineDoubleKeyFrame(
            1.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(310)),
            new KeySpline(0.22, 1.0, 0.36, 1.0)));
        PageViewportHost.BeginAnimation(OpacityProperty, fadeAnim);

        var slideAnim = new DoubleAnimationUsingKeyFrames();
        slideAnim.KeyFrames.Add(new SplineDoubleKeyFrame(
            0.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)),
            new KeySpline(0.22, 1.0, 0.36, 1.0)));
        PageSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        Theme.ToggleTheme(this);
    }

    private void OnToggleLanguageClick(object sender, RoutedEventArgs e)
    {
        Loc.ToggleLanguage();
    }

    private void OnRescanControllersClick(object sender, RoutedEventArgs e)
    {
        var devices = _detector.ScanSonyControllers();
        ApplyConnectedControllersList(devices);
    }

    private async void OnProvisionWhqlDriverClick(object sender, RoutedEventArgs e)
    {
        string targetDriverFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "driver");
        var (infPath, sysPath, sourceSummary) = await SerialEngine.ProvisionWhqlDriverFilesAsync(targetDriverFolder);
        var (ok, outputLog) = SerialLockPollingEngine.InstallWhqlDriverToSystem(infPath, sysPath);

        SerialLockHashText.Text = $"{sourceSummary} · pnputil: {(ok ? "Installed" : outputLog)}";
        if (_activeDevice.IsPhysicalHardware)
        {
            SerialEngine.ApplySerialPolicy(_activeDevice);
            RefreshLocalizedDynamicUI();
        }
    }

    private void OnAuthorizeCurrentSerialClick(object sender, RoutedEventArgs e)
    {
        string targetSerial = !string.IsNullOrWhiteSpace(_activeDevice.SerialNumber)
            ? _activeDevice.SerialNumber
            : CustomSerialInputBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(targetSerial))
        {
            SerialEngine.AuthorizeSerial(targetSerial, _activeDevice);
            RefreshLocalizedDynamicUI();
            RunAuthenticityAnalysis();
        }
    }

    private void OnRevokeCurrentSerialClick(object sender, RoutedEventArgs e)
    {
        string targetSerial = !string.IsNullOrWhiteSpace(_activeDevice.SerialNumber)
            ? _activeDevice.SerialNumber
            : CustomSerialInputBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(targetSerial))
        {
            SerialEngine.RevokeSerial(targetSerial, _activeDevice);
            RefreshLocalizedDynamicUI();
            RunAuthenticityAnalysis();
        }
    }

    private void OnAddCustomSerialClick(object sender, RoutedEventArgs e)
    {
        string input = CustomSerialInputBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(input))
        {
            SerialEngine.AuthorizeSerial(input, _activeDevice);
            RefreshLocalizedDynamicUI();
            RunAuthenticityAnalysis();
        }
    }

    private void OnRunAuthenticityVerifyClick(object sender, RoutedEventArgs e)
    {
        RunAuthenticityAnalysis();
    }

    private void OnPauseResumeOscilloscopeClick(object sender, RoutedEventArgs e)
    {
        _oscilloscope.IsPaused = !_oscilloscope.IsPaused;
        RefreshLocalizedDynamicUI();
    }

    private void OnResetOscilloscopeClick(object sender, RoutedEventArgs e)
    {
        _oscilloscope.ResetBuffers();
    }

    private void OnSampleStickCenterClick(object sender, RoutedEventArgs e)
    {
        _nvsCalibrator.SampleStickCenter(_activeDevice, _calibrationProfile);
        RefreshCalibrationUI();
    }

    private void OnCalibrateRangeClick(object sender, RoutedEventArgs e)
    {
        _nvsCalibrator.CalibrateOuterCircularity(_activeDevice, _calibrationProfile);
        RefreshCalibrationUI();
    }

    private void OnCommitNvsFlashClick(object sender, RoutedEventArgs e)
    {
        _nvsCalibrator.CommitPermanentNvsFlash(_activeDevice, _calibrationProfile);
        RefreshCalibrationUI();
    }

    private void OnQueryNvsStateClick(object sender, RoutedEventArgs e)
    {
        _nvsCalibrator.QueryControllerCalibrationState(_activeDevice, _calibrationProfile);
        RefreshCalibrationUI();
    }

    private void RefreshCalibrationUI()
    {
        bool dark = Theme.IsDarkMode;
        LeftDriftValueText.Text = $"{_calibrationProfile.LeftDriftPercent:F2}%";
        RightDriftValueText.Text = $"{_calibrationProfile.RightDriftPercent:F2}%";
        LeftCircErrorValueText.Text = $"{_calibrationProfile.LeftCircularityErrorPercent:F2}%";
        RightCircErrorValueText.Text = $"{_calibrationProfile.RightCircularityErrorPercent:F2}%";
        NvsFeatureReportHexText.Text = $"HID Report: {_calibrationProfile.LastFeatureReportHex}";
        NvsCalibrationStatusText.Text = _calibrationProfile.StatusMessage;

        if (_calibrationProfile.LastWin32ErrorCode != 0)
        {
            NvsCalibrationStatusText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(248, 113, 113) : Color.FromRgb(220, 38, 38));
        }
        else if (_calibrationProfile.IsCommittedToNvsFlash)
        {
            NvsCalibrationStatusText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105));
        }
        else
        {
            NvsCalibrationStatusText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(56, 189, 248) : Color.FromRgb(2, 132, 199));
        }
    }

    private void OnApplyUsbOptimizationClick(object sender, RoutedEventArgs e)
    {
        _usbOptimizer.ApplyLowLatencyUsbPowerProfile(_activeDevice.DevicePath);
        RefreshLocalizedDynamicUI();
    }

    private void OnRestoreDefaultUsbClick(object sender, RoutedEventArgs e)
    {
        _usbOptimizer.RestoreDefaultUsbPowerPolicy();
        RefreshLocalizedDynamicUI();
    }

    private void OnRefreshSecurityShieldClick(object sender, RoutedEventArgs e)
    {
        RefreshSecurityBadges();
        var status = _securityShield.InspectSystemIntegrity();
        GitHubUpdateStatusText.Text = $"Verified via {status.KernelVerificationSource}";
    }

    private async void OnCheckGitHubUpdateClick(object sender, RoutedEventArgs e)
    {
        GitHubUpdateStatusText.Text = "Querying GitHub Releases API...";
        var info = await _releaseUpdater.CheckLatestReleaseAsync();
        GitHubUpdateStatusText.Text = info.IsUpdateAvailable
            ? $"New Release Available: {info.TagName} ({info.AssetName})"
            : $"Up to Date · {info.ReleaseTitle} [SHA256: {info.Sha256Digest[..10]}]";
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
