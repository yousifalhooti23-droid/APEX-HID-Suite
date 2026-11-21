using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ApexHid.Core.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private bool _isArabic;
    private readonly Dictionary<string, (string En, string Ar)> _strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Global Header & Navigation
        ["AppTitle"] = ("APEX-HID Suite", "منظومة APEX-HID الاحترافية"),
        ["AppSubtitle"] = ("7-Layer Controller Precision & Microsecond Telemetry Engine", "محرك الدقة السباعي لوحدات التحكم والقياس بالميكروثانية"),
        ["LangToggleText"] = ("العربية | EN", "EN | العربية"),
        ["LangActiveBadge"] = ("ENGLISH · LTR", "العربية · RTL"),
        ["NavDashboard"] = ("Controller & Serial Lock", "وحدة التحكم وقفل السيريال"),
        ["NavOscilloscope"] = ("Microsecond Oscilloscope", "راسم الذبذبات بالميكروثانية"),
        ["NavCalibration"] = ("NVS Flash Calibration", "معايرة ذاكرة الفلاش NVS"),
        ["NavSystemShield"] = ("USB, MMCSS & Anti-Cheat", "الطاقة وMMCSS ومكافحة الغش"),

        // Controller & Serial Lock (Layer 1, 2, 3)
        ["DetectedHardwareTitle"] = ("Physical Controller & USB Endpoint", "وحدة التحكم الفعلية ومنفذ USB"),
        ["ScanControllersBtn"] = ("Rescan Physical USB Bus", "إعادة فحص منافذ USB الفعلية"),
        ["ProvisionWhqlBtn"] = ("Install WHQL hidusbf Driver (pnputil)", "تثبيت تعريف hidusbf المعتمد WHQL"),
        ["ConnectionModePhysical"] = ("LIVE PHYSICAL USB HID", "عتاد USB HID متصل فعلياً"),
        ["ConnectionModeDisconnected"] = ("NO CONTROLLER · WAITING FOR USB HID", "غير متصل · بانتظار توصيل جهاز USB HID"),
        ["ModelLabel"] = ("Controller Model", "طراز وحدة التحكم"),
        ["BoardRevisionLabel"] = ("Board / BDM Revision", "مراجعة اللوحة الأم (BDM)"),
        ["VidPidLabel"] = ("USB Vendor / Product ID", "معرف المصنّع والمنتج (VID:PID)"),
        ["SerialNumberLabel"] = ("Hardware Serial Number", "الرقم التسلسلي للعتاد (Serial)"),
        ["FirmwareLabel"] = ("Device Instance / Firmware", "معرّف الجهاز والبرمجية الثابتة"),
        ["USBIntervalLabel"] = ("SetupAPI LowerFilters & Rate", "حالة مرشح LowerFilters والتردد"),
        ["WaitingForControllerBadge"] = ("0 Hz / Waiting for Controller", "0 Hz / بانتظار وحدة التحكم"),

        // Serial Lock Card (Layer 2)
        ["SerialLockTitle"] = ("Serial-Locked 1000Hz Overclocking Vault", "خزنة قفل التردد 1000Hz بالرقم التسلسلي"),
        ["SerialLockSubtitle"] = ("Injects WHQL 'hidusbf' into LowerFilters (SelectedRate=1000, bInterval=1) ONLY on registered serial numbers via SetupDiCallClassInstaller(DIF_PROPERTYCHANGE). Unregistered controllers remain at stock 250Hz (4.00ms).", "يحقن مرشح hidusbf المعتمد WHQL في LowerFilters (SelectedRate=1000) حصرياً للأرقام التسلسلية المسجلة، ويزيله من الوحدات غير المسجلة لتبقى على تردد 250Hz الافتراضي (4.00ms)."),
        ["StatusRegistered1000Hz"] = ("REGISTERED · LOWERFILTERS=HIDUSBF (1000 Hz / 1.00 ms)", "مسجّل · مرشح hidusbf مفعّل (1000 Hz / 1.00 ms)"),
        ["StatusUnregistered250Hz"] = ("UNREGISTERED · STOCK 250 Hz (4.00 ms)", "غير مسجّل · التردد الافتراضي 250 Hz (4.00 ms)"),
        ["StatusDisconnectedLock"] = ("WAITING FOR PHYSICAL CONTROLLER CONNECTION", "بانتظار توصيل وحدة تحكم فعلية عبر منفذ USB"),
        ["RegisterSerialBtn"] = ("Authorize Connected Serial for 1000Hz", "اعتماد الرقم التسلسلي المتصل لـ 1000Hz"),
        ["RevokeSerialBtn"] = ("Revoke Lock (Revert Instance to 250Hz)", "إلغاء الاعتماد (إعادة المنفذ إلى 250Hz)"),
        ["AddSerialBtn"] = ("Add Serial to Vault", "إضافة للخزنة"),
        ["RegisteredVaultHeader"] = ("Authorized Serial Whitelist Vault", "قائمة الأرقام التسلسلية المعتمدة في الخزنة"),

        // Chipset Authenticity Detector (Layer 3)
        ["AuthenticityTitle"] = ("Layer 3 · Chipset Authenticity & 1ms Acceptance Analyzer", "الطبقة 3 · فاحص أصالة الشريحة وقبول تردد 1ms"),
        ["AuthenticitySubtitle"] = ("Probes physical HID Feature Reports (0x20 / 0x12) and real QPC packet deltas to verify authentic Sony silicon.", "يفحص تقارير HID Feature Reports الفعلية (0x20 / 0x12) والفواصل الزمنية الحقيقية للتحقق من أصالة شريحة سوني."),
        ["RunAuthenticityBtn"] = ("Probe Physical Chipset & Feature Report", "فحص الشريحة الفعلية وتقرير Feature Report"),
        ["McuSignatureLabel"] = ("Detected MCU Architecture", "معمارية المعالج الدقيق المكتشفة"),
        ["DescriptorProofLabel"] = ("USB Descriptor & Timing Proof", "برهان واصف USB واستقرار التوقيت"),
        ["ConfidenceScoreLabel"] = ("Authenticity Confidence", "درجة موثوقية الأصالة"),
        ["VerdictNoDevice"] = ("WAITING FOR PHYSICAL CONTROLLER CONNECTION", "بانتظار توصيل وحدة تحكم سوني فعلية للفحص"),
        ["VerdictGenuine1000Hz"] = ("AUTHENTIC SONY MCU · TRUE 1.00ms POLLING VERIFIED", "شريحة سوني أصلية · تم التحقق من استجابة 1.00ms الحقيقية"),
        ["VerdictGenuineStock"] = ("AUTHENTIC SONY MCU · RUNNING STOCK 250Hz (4.00ms)", "شريحة سوني أصلية · تعمل بالتردد القياسي 250Hz (4.00ms)"),
        ["VerdictCloneLocked"] = ("CLONE / HARDWARE-LOCKED MCU · REJECTED FEATURE REPORT OR 1ms", "شريحة مقلدة مقفولة عتادياً · رفضت تقرير الأصالة أو تردد 1ms"),

        // Microsecond Oscilloscope (Layer 6)
        ["OscilloscopeTitle"] = ("Layer 6 · 60fps RawInput (WM_INPUT) + QPC Microsecond Oscilloscope", "الطبقة 6 · راسم الذبذبات الفوري بالميكروثانية عبر WM_INPUT و QPC"),
        ["LivePollingHzLabel"] = ("LIVE WM_INPUT RATE", "معدل استطلاع WM_INPUT الفعلي"),
        ["PacketDeltaMsLabel"] = ("QPC PACKET DELTA", "الفاصل الزمني الفعلي للحزمة"),
        ["JitterVarianceUsLabel"] = ("JITTER VARIANCE", "تباين الارتجاف (Jitter)"),
        ["P99LatencyMsLabel"] = ("99TH PERCENTILE", "زمن الاستجابة المئوي 99%"),
        ["WaveformGraphHeader"] = ("Real-Time WM_INPUT Inter-Packet Interval Waveform (QueryPerformanceCounter)", "الموجة الزمنية الفورية للفواصل بين حزم WM_INPUT الحقيقية"),
        ["CircularityGraphHeader"] = ("Live 360° Stick Circularity & Polar Heatmap", "الخريطة الحرارية القطبية لدائرية العصا 360°"),
        ["CircularityAvgErrorLabel"] = ("Measured Circularity Error", "نسبة خطأ الدائرية المقاسة"),
        ["PauseTelemetryBtn"] = ("Pause Capture", "إيقاف الالتقاط مؤقتاً"),
        ["ResumeTelemetryBtn"] = ("Resume Capture", "استئناف الالتقاط"),
        ["ResetStatsBtn"] = ("Reset Counters", "تصفير العدادات"),

        // NVS Flash Calibration (Layer 5)
        ["CalibrationTitle"] = ("Layer 5 · Onboard NVS Flash Stick Calibration Engine", "الطبقة 5 · محرك معايرة العصا في ذاكرة الفلاش الدائمة NVS"),
        ["CalibrationSubtitle"] = ("Sends direct Sony DualShock 4 / DualSense HID Feature Reports (0x82 / 0x83) via HidD_SetFeature and reports actual Win32 error codes if disconnected or rejected.", "يرسل تقارير HID Feature Reports الحقيقية (0x82 / 0x83) عبر HidD_SetFeature ويعرض رمز خطأ Win32 الفعلي في حال عدم الاتصال أو الرفض."),
        ["LeftStickHeader"] = ("Left Analog Axis (LX / LY)", "المحور التماثلي الأيسر (LX / LY)"),
        ["RightStickHeader"] = ("Right Analog Axis (RX / RY)", "المحور التماثلي الأيمن (RX / RY)"),
        ["SampleCenterBtn"] = ("1. Sample Zero Center (0x82 0x01/0x02)", "1. معايرة المركز الصفري (0x82)"),
        ["CalibrateRangeBtn"] = ("2. Start Range Calibration (0x82 0x02)", "2. بدء معايرة المحيط الدائري (0x82)"),
        ["CommitNvsFlashBtn"] = ("3. Burn Calibration to NVS Flash (0x82 0x03)", "3. حفظ المعايرة دائماً في ذاكرة NVS (0x82 0x03)"),
        ["ResetFactoryNvsBtn"] = ("Query NVS State (HidD_GetFeature 0x83)", "استعلام حالة ذاكرة NVS (تقرير 0x83)"),
        ["NvsWriteStatusIdle"] = ("Waiting for physical Sony controller to send HidD_SetFeature(0x82/0x83)...", "بانتظار توصيل وحدة تحكم سوني فعلية لإرسال تقارير المعايرة..."),

        // USB Power, MMCSS & Anti-Cheat Shield (Layer 4 & Layer 7)
        ["UsbMmcssTitle"] = ("Layer 4 · USB Selective Suspend & MMCSS Thread Elevation", "الطبقة 4 · تحسين طاقة USB ورفع أولوية خيط المعالجة MMCSS"),
        ["UsbMmcssSubtitle"] = ("Eliminates USB xHCI idle power wake penalties and locks the HID reader thread to Windows Games / Pro Audio MMCSS Critical priority.", "يلغي تأخير استيقاظ منافذ USB xHCI ويرفع خيط قراءة HID إلى الأولوية الحرجة عبر خدمة Windows MMCSS."),
        ["UsbSelectiveSuspendLabel"] = ("Root Hub USB Selective Suspend", "التعليق الانتقائي لطاقة منفذ USB"),
        ["UsbSuspendDisabledBadge"] = ("DISABLED · 0ms WAKE PENALTY", "معطّل · صفر تأخير استيقاظ"),
        ["UsbSuspendEnabledBadge"] = ("ACTIVE · POWER SAVING", "مفعّل · وضع توفير الطاقة"),
        ["MmcssThreadLabel"] = ("AvSetMmThreadCharacteristics Priority", "أولوية خيط AvSetMmThreadCharacteristics"),
        ["MmcssActiveBadge"] = ("AVRT_PRIORITY_CRITICAL (Task: Games)", "أولوية حرجة AVRT_PRIORITY_CRITICAL"),
        ["TimerResolutionLabel"] = ("Kernel Timer Resolution (timeBeginPeriod)", "دقة مؤقت النواة (timeBeginPeriod)"),
        ["ApplyUsbOptimizationBtn"] = ("Apply Low-Latency USB & MMCSS Profile", "تطبيق ملف تحسين USB وMMCSS الفوري"),
        ["RestoreDefaultUsbBtn"] = ("Restore Windows Default Power Policy", "استعادة سياسة طاقة ويندوز الافتراضية"),

        // Anti-Cheat Pre-Flight Shield (Layer 7)
        ["AntiCheatTitle"] = ("Layer 7 · Anti-Cheat Pre-Flight Security Shield", "الطبقة 7 · درع الأمان المسبق والتوافق مع أنظمة مكافحة الغش"),
        ["AntiCheatSubtitle"] = ("Live kernel code-integrity telemetry verifying compliance with Riot Vanguard, FACEIT AC, EasyAntiCheat, BattlEye, and Ricochet.", "فحص حي لنزاهة النواة للتحقق من حالة Secure Boot وHVCI وTest Signing."),
        ["SecureBootBadgeTitle"] = ("UEFI Secure Boot", "الإقلاع الآمن UEFI Secure Boot"),
        ["HvciBadgeTitle"] = ("HVCI / Memory Integrity", "نزاهة الذاكرة HVCI"),
        ["TestSigningBadgeTitle"] = ("Kernel Test Signing", "وضع توقيع الاختبار Test Signing"),
        ["BadgeSecureBootOn"] = ("SECURE BOOT = ON", "الإقلاع الآمن = مفعّل (ON)"),
        ["BadgeHvciOn"] = ("HVCI = ON", "عزل النواة HVCI = مفعّل (ON)"),
        ["BadgeTestSigningOff"] = ("TEST SIGNING = OFF", "توقيع الاختبار = معطّل (OFF)"),
        ["RefreshSecurityBtn"] = ("Re-Verify Kernel Integrity", "إعادة فحص نزاهة النواة"),
        ["CheckUpdatesBtn"] = ("Check GitHub Releases Update", "التحقق من تحديثات GitHub Releases"),

        // Installer Strings (Part 3)
        ["InstallerTitle"] = ("APEX-HID Suite Setup & Download Manager", "مدير تثبيت وتحميل منظومة APEX-HID Suite"),
        ["InstallerSubtitle"] = ("Official Windows 10/11 Deployment & WHQL hidusbf Filter Provisioning Utility", "أداة التثبيت الرسمية لويندوز 10/11 وتثبيت تعريف hidusbf المعتمد WHQL"),
        ["Step1Header"] = ("1. System Compatibility & Pre-Flight Check", "1. فحص توافق النظام والجاهزية المسبقة"),
        ["Step2Header"] = ("2. Installation Directory & Modular Components", "2. مسار التثبيت والمكونات البرمجية"),
        ["Step3Header"] = ("3. Live WHQL Driver Provisioning, Extraction & Registration", "3. تحميل وتثبيت تعريف WHQL وفك الضغط والتسجيل في النظام"),
        ["OsCheckTitle"] = ("Windows 10 / 11 x64 Kernel Architecture", "معمارية نواة ويندوز 10 / 11 (64-بت)"),
        ["UsbCheckTitle"] = ("USB xHCI 3.0 / 3.1 Host Controller Stack", "مكدس متحكمات منافذ USB xHCI 3.0 / 3.1"),
        ["IntegrityCheckTitle"] = ("Secure Boot & HVCI Anti-Cheat Compliance", "توافق الإقلاع الآمن Secure Boot وHVCI"),
        ["InstallPathLabel"] = ("Target Installation Directory", "مسار مجلد التثبيت المستهدف"),
        ["BrowseFolderBtn"] = ("Browse...", "استعراض..."),
        ["CompCoreTitle"] = ("Core Precision Engine (ApexHid.App & ApexHid.Core)", "محرك الدقة الأساسي (ApexHid.App و ApexHid.Core)"),
        ["CompCoreDesc"] = ("Required · 7-Layer Sony DualSense/DS4 1000Hz serial-locked engine & real WM_INPUT + QPC oscilloscope.", "إلزامي · المحرك السباعي لقفل 1000Hz بالرقم التسلسلي وراسم الذبذبات الفعلي عبر WM_INPUT و QPC."),
        ["CompDriverTitle"] = ("WHQL-Signed hidusbf USB Polling Filter Driver (SweetLow)", "تعريف مرشح تردد USB الموقع رسمياً WHQL (hidusbf.sys)"),
        ["CompDriverDesc"] = ("Recommended · Downloads & installs WHQL hidusbf.inf/.sys via pnputil /add-driver with Secure Boot = ON.", "موصى به · يحمّل ويثبّت تعريف hidusbf.inf/.sys المعتمد WHQL عبر pnputil مع بقاء Secure Boot مفعلاً."),
        ["CompBridgeTitle"] = ("Optional ViGEmBus / HidHide Compatibility Bridge", "جسر التوافق الاختياري مع ViGEmBus و HidHide"),
        ["CompBridgeDesc"] = ("Optional · Configures double-input cloaking and virtual XInput/DualShock bus integration.", "اختياري · يضبط إخفاء الإدخال المزدوج والتكامل مع الناقل الافتراضي XInput/DualShock."),
        ["CreateDesktopShortcut"] = ("Create Desktop & Start Menu Shortcuts", "إنشاء اختصارات على سطح المكتب وقائمة ابدأ"),
        ["RegisterAppsFeatures"] = ("Register Official Uninstaller in Windows Apps & Features", "تسجيل برنامج إلغاء التثبيت الرسمي في تطبيقات وميزات ويندوز"),
        ["StartInstallBtn"] = ("Install APEX-HID Suite & WHQL Driver Now", "تثبيت منظومة APEX-HID وتعريف WHQL الآن"),
        ["UninstallRestoreBtn"] = ("Uninstall & Restore Stock 250Hz USB Settings", "إلغاء التثبيت واستعادة إعدادات USB الافتراضية 250Hz"),
        ["LaunchAppBtn"] = ("Launch APEX-HID Suite", "تشغيل منظومة APEX-HID الآن")
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsArabic
    {
        get => _isArabic;
        set
        {
            if (_isArabic == value) return;
            _isArabic = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentFlowDirection));
            OnPropertyChanged(nameof(PrimaryFontFamily));
            OnPropertyChanged("Item[]");
        }
    }

    public FlowDirection CurrentFlowDirection => _isArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public FontFamily PrimaryFontFamily => _isArabic
        ? new FontFamily("IBM Plex Sans Arabic, Segoe UI, Arial")
        : new FontFamily("Inter, Segoe UI Variable Display, Segoe UI");

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(key, out var pair))
            {
                return _isArabic ? pair.Ar : pair.En;
            }
            return key;
        }
    }

    public void ToggleLanguage()
    {
        IsArabic = !IsArabic;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
