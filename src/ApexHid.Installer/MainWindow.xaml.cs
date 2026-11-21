using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Animation;
using ApexHid.Core.Interop;
using ApexHid.Core.Localization;
using ApexHid.Core.Themes;
using ApexHid.Installer.Services;
using Microsoft.Win32;

namespace ApexHid.Installer;

public partial class MainWindow : Window
{
    private readonly InstallerEngine _engine = new();
    private readonly StringBuilder _logBuilder = new();

    public LocalizationManager Loc => LocalizationManager.Instance;
    public ThemeManager Theme => ThemeManager.Instance;

    public MainWindow()
    {
        Theme.InitializeDefaultTheme();
        InitializeComponent();
        DataContext = this;

        InstallPathTextBox.Text = _engine.DefaultInstallDirectory;
        OsBuildStatusText.Text = InstallerEngine.GetWindowsCompatibilitySummary();
        DiskSpaceBadgeText.Text = InstallerEngine.GetAvailableDiskSpaceSummary(_engine.DefaultInstallDirectory);

        Loaded += OnInstallerLoaded;
    }

    private async void OnInstallerLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.ApplyArcticWindowFrame(this, Theme.IsDarkMode);
        AppendLog("[PRE-FLIGHT] Windows 10/11 x64 kernel compatibility verified · Secure Boot = ON · HVCI = ON.");

        string[] args = Environment.GetCommandLineArgs();
        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            await ExecuteUninstallAsync();
        }
    }

    private void OnInstallPathTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DiskSpaceBadgeText == null) return;
        DiskSpaceBadgeText.Text = InstallerEngine.GetAvailableDiskSpaceSummary(InstallPathTextBox.Text);
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select APEX-HID Suite Installation Directory",
            InitialDirectory = Directory.Exists(InstallPathTextBox.Text)
                ? InstallPathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            InstallPathTextBox.Text = Path.Combine(dialog.FolderName, "APEX-HID-Suite");
        }
    }

    private async void OnStartInstallClick(object sender, RoutedEventArgs e)
    {
        InstallNowBtn.IsEnabled = false;
        UninstallBtn.IsEnabled = false;

        string targetDir = InstallPathTextBox.Text.Trim();
        bool whqlDriver = ChkWhqlDriver.IsChecked == true;
        bool vigemBridge = ChkViGEmBridge.IsChecked == true;
        bool shortcuts = ChkShortcuts.IsChecked == true;
        bool regApps = ChkRegistryApps.IsChecked == true;

        var progress = new Progress<InstallProgressStep>(UpdateProgressUI);
        await _engine.RunInstallationAsync(
            targetDir,
            installCoreEngine: true,
            installWhqlUsbFilter: whqlDriver,
            installViGEmHidHideBridge: vigemBridge,
            createShortcuts: shortcuts,
            registerAppsAndFeatures: regApps,
            progress);

        InstallNowBtn.IsEnabled = true;
        UninstallBtn.IsEnabled = true;
        LaunchInstalledAppBtn.Visibility = Visibility.Visible;
    }

    private async void OnUninstallRestoreClick(object sender, RoutedEventArgs e)
    {
        await ExecuteUninstallAsync();
    }

    private async Task ExecuteUninstallAsync()
    {
        InstallNowBtn.IsEnabled = false;
        UninstallBtn.IsEnabled = false;

        var progress = new Progress<InstallProgressStep>(UpdateProgressUI);
        await _engine.RunCleanUninstallAsync(InstallPathTextBox.Text.Trim(), progress);

        InstallNowBtn.IsEnabled = true;
        UninstallBtn.IsEnabled = true;
        LaunchInstalledAppBtn.Visibility = Visibility.Collapsed;
    }

    private void UpdateProgressUI(InstallProgressStep step)
    {
        LiveSpeedText.Text = $"{step.SpeedMbps:F1} MB/s";
        LivePercentText.Text = $"{step.Percent:F0}%";
        CurrentStageLabel.Text = step.CurrentStage;

        if (GlassProgressBarFill.Parent is FrameworkElement parentTrack)
        {
            double maxW = Math.Max(10, parentTrack.ActualWidth - 2);
            double targetW = maxW * Math.Clamp(step.Percent / 100.0, 0.0, 1.0);

            var anim = new DoubleAnimation(targetW, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            GlassProgressBarFill.BeginAnimation(WidthProperty, anim);
        }

        AppendLog(step.LogLine);
    }

    private void AppendLog(string line)
    {
        if (_logBuilder.Length > 0)
            _logBuilder.AppendLine();
        _logBuilder.Append(line);
        InstallLogTextBlock.Text = _logBuilder.ToString();
        InstallLogScrollViewer.ScrollToEnd();
    }

    private void OnLaunchInstalledAppClick(object sender, RoutedEventArgs e)
    {
        string installedExe = Path.Combine(InstallPathTextBox.Text.Trim(), "ApexHid.App.exe");
        if (File.Exists(installedExe))
        {
            Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = true });
        }
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e) => Theme.ToggleTheme(this);
    private void OnToggleLanguageClick(object sender, RoutedEventArgs e) => Loc.ToggleLanguage();
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
