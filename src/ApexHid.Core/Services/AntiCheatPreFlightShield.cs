using System.Runtime.InteropServices;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;
using Microsoft.Win32;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 7: Real Hardware Anti-Cheat Pre-Flight Shield.
/// Queries live Windows Kernel &amp; UEFI telemetry without hardcoded overrides.
/// </summary>
public sealed class AntiCheatPreFlightShield
{
    public SecurityShieldStatus InspectSystemIntegrity()
    {
        bool secureBootOn = false;
        bool hvciOn = false;
        bool testSigningOff = true;

        try
        {
            using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (sbKey?.GetValue("UEFISecureBootEnabled") is int sbVal)
            {
                secureBootOn = sbVal == 1;
            }
        }
        catch
        {
            // Leave false if unreadable
        }

        try
        {
            using var hvciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (hvciKey?.GetValue("Enabled") is int hvciVal)
            {
                hvciOn = hvciVal == 1;
            }
        }
        catch
        {
            // Leave false if unreadable
        }

        try
        {
            var sci = new NativeMethods.SYSTEM_CODEINTEGRITY_INFORMATION
            {
                Length = (uint)Marshal.SizeOf<NativeMethods.SYSTEM_CODEINTEGRITY_INFORMATION>()
            };

            int status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemCodeIntegrityInformation,
                ref sci,
                sci.Length,
                out _);

            if (status == 0)
            {
                bool testSignFlag = (sci.CodeIntegrityOptions & NativeMethods.CODEINTEGRITY_OPTION_TESTSIGN) != 0;
                testSigningOff = !testSignFlag;

                if ((sci.CodeIntegrityOptions & NativeMethods.CODEINTEGRITY_OPTION_HVCI_KMCI_ENABLED) != 0)
                {
                    hvciOn = true;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return new SecurityShieldStatus
        {
            SecureBootEnabled = secureBootOn,
            HvciEnabled = hvciOn,
            TestSigningDisabled = testSigningOff,
            KernelVerificationSource = "NtQuerySystemInformation(0x67) · HKLM\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State"
        };
    }
}
