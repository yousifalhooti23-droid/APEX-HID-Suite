using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ApexHid.Core.Themes;

namespace ApexHid.Core.Controls;

/// <summary>
/// Apple-Inspired "Arctic Liquid Glass" Translucent Card Control.
/// Supports dynamic switching between:
/// - Glacial Light Mode: Frosted white-ice glass (rgba(255, 255, 255, 0.62)), 1px top-edge ice-white specular highlights,
///   and soft glacial blue drop shadows.
/// - Arctic Obsidian Dark Mode: Translucent dark-ice glass (rgba(14, 28, 46, 0.52)), 1px frosted cyan top-edge
///   specular borders (rgba(125, 211, 252, 0.25)), and icy cyan rim reflections.
/// </summary>
public class LiquidGlassCard : ContentControl
{
    private Border? _outerBorder;
    private Border? _specularHoverLayer;
    private Border? _topRimBorder;
    private RadialGradientBrush? _cursorRadialBrush;
    private readonly ScaleTransform _scaleTransform;

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(LiquidGlassCard),
            new PropertyMetadata(new CornerRadius(16)));

    public static readonly DependencyProperty CardPaddingProperty =
        DependencyProperty.Register(
            nameof(CardPadding),
            typeof(Thickness),
            typeof(LiquidGlassCard),
            new PropertyMetadata(new Thickness(20)));

    public static readonly DependencyProperty InteractiveScaleProperty =
        DependencyProperty.Register(
            nameof(InteractiveScale),
            typeof(bool),
            typeof(LiquidGlassCard),
            new PropertyMetadata(true));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Thickness CardPadding
    {
        get => (Thickness)GetValue(CardPaddingProperty);
        set => SetValue(CardPaddingProperty, value);
    }

    public bool InteractiveScale
    {
        get => (bool)GetValue(InteractiveScaleProperty);
        set => SetValue(InteractiveScaleProperty, value);
    }

    public LiquidGlassCard()
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        RenderTransformOrigin = new Point(0.5, 0.5);
        _scaleTransform = new ScaleTransform(1.0, 1.0);
        RenderTransform = _scaleTransform;

        Template = BuildGlassControlTemplate();

        Loaded += OnCardLoaded;
        Unloaded += OnCardUnloaded;
    }

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        ApplyThemeVisuals(ThemeManager.Instance.CurrentTheme);
    }

    private void OnCardUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, ArcticThemeMode mode)
    {
        ApplyThemeVisuals(mode);
    }

    private static ControlTemplate BuildGlassControlTemplate()
    {
        var template = new ControlTemplate(typeof(LiquidGlassCard));

        var outerBorder = new FrameworkElementFactory(typeof(Border), "PART_OuterBorder");
        outerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        outerBorder.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(CornerRadius))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });

        var hostGrid = new FrameworkElementFactory(typeof(Grid));

        var specularLayer = new FrameworkElementFactory(typeof(Border), "PART_SpecularHoverLayer");
        specularLayer.SetValue(OpacityProperty, 0.0);
        specularLayer.SetValue(IsHitTestVisibleProperty, false);
        specularLayer.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(CornerRadius))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });

        var topRim = new FrameworkElementFactory(typeof(Border), "PART_TopRimBorder");
        topRim.SetValue(HeightProperty, 1.0);
        topRim.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        topRim.SetValue(MarginProperty, new Thickness(16, 0, 16, 0));
        topRim.SetValue(IsHitTestVisibleProperty, false);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(MarginProperty, new Binding(nameof(CardPadding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });

        hostGrid.AppendChild(specularLayer);
        hostGrid.AppendChild(topRim);
        hostGrid.AppendChild(presenter);
        outerBorder.AppendChild(hostGrid);

        template.VisualTree = outerBorder;
        return template;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _outerBorder = GetTemplateChild("PART_OuterBorder") as Border;
        _specularHoverLayer = GetTemplateChild("PART_SpecularHoverLayer") as Border;
        _topRimBorder = GetTemplateChild("PART_TopRimBorder") as Border;

        ApplyThemeVisuals(ThemeManager.Instance.CurrentTheme);
    }

    private void ApplyThemeVisuals(ArcticThemeMode mode)
    {
        if (_outerBorder == null) return;

        bool dark = mode == ArcticThemeMode.ArcticDark;

        // 1. Card Surface Fill:
        // Glacial Light: rgba(255, 255, 255, 0.62) -> Alpha 158
        // Arctic Dark: rgba(14, 28, 46, 0.52) -> Alpha 133
        var surfaceBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(142, 14, 28, 46), 0.0),
                    new GradientStop(Color.FromArgb(128, 10, 22, 38), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(168, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(148, 246, 252, 255), 1.0)
                ]
        };
        surfaceBrush.Freeze();
        _outerBorder.Background = surfaceBrush;

        // 2. 1px Top-Edge Specular Border:
        // Glacial Light: Crisp 1px top-edge ice-white highlight -> soft sky blue bottom
        // Arctic Dark: 1px frosted cyan top-edge specular border rgba(125, 211, 252, 0.25) -> Alpha 64
        var borderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = dark
                ?
                [
                    new GradientStop(Color.FromArgb(68, 125, 211, 252), 0.0),
                    new GradientStop(Color.FromArgb(34, 56, 189, 248), 0.45),
                    new GradientStop(Color.FromArgb(15, 14, 165, 233), 1.0)
                ]
                :
                [
                    new GradientStop(Color.FromArgb(245, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(170, 186, 230, 253), 0.45),
                    new GradientStop(Color.FromArgb(105, 56, 189, 248), 1.0)
                ]
        };
        borderBrush.Freeze();
        _outerBorder.BorderBrush = borderBrush;

        // 3. Soft Glacial Blue Drop Shadow (Glacial Light) vs Deep Freeze Drop Shadow (Arctic Dark)
        var shadow = new DropShadowEffect
        {
            Color = dark ? Color.FromRgb(2, 6, 12) : Color.FromRgb(14, 116, 178),
            BlurRadius = dark ? 26 : 22,
            ShadowDepth = 6,
            Direction = 270,
            Opacity = dark ? 0.44 : 0.13
        };
        shadow.Freeze();
        _outerBorder.Effect = shadow;

        // 4. Top 1px Specular Sheen Line
        if (_topRimBorder != null)
        {
            var topRimBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops = dark
                    ?
                    [
                        new GradientStop(Color.FromArgb(0, 125, 211, 252), 0.0),
                        new GradientStop(Color.FromArgb(90, 125, 211, 252), 0.5),
                        new GradientStop(Color.FromArgb(0, 125, 211, 252), 1.0)
                    ]
                    :
                    [
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(235, 255, 255, 255), 0.5),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                    ]
            };
            topRimBrush.Freeze();
            _topRimBorder.Background = topRimBrush;
        }

        // 5. Cursor-Tracking Specular Radial Hover Highlight
        if (_specularHoverLayer != null)
        {
            _cursorRadialBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.0),
                GradientOrigin = new Point(0.5, 0.0),
                RadiusX = 0.65,
                RadiusY = 0.75,
                GradientStops = dark
                    ?
                    [
                        new GradientStop(Color.FromArgb(32, 125, 211, 252), 0.0),
                        new GradientStop(Color.FromArgb(14, 14, 165, 233), 0.45),
                        new GradientStop(Color.FromArgb(0, 14, 165, 233), 1.0)
                    ]
                    :
                    [
                        new GradientStop(Color.FromArgb(110, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(32, 56, 189, 248), 0.45),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                    ]
            };
            _specularHoverLayer.Background = _cursorRadialBrush;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (_specularHoverLayer != null)
        {
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _specularHoverLayer.BeginAnimation(OpacityProperty, fadeIn);
        }

        if (InteractiveScale)
        {
            AnimateScale(1.004, 200);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_cursorRadialBrush == null || ActualWidth <= 1 || ActualHeight <= 1)
            return;

        Point pos = e.GetPosition(this);
        double relX = Math.Clamp(pos.X / ActualWidth, 0.0, 1.0);
        double relY = Math.Clamp(pos.Y / ActualHeight, 0.0, 1.0);

        _cursorRadialBrush.Center = new Point(relX, relY);
        _cursorRadialBrush.GradientOrigin = new Point(relX, relY);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_specularHoverLayer != null)
        {
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _specularHoverLayer.BeginAnimation(OpacityProperty, fadeOut);
        }

        if (InteractiveScale)
        {
            AnimateScale(1.0, 220);
        }
    }

    private void AnimateScale(double target, int durationMs)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}
