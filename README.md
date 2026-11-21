# APEX-HID-Suite · منظومة الدقة السباعية لوحدات التحكم

<div align="center">

**Apple-Inspired "Liquid Glass" 7-Layer Sony DualSense / DualShock 4 Precision & 1000Hz Serial-Locked Telemetry Suite for Windows 10/11 (.NET 8)**  
**منظومة احترافية بتصميم الزجاج السائل (Liquid Glass) لقفل تردد 1000Hz بالرقم التسلسلي والقياس الفوري بالميكروثانية لويندوز 10/11**

[![.NET 8.0 Windows](https://img.shields.io/badge/.NET-8.0%20Windows%20x64-0A84FF)](https://dotnet.microsoft.com/)
[![Design System](https://img.shields.io/badge/UI-Apple%20Liquid%20Glass-5E5CE6)](#part-1-apple-inspired-liquid-glass-design-system)
[![Anti-Cheat Ready](https://img.shields.io/badge/Secure%20Boot%20%26%20HVCI-ON%20%C2%B7%20Test%20Signing%20OFF-30D158)](#7-layer-controller-precision-engine-apexhidcore)
[![License: MIT](https://img.shields.io/badge/License-MIT-F5F7FA)](LICENSE)

</div>

---

## 🌐 English Documentation

### Overview
**APEX-HID-Suite** is a production-grade Windows 10/11 hardware telemetry, serial-locked USB polling, and onboard NVS flash calibration solution engineered in **C# (.NET 8 WPF)**. Built around an authentic Apple-inspired **"Liquid Glass"** obsidian design language (`#0A0C10`), it eliminates cheap gamer RGB clutter in favor of surgical microsecond accuracy, native Arabic (RTL) & English (LTR) switching, and 100% kernel anti-cheat compliance.

### Repository Architecture
```text
APEX-HID-Suite/
├── .github/workflows/release-build.yml   # Automated CI/CD & GitHub Releases Publisher
├── src/
│   ├── ApexHid.Core/                     # Shared 7-Layer Hardware Engine & Liquid Glass Controls
│   ├── ApexHid.App/                      # Main Desktop Precision Suite (ApexHid.App.exe)
│   └── ApexHid.Installer/                # Windows Setup & Download Manager (APEX-HID-Setup.exe)
├── APEX-HID-Suite.sln                    # Visual Studio / .NET 8 Solution
├── LICENSE                               # MIT License
└── README.md                             # Bilingual Documentation (EN / AR)
```

### Part 1: Apple-Inspired "Liquid Glass" Design System
1. **Unified Seamless Canvas**:
   - Borderless custom `WindowChrome` + DWM dark framing with a single unified obsidian-glass background (`#0A0C10`) flowing seamlessly across the header, sidebar, and workspace.
   - Translucent `LiquidGlassCard` surfaces (`rgba(255, 255, 255, 0.035)`) featuring **1px top-edge specular light gradients** (`14%` white at top to `2%` at bottom), soft ambient shadows, and **cursor-tracking radial specular hover highlights**.
   - Monochrome typography paired with refined Apple system accents (`#0A84FF` / `#5E5CE6` / `#30D158`).
2. **Fluid Spring Animations & Interactive Transitions**:
   - `CubicBezier(0.22, 1, 0.36, 1)` (`KeySpline="0.22,1,0.36,1"`) page transitions and a **gliding Liquid Glass sidebar indicator pill**.
   - Tactile scale & opacity micro-interactions on cards, buttons, and iOS/macOS pill switches.
3. **Native Bilingual Support (Arabic RTL & English LTR)**:
   - Instant top-bar toggle (`"العربية | EN"`) dynamically switches the entire app's `FlowDirection` (`RightToLeft` / `LeftToRight`) and swaps between `"IBM Plex Sans Arabic"` and `"Inter"` with **Tabular Numerals** (`Typography.NumeralAlignment="Tabular"`).

### Part 2: 7-Layer Controller Precision Engine (`ApexHid.Core`)
| Layer | Module | Engineering Capability |
| :--- | :--- | :--- |
| **Layer 1** | `SonyControllerDetector` | Multi-revision detection for **PS4 V1 (`PID_05C4`)**, **PS4 V2 (`PID_09CC`)**, **DualSense BDM-010..050 (`PID_0CE6`)**, and **DualSense Edge (`PID_0DF2`)** via `SetupAPI` & `WM_DEVICECHANGE`. |
| **Layer 2** | `SerialLockPollingEngine` | Locks **1000Hz (`1.00ms`, `bInterval=1`)** exclusively to registered Serial Numbers in the cryptographic vault while keeping unregistered controllers at stock **250Hz (`4.00ms`, `bInterval=4`)**. |
| **Layer 3** | `ChipsetAuthenticityAnalyzer` | Verifies whether the physical MCU (`SIE CXD90064GG` / `CXD90071GG` / `NXP Kinetis`) genuinely accepted `1ms` polling or is a hardware-locked counterfeit clone. |
| **Layer 4** | `UsbPowerAndMmcssOptimizer` | Disables Root Hub **USB Selective Suspend** and elevates the HID reader thread via `AvSetMmThreadCharacteristics("Games", AVRT_PRIORITY_CRITICAL)` + `timeBeginPeriod(1)`. |
| **Layer 5** | `NvsFlashCalibrator` | Direct HID Feature Reports (`0x82` / `0x83`) to calibrate stick center and 360° circularity permanently inside onboard controller NVS flash memory. |
| **Layer 6** | `MicrosecondOscilloscope` | Real-time 60fps `RawInput` + `QueryPerformanceCounter` waveform showing exact `Hz`, packet delta (`ms`), jitter variance (`µs`), and a 36-sector polar **Stick Circularity Heatmap**. |
| **Layer 7** | `AntiCheatPreFlightShield` | Live kernel verification via `NtQuerySystemInformation(0x67)` & UEFI registry confirming **Secure Boot = ON**, **HVCI = ON**, and **Test Signing = OFF**. |

### Part 3: AAA Windows Setup & Download Manager (`APEX-HID-Setup.exe`)
- Built with the exact same **Liquid Glass** UI and instant Arabic/English toggle.
- Pre-flight Windows 10/11 x64 compatibility check, custom installation folder picker, modular component checkboxes (**Core Engine**, **WHQL USB Filter**, **Optional ViGEmBus/HidHide Bridge**), live `MB/s` download & extraction progress bar, Desktop/Start Menu shortcut creation, official Windows **Apps & Features** registry integration, clean `--uninstall` default USB restoration, and a GitHub Releases Auto-Updater.

### Build & Run Instructions
```powershell
# 1. Build the entire solution in Release mode
dotnet build APEX-HID-Suite.sln -c Release

# 2. Launch the main desktop application
dotnet run --project src/ApexHid.App -c Release

# 3. Launch the standalone Setup & Download Manager (APEX-HID-Setup.exe)
dotnet run --project src/ApexHid.Installer -c Release
```

---

<div dir="rtl">

## 🌍 التوثيق العربي (Arabic Documentation)

### نظرة عامة
تُعد منظومة **APEX-HID-Suite** حلاً هندسياً متكاملاً لنظامي **Windows 10 و Windows 11** مبنياً بلغة **C# (.NET 8 WPF)** لإدارة وحدات تحكم سوني الاحترافية وقفل تردد الاستطلاع **1000Hz (1ms)** حصرياً للأرقام التسلسلية المعتمدة، مع معايرة ذاكرة الفلاش الدائمة **NVS** وراسم ذبذبات فوري بالميكروثانية بسرعة 60 إطاراً في الثانية.

### المزايا الرئيسية
1. **نظام تصميم الزجاج السائل (Apple-Inspired Liquid Glass)**:
   - نافذة موحدة بدون حواف تقليدية بخلفية زجاج الأوبسيديان الداكن (`#0A0C10`) تتدفق بسلاسة بين الشريط العلوي والقائمة الجانبية ومساحة العمل.
   - بطاقات زجاجية شفافة (`rgba(255, 255, 255, 0.035)`) مع إضاءة حافة علوية بسمك `1px` وتوهج ضوئي يتتبع حركة مؤشر الفأرة في الوقت الحقيقي.
   - انتقالات حركية فيزيائية سلسة وفق منحنى `CubicBezier(0.22, 1, 0.36, 1)` ومؤشر قائمة زجاجي عائم ينزلق بنعومة.
   - دعم ثنائي اللغة فوري (**العربية RTL والإنجليزية LTR**) عبر زر التبديل العلوي (`العربية | EN`) مع خط **IBM Plex Sans Arabic** والأرقام الجدولية الثابتة (**Tabular Numerals**) لمنع اهتزاز قراءات التردد والزمن.

2. **محرك الدقة السباعي (`ApexHid.Core`)**:
   - **الطبقة 1**: الكشف التلقائي عن طرازات **PS4 V1 (`05C4`)** و **PS4 V2 (`09CC`)** و **DualSense BDM-010..050 (`0CE6`)** و **DualSense Edge (`0DF2`)** عبر `SetupAPI` و `WM_DEVICECHANGE`.
   - **الطبقة 2**: قفل تردد **1000Hz (1ms)** حصرياً للأرقام التسلسلية المسجلة في الخزنة مع إبقاء الوحدات غير المسجلة على تردد **250Hz (4ms)** دون أي تعديل في BIOS أو تعطيل لـ Secure Boot.
   - **الطبقة 3**: كاشف أصالة الشريحة للتحقق من قبول المعالج الفيزيائي لتردد `1ms` الفعلي وكشف الشرائح المقلدة المقفولة عتادياً.
   - **الطبقة 4**: تعطيل التعليق الانتقائي لطاقة USB (`USB Selective Suspend`) ورفع أولوية خيط قراءة HID عبر `AvSetMmThreadCharacteristics`.
   - **الطبقة 5**: إرسال تقارير `HID Feature Reports (0x82 / 0x83)` لمعايرة نقطة المركز ودائرية العصا وحفظها دائماً داخل ذاكرة الفلاش **NVS** لوحدة التحكم.
   - **الطبقة 6**: راسم ذبذبات فوري بالميكروثانية (`RawInput + QueryPerformanceCounter`) بسرعة 60fps يعرض التردد الفعلي `Hz` والفاصل الزمني `ms` وتباين الارتجاف `µs` والخريطة الحرارية القطبية لدائرية العصا 360°.
   - **الطبقة 7**: درع التوافق المسبق مع أنظمة مكافحة الغش (Vanguard / FACEIT / EAC / BattlEye) مع شارات تحقق حية لـ **Secure Boot = ON** و **HVCI = ON** و **Test Signing = OFF**.

3. **مدير التثبيت والتحميل الاحترافي (`APEX-HID-Setup.exe`)**:
   - فحص توافق ويندوز 10/11، اختيار مسار التثبيت والمكونات، شريط تقدم زجاجي مباشر مع سرعة التحميل بـ `MB/s`، إنشاء اختصارات سطح المكتب وقائمة ابدأ، التسجيل الرسمي في **تطبيقات وميزات ويندوز (Apps & Features)** مع أداة إلغاء تثبيت نظيفة تعيد إعدادات USB الافتراضية، ووحدة التحديث التلقائي عبر **GitHub Releases**.

</div>
