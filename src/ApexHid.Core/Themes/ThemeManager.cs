using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ApexHid.Core.Interop;

namespace ApexHid.Core.Themes;

public enum ArcticThemeMode
{
    GlacialLight,
    ArcticDark
}

/// <summary>
/// Manages the "Arctic Liquid Glass" dual-theme system:
/// - Glacial Light Mode (Default - Frosted Ice Blue &amp; White: #F2F8FC -> #E3EFF8 -> #D6E7F4)
/// - Arctic Obsidian Dark Mode (Deep Freeze Blue-Black: #060B12 -> #0A1320 -> #070E18)
/// </summary>
public sealed class ThemeManager : INotifyPropertyChanged
{
    public static ThemeManager Instance { get; } = new();

    private ArcticThemeMode _currentTheme = ArcticThemeMode.GlacialLight;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ArcticThemeMode>? ThemeChanged;

    public ArcticThemeMode CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme == value) return;
            _currentTheme = value;
            ApplyResources(_currentTheme);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(ThemeToggleLabel));
            ThemeChanged?.Invoke(this, _currentTheme);
        }
    }

    public bool IsDarkMode
    {
        get => _currentTheme == ArcticThemeMode.ArcticDark;
        set => CurrentTheme = value ? ArcticThemeMode.ArcticDark : ArcticThemeMode.GlacialLight;
    }

    public string ThemeToggleLabel => "Glacial Light ❄️ | Arctic Dark 🌑";

    public void InitializeDefaultTheme()
    {
        ApplyResources(_currentTheme);
    }

    public void ToggleTheme(Window? activeWindow = null)
    {
        CurrentTheme = _currentTheme == ArcticThemeMode.GlacialLight
            ? ArcticThemeMode.ArcticDark
            : ArcticThemeMode.GlacialLight;

        if (activeWindow != null)
        {
            NativeMethods.ApplyArcticWindowFrame(activeWindow, IsDarkMode);
        }
    }

    public static void ApplyResources(ArcticThemeMode mode)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        bool dark = mode == ArcticThemeMode.ArcticDark;

        // 1. Unified Window Canvas Background Gradient
        var canvasGrad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = dark
                ?
                [
                    new GradientStop((Color)ColorConverter.ConvertFromString("#060B12"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#0A1320"), 0.52),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#070E18"), 1.0)
                ]
                :
                [
                    new GradientStop((Color)ColorConverter.ConvertFromString("#F2F8FC"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#E3EFF8"), 0.52),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#D6E7F4"), 1.0)
                ]
        };
        canvasGrad.Freeze();
        res["WindowCanvasBrush"] = canvasGrad;

        // 2. Ambient Radial Refraction Glows (#7DD3FC at 18% opacity in Glacial Light)
        var topGlow = new RadialGradientBrush
        {
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(38, 56, 189, 248), 0.0),
                    new GradientStop(Color.FromArgb(0, 56, 189, 248), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(46, 125, 211, 252), 0.0), // #7DD3FC @ 18%
                    new GradientStop(Color.FromArgb(0, 125, 211, 252), 1.0)
                ]
        };
        topGlow.Freeze();
        res["AmbientGlowTopLeftBrush"] = topGlow;

        var bottomGlow = new RadialGradientBrush
        {
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(34, 14, 165, 233), 0.0),
                    new GradientStop(Color.FromArgb(0, 14, 165, 233), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(46, 125, 211, 252), 0.0), // #7DD3FC @ 18%
                    new GradientStop(Color.FromArgb(0, 125, 211, 252), 1.0)
                ]
        };
        bottomGlow.Freeze();
        res["AmbientGlowBottomRightBrush"] = bottomGlow;

        // 3. 1px Specular Border Gradient
        var specBorder = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(64, 125, 211, 252), 0.0), // rgba(125, 211, 252, 0.25)
                    new GradientStop(Color.FromArgb(32, 56, 189, 248), 0.45),
                    new GradientStop(Color.FromArgb(14, 14, 165, 233), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(242, 255, 255, 255), 0.0), // Crisp 1px top-edge ice-white highlight
                    new GradientStop(Color.FromArgb(165, 186, 230, 253), 0.45),
                    new GradientStop(Color.FromArgb(95, 125, 211, 252), 1.0)
                ]
        };
        specBorder.Freeze();
        res["SpecularBorderBrush"] = specBorder;

        // 4. Inner Sub-Card & Panel Surfaces
        res["SubCardSurfaceBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(128, 8, 17, 29) : Color.FromArgb(148, 255, 255, 255)));

        res["SubCardBorderBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(48, 125, 211, 252) : Color.FromArgb(72, 2, 132, 199)));

        res["TopPillSurfaceBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(140, 12, 24, 40) : Color.FromArgb(175, 255, 255, 255)));

        res["SidebarPillSurfaceBrush"] = Freeze(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(52, 56, 189, 248), 0.0),
                    new GradientStop(Color.FromArgb(26, 2, 132, 199), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(230, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(185, 224, 242, 254), 1.0)
                ]
        });

        // 5. Typography Brushes
        // Glacial Light: Deep Slate-Navy (#0B192C / #3B5572)
        // Arctic Dark: Crisp Ice-White (#F0F8FF / #94B3D0)
        res["TextPrimaryBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#F0F8FF")
                 : (Color)ColorConverter.ConvertFromString("#0B192C")));

        res["TextSecondaryBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#94B3D0")
                 : (Color)ColorConverter.ConvertFromString("#3B5572")));

        res["TextMutedBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#6889A8")
                 : (Color)ColorConverter.ConvertFromString("#526E8C")));

        // 6. Ice-Cyan & Glacial-Blue Accents (#0284C7 / #0EA5E9 / #38BDF8)
        res["AppleBlueBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#38BDF8")
                 : (Color)ColorConverter.ConvertFromString("#0284C7")));

        res["AppleIndigoBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#0EA5E9")
                 : (Color)ColorConverter.ConvertFromString("#0369A1")));

        res["AppleGreenBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#34D399")
                 : (Color)ColorConverter.ConvertFromString("#059669")));

        res["AppleAmberBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#FBBF24")
                 : (Color)ColorConverter.ConvertFromString("#D97706")));

        res["AppleRedBrush"] = Freeze(new SolidColorBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#F87171")
                 : (Color)ColorConverter.ConvertFromString("#DC2626")));

        var accentGrad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop((Color)ColorConverter.ConvertFromString("#0284C7"), 0.0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#0EA5E9"), 0.55),
                new GradientStop((Color)ColorConverter.ConvertFromString("#38BDF8"), 1.0)
            ]
        };
        accentGrad.Freeze();
        res["AppleAccentGradientBrush"] = accentGrad;

        // 7. Interactive Secondary Button & Input Brushes
        res["SecondaryButtonBgBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(140, 16, 32, 52) : Color.FromArgb(185, 255, 255, 255)));

        res["SecondaryButtonHoverBgBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(190, 24, 46, 74) : Color.FromArgb(235, 255, 255, 255)));

        res["InputSurfaceBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(145, 10, 20, 34) : Color.FromArgb(200, 255, 255, 255)));

        res["ConsoleSurfaceBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(195, 5, 11, 19) : Color.FromArgb(195, 232, 243, 252)));

        res["ToggleTrackOffBrush"] = Freeze(new SolidColorBrush(
            dark ? Color.FromArgb(90, 125, 211, 252) : Color.FromArgb(95, 2, 132, 199)));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze)
            freezable.Freeze();
        return freezable;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
