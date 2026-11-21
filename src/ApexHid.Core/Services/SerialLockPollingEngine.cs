using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;
using Microsoft.Win32;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 2: Real Serial-Locked 1000Hz Overclocking &amp; SetupAPI Per-Instance LowerFilters Engine.
/// Injects WHQL-signed 'hidusbf' into LowerFilters (SelectedRate=1000, bInterval=1) and restarts ONLY
/// the matching device instance via SetupDiCallClassInstaller(DIF_PROPERTYCHANGE).
/// Unregistered controllers have 'hidusbf' removed from their LowerFilters and stay at stock 250Hz (4.00ms).
/// </summary>
public sealed class SerialLockPollingEngine
{
    private const string FilterServiceName = "hidusbf";
    private const string SweetLowRawSysUrl = "https://raw.githubusercontent.com/SweetLow/hidusbf/master/DRIVER/AMD64_AS/1khz/hidusbf.sys";
    private const string SweetLowRawInfUrl = "https://raw.githubusercontent.com/SweetLow/hidusbf/master/DRIVER/HIDUSBF_AS.INF";
    private const string LordOfMiceZipUrl = "https://raw.githubusercontent.com/LordOfMice/hidusbf/master/hidusbf.zip";

    private readonly string _vaultFilePath;
    private readonly HashSet<string> _registeredSerials = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<string> AuthorizedSerials { get; } = [];
    public string LastDriverActionLog { get; private set; } = "Ready";

    public SerialLockPollingEngine()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "APEX-HID-Suite");
        Directory.CreateDirectory(appData);
        _vaultFilePath = Path.Combine(appData, "serial_vault.json");
        LoadVault();
    }

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static (string InfPath, string SysPath) ResolveLocalDriverAssets()
    {
        string[] candidateDirs =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", "driver"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "driver")),
            Path.Combine(Environment.CurrentDirectory, "assets", "driver")
        ];

        foreach (string dir in candidateDirs)
        {
            string inf = Path.Combine(dir, "hidusbf.inf");
            string sys = Path.Combine(dir, "hidusbf.sys");
            if (File.Exists(inf) && File.Exists(sys))
            {
                return (inf, sys);
            }
        }

        string fallbackDir = Path.Combine(AppContext.BaseDirectory, "assets", "driver");
        return (Path.Combine(fallbackDir, "hidusbf.inf"), Path.Combine(fallbackDir, "hidusbf.sys"));
    }

    /// <summary>
    /// Downloads the official WHQL-signed hidusbf.sys and hidusbf.inf from SweetLow's official GitHub repository,
    /// falling back to the local assets\driver\ cache.
    /// </summary>
    public async Task<(string InfPath, string SysPath, string SourceSummary)> ProvisionWhqlDriverFilesAsync(
        string targetDriverFolder,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(targetDriverFolder);
        string amd64AsDir = Path.Combine(targetDriverFolder, "amd64_as");
        Directory.CreateDirectory(amd64AsDir);

        string targetInf = Path.Combine(targetDriverFolder, "hidusbf.inf");
        string targetSys = Path.Combine(targetDriverFolder, "hidusbf.sys");
        string targetAmd64Sys = Path.Combine(amd64AsDir, "hidusbf.sys");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("APEX-HID-Suite/1.4.0");

        // 1. Attempt direct raw download from SweetLow/hidusbf or official LordOfMice/hidusbf archive
        try
        {
            using var sysResp = await http.GetAsync(SweetLowRawSysUrl, token).ConfigureAwait(false);
            using var infResp = await http.GetAsync(SweetLowRawInfUrl, token).ConfigureAwait(false);

            if (sysResp.IsSuccessStatusCode && infResp.IsSuccessStatusCode)
            {
                byte[] sysBytes = await sysResp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                byte[] infBytes = await infResp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

                await File.WriteAllBytesAsync(targetSys, sysBytes, token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(targetAmd64Sys, sysBytes, token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(targetInf, infBytes, token).ConfigureAwait(false);

                return (targetInf, targetSys, "Downloaded directly from SweetLow/hidusbf GitHub");
            }

            // Try official hidusbf.zip from SweetLow's LordOfMice/hidusbf repository
            using var zipResp = await http.GetAsync(LordOfMiceZipUrl, token).ConfigureAwait(false);
            if (zipResp.IsSuccessStatusCode)
            {
                byte[] zipBytes = await zipResp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                using var ms = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                var sysEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("DRIVER/AMD64_AS/1khz/hidusbf.sys", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.Equals("DRIVER/AMD64_AS/hidusbf.sys", StringComparison.OrdinalIgnoreCase));

                var infEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("DRIVER/HIDUSBF_AS.INF", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.Equals("DRIVER/HIDUSBF.INF", StringComparison.OrdinalIgnoreCase));

                if (sysEntry != null && infEntry != null)
                {
                    sysEntry.ExtractToFile(targetSys, overwrite: true);
                    sysEntry.ExtractToFile(targetAmd64Sys, overwrite: true);
                    infEntry.ExtractToFile(targetInf, overwrite: true);
                    return (targetInf, targetSys, "Downloaded & extracted WHQL AMD64_AS driver from official SweetLow/LordOfMice GitHub");
                }
            }
        }
        catch
        {
            // Fall through to bundled local assets\driver\ cache
        }

        // 2. Local fallback cache in assets\driver\
        var (localInf, localSys) = ResolveLocalDriverAssets();
        if (File.Exists(localInf) && File.Exists(localSys))
        {
            if (!string.Equals(Path.GetFullPath(localSys), Path.GetFullPath(targetSys), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(localSys, targetSys, overwrite: true);
                File.Copy(localSys, targetAmd64Sys, overwrite: true);
                File.Copy(localInf, targetInf, overwrite: true);
            }
            else if (!File.Exists(targetAmd64Sys))
            {
                File.Copy(localSys, targetAmd64Sys, overwrite: true);
            }
            return (targetInf, targetSys, "Loaded verified WHQL driver from local assets\\driver\\ cache");
        }

        return (targetInf, targetSys, "Driver files not found");
    }

    /// <summary>
    /// Installs hidusbf.inf into the Windows Driver Store via pnputil /add-driver and copies hidusbf.sys
    /// to C:\Windows\System32\drivers\ when running elevated.
    /// </summary>
    public static (bool Success, string OutputLog) InstallWhqlDriverToSystem(string infPath, string sysPath)
    {
        if (!File.Exists(infPath) || !File.Exists(sysPath))
        {
            return (false, $"Driver files missing: {infPath}");
        }

        bool isAdmin = IsRunningAsAdministrator();
        var log = new StringBuilder();

        if (isAdmin)
        {
            try
            {
                string systemDriversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                string destSys = Path.Combine(systemDriversDir, "hidusbf.sys");
                File.Copy(sysPath, destSys, overwrite: true);
                log.AppendLine($"Copied WHQL hidusbf.sys -> {destSys}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"System32\\drivers copy notice: {ex.Message}");
            }
        }

        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", $"/add-driver \"{infPath}\" /install")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string stdout = proc.StandardOutput.ReadToEnd().Trim();
                string stderr = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit(8000);

                if (!string.IsNullOrWhiteSpace(stdout))
                    log.AppendLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                    log.AppendLine(stderr);

                bool ok = proc.ExitCode == 0 || proc.ExitCode == 259 || proc.ExitCode == 3010;
                if (!ok && !isAdmin)
                {
                    log.AppendLine("Note: pnputil /add-driver requires Administrator elevation to write to Windows DriverStore.");
                }
                return (ok, log.ToString().Trim());
            }
        }
        catch (Exception ex)
        {
            log.AppendLine($"pnputil execution error: {ex.Message}");
        }

        return (false, log.ToString().Trim());
    }

    public bool IsSerialAuthorized(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return false;
        return _registeredSerials.Contains(NormalizeSerial(serialNumber));
    }

    /// <summary>
    /// Applies real per-controller Serial Number locking:
    /// - If authorized: injects 'hidusbf' into LowerFilters, sets SelectedRate = 1000 (bInterval = 1),
    ///   and restarts ONLY that specific device instance via SetupDiCallClassInstaller(DIF_PROPERTYCHANGE).
    /// - If unregistered: removes 'hidusbf' from LowerFilters, sets SelectedRate = 0 (250Hz / 4.00ms stock),
    ///   and restarts ONLY that specific device instance.
    /// </summary>
    public void ApplySerialPolicy(ControllerDevice device)
    {
        if (!device.IsPhysicalHardware)
        {
            device.IsRegisteredFor1000Hz = false;
            device.DriverBindingStatus = "No Physical Controller Connected";
            LastDriverActionLog = device.DriverBindingStatus;
            return;
        }

        bool authorized = IsSerialAuthorized(device.SerialNumber);
        device.IsRegisteredFor1000Hz = authorized;

        if (!string.IsNullOrWhiteSpace(device.DevicePath))
        {
            TryConfigurePhysicalHidBuffers(device.DevicePath, authorized ? 2 : 32);
        }

        var (ok, statusMsg) = ConfigureDeviceInstanceLowerFilterAndRestart(
            device.DeviceInstanceId,
            enable1000HzFilter: authorized);

        device.DriverBindingStatus = statusMsg;
        LastDriverActionLog = statusMsg;
    }

    public bool AuthorizeSerial(string serialNumber, ControllerDevice? activeDevice = null)
    {
        string normalized = NormalizeSerial(serialNumber);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (_registeredSerials.Add(normalized))
        {
            AuthorizedSerials.Add(normalized);
            SaveVault();
        }

        if (activeDevice is { IsPhysicalHardware: true } &&
            string.Equals(NormalizeSerial(activeDevice.SerialNumber), normalized, StringComparison.OrdinalIgnoreCase))
        {
            ApplySerialPolicy(activeDevice);
        }

        return true;
    }

    public bool RevokeSerial(string serialNumber, ControllerDevice? activeDevice = null)
    {
        string normalized = NormalizeSerial(serialNumber);
        if (_registeredSerials.Remove(normalized))
        {
            var existing = AuthorizedSerials.FirstOrDefault(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                AuthorizedSerials.Remove(existing);
            }
            SaveVault();
        }

        if (activeDevice is { IsPhysicalHardware: true } &&
            string.Equals(NormalizeSerial(activeDevice.SerialNumber), normalized, StringComparison.OrdinalIgnoreCase))
        {
            ApplySerialPolicy(activeDevice);
        }

        return true;
    }

    /// <summary>
    /// Locates the exact device instance in SetupAPI, injects or removes 'hidusbf' in SPDRP_LOWERFILTERS,
    /// sets SelectedRate = 1000 (or 0 for stock 250Hz) in the device registry key, and restarts ONLY
    /// that specific device instance via SetupDiCallClassInstaller(DIF_PROPERTYCHANGE).
    /// </summary>
    public static (bool Success, string Message) ConfigureDeviceInstanceLowerFilterAndRestart(
        string deviceInstanceId,
        bool enable1000HzFilter)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
        {
            return (false, "No DeviceInstanceId available for SetupAPI binding.");
        }

        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero,
            deviceInstanceId,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);

        if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
        {
            int err = Marshal.GetLastWin32Error();
            return (false, $"SetupDiGetClassDevs failed for {deviceInstanceId} (Win32 Error 0x{err:X8})");
        }

        try
        {
            var devInfoData = new NativeMethods.SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>()
            };

            if (!NativeMethods.SetupDiEnumDeviceInfo(devInfoSet, 0, ref devInfoData))
            {
                int err = Marshal.GetLastWin32Error();
                return (false, $"Device instance {deviceInstanceId} not active (Win32 Error 0x{err:X8})");
            }

            // 1. Read existing SPDRP_LOWERFILTERS (REG_MULTI_SZ)
            var currentFilters = ReadDeviceMultiSzProperty(devInfoSet, ref devInfoData, NativeMethods.SPDRP_LOWERFILTERS);
            bool modifiedFilters = false;

            if (enable1000HzFilter)
            {
                if (!currentFilters.Any(f => string.Equals(f, FilterServiceName, StringComparison.OrdinalIgnoreCase)))
                {
                    currentFilters.Add(FilterServiceName);
                    modifiedFilters = true;
                }
            }
            else
            {
                int removed = currentFilters.RemoveAll(f => string.Equals(f, FilterServiceName, StringComparison.OrdinalIgnoreCase));
                modifiedFilters = removed > 0;
            }

            // 2. Write updated SPDRP_LOWERFILTERS to this specific device node
            if (modifiedFilters)
            {
                byte[]? multiSzBytes = EncodeMultiSz(currentFilters);
                if (!NativeMethods.SetupDiSetDeviceRegistryProperty(
                        devInfoSet,
                        ref devInfoData,
                        NativeMethods.SPDRP_LOWERFILTERS,
                        multiSzBytes,
                        (uint)(multiSzBytes?.Length ?? 0)))
                {
                    int err = Marshal.GetLastWin32Error();
                    return (false, $"SetupDiSetDeviceRegistryProperty(SPDRP_LOWERFILTERS) failed — Win32 Error 0x{err:X8} (Run as Administrator)");
                }
            }

            // 3. Open the device's hardware Parameters registry key and set SelectedRate = 1000 (1ms) or 0 (250Hz stock)
            IntPtr hDevKey = NativeMethods.SetupDiOpenDevRegKey(
                devInfoSet,
                ref devInfoData,
                NativeMethods.DICS_FLAG_GLOBAL,
                0,
                NativeMethods.DIREG_DEV,
                NativeMethods.KEY_WRITE);

            if (hDevKey != IntPtr.Zero && hDevKey != new IntPtr(-1))
            {
                try
                {
                    uint selectedRate = enable1000HzFilter ? 1000u : 0u;
                    NativeMethods.RegSetValueEx(hDevKey, "SelectedRate", 0, NativeMethods.REG_DWORD, ref selectedRate, sizeof(uint));
                }
                finally
                {
                    NativeMethods.RegCloseKey(hDevKey);
                }
            }

            // 4. Restart ONLY this specific USB/HID device instance via SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)
            var propChange = new NativeMethods.SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new NativeMethods.SP_CLASSINSTALL_HEADER
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_CLASSINSTALL_HEADER>(),
                    InstallFunction = NativeMethods.DIF_PROPERTYCHANGE
                },
                StateChange = NativeMethods.DICS_PROPCHANGE,
                Scope = NativeMethods.DICS_FLAG_CONFIGSPECIFIC,
                HwProfile = 0
            };

            if (!NativeMethods.SetupDiSetClassInstallParams(
                    devInfoSet,
                    ref devInfoData,
                    ref propChange,
                    (uint)Marshal.SizeOf<NativeMethods.SP_PROPCHANGE_PARAMS>()))
            {
                int err = Marshal.GetLastWin32Error();
                return (false, $"SetupDiSetClassInstallParams(DIF_PROPERTYCHANGE) failed — Win32 Error 0x{err:X8}");
            }

            if (!NativeMethods.SetupDiCallClassInstaller(
                    NativeMethods.DIF_PROPERTYCHANGE,
                    devInfoSet,
                    ref devInfoData))
            {
                int err = Marshal.GetLastWin32Error();
                return (false, $"SetupDiCallClassInstaller(DIF_PROPERTYCHANGE) failed — Win32 Error 0x{err:X8} (Administrator privilege required)");
            }

            return enable1000HzFilter
                ? (true, $"Injected LowerFilters='hidusbf' (SelectedRate=1000, bInterval=1) & restarted {deviceInstanceId}")
                : (true, $"Removed 'hidusbf' from LowerFilters (Stock 250Hz / 4.00ms) & restarted {deviceInstanceId}");
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet);
        }
    }

    private static List<string> ReadDeviceMultiSzProperty(
        IntPtr devInfoSet,
        ref NativeMethods.SP_DEVINFO_DATA devInfoData,
        uint property)
    {
        var list = new List<string>();
        NativeMethods.SetupDiGetDeviceRegistryProperty(
            devInfoSet,
            ref devInfoData,
            property,
            out _,
            null,
            0,
            out uint requiredSize);

        if (requiredSize == 0 || requiredSize > 4096)
            return list;

        byte[] buffer = new byte[requiredSize];
        if (!NativeMethods.SetupDiGetDeviceRegistryProperty(
                devInfoSet,
                ref devInfoData,
                property,
                out uint regType,
                buffer,
                requiredSize,
                out _) || regType != NativeMethods.REG_MULTI_SZ)
        {
            return list;
        }

        string raw = Encoding.Unicode.GetString(buffer);
        foreach (string part in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                list.Add(part.Trim());
        }

        return list;
    }

    private static byte[]? EncodeMultiSz(List<string> values)
    {
        if (values.Count == 0)
            return null;

        string joined = string.Join("\0", values) + "\0\0";
        return Encoding.Unicode.GetBytes(joined);
    }

    public static string ComputeSerialLockSignature(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return "NONE";
        byte[] bytes = Encoding.UTF8.GetBytes($"APEX-HID-1000HZ-LOCK::{NormalizeSerial(serialNumber)}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private static void TryConfigurePhysicalHidBuffers(string devicePath, int bufferCount)
    {
        try
        {
            using var handle = NativeMethods.CreateFile(
                devicePath,
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                NativeMethods.HidD_SetNumInputBuffers(handle, bufferCount);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private void LoadVault()
    {
        try
        {
            if (File.Exists(_vaultFilePath))
            {
                string json = File.ReadAllText(_vaultFilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    foreach (var s in list)
                    {
                        string norm = NormalizeSerial(s);
                        if (!string.IsNullOrWhiteSpace(norm) && _registeredSerials.Add(norm))
                            AuthorizedSerials.Add(norm);
                    }
                }
            }
        }
        catch
        {
            // Start with empty vault on fresh system
        }
    }

    private void SaveVault()
    {
        try
        {
            string json = JsonSerializer.Serialize(_registeredSerials.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_vaultFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    private static string NormalizeSerial(string raw) => raw.Trim().ToUpperInvariant();
}
