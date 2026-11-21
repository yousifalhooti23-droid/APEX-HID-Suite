using System.Windows;
using System.Windows.Media;
using ApexHid.Core.Services;
using ApexHid.Core.Themes;

namespace ApexHid.Core.Controls;

/// <summary>
/// Real-Time 60fps 360-Degree Polar Stick Circularity Heatmap Renderer.
/// Visualizes real captured analog stick polar boundary sectors from WM_INPUT packets.
/// </summary>
public sealed class CircularityHeatmapControl : FrameworkElement
{
    private readonly float[] _leftSectors = new float[MicrosecondOscilloscope.PolarSectorCount];
    private readonly float[] _rightSectors = new float[MicrosecondOscilloscope.PolarSectorCount];
    private readonly Pen _leftPolygonPen;
    private readonly Brush _leftPolygonFill;
    private readonly Pen _rightPolygonPen;

    public MicrosecondOscilloscope? Oscilloscope { get; set; }

    public CircularityHeatmapControl()
    {
        FlowDirection = FlowDirection.LeftToRight;
        ClipToBounds = true;
        MinHeight = 210;
        MinWidth = 210;

        var leftStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
        leftStroke.Freeze();
        _leftPolygonPen = new Pen(leftStroke, 2.1);
        _leftPolygonPen.Freeze();

        var radialFill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(22, 14, 165, 233), 0.0),
                new GradientStop(Color.FromArgb(64, 14, 165, 233), 0.78),
                new GradientStop(Color.FromArgb(105, 2, 132, 199), 1.0)
            ]
        };
        radialFill.Freeze();
        _leftPolygonFill = radialFill;

        var rightStroke = new SolidColorBrush(Color.FromArgb(170, 5, 150, 105));
        rightStroke.Freeze();
        _rightPolygonPen = new Pen(rightStroke, 1.5);
        _rightPolygonPen.Freeze();

        Loaded += (_, _) => ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, ArcticThemeMode e) => InvalidateVisual();

    public void RefreshFrame()
    {
        Oscilloscope?.CopyPolarHeatmapSnapshot(_leftSectors, _rightSectors);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 20 || h <= 20) return;

        bool dark = ThemeManager.Instance.IsDarkMode;

        var bgBrush = new SolidColorBrush(dark
            ? Color.FromArgb(145, 5, 11, 20)
            : Color.FromArgb(165, 235, 245, 253));
        bgBrush.Freeze();

        var gridBrush = new SolidColorBrush(dark
            ? Color.FromArgb(28, 125, 211, 252)
            : Color.FromArgb(45, 2, 132, 199));
        gridBrush.Freeze();
        var gridPen = new Pen(gridBrush, 1.0);
        gridPen.Freeze();

        var unitBrush = new SolidColorBrush(dark
            ? Color.FromArgb(130, 240, 248, 255)
            : Color.FromArgb(145, 11, 25, 44));
        unitBrush.Freeze();
        var unitCirclePen = new Pen(unitBrush, 1.2) { DashStyle = new DashStyle([3, 3], 0) };
        unitCirclePen.Freeze();

        dc.DrawRoundedRectangle(bgBrush, gridPen, new Rect(0, 0, w, h), 12, 12);

        double cx = w * 0.5;
        double cy = h * 0.5;
        double unitRadius = Math.Min(w, h) * 0.37;

        dc.DrawEllipse(null, gridPen, new Point(cx, cy), unitRadius * 0.25, unitRadius * 0.25);
        dc.DrawEllipse(null, gridPen, new Point(cx, cy), unitRadius * 0.50, unitRadius * 0.50);
        dc.DrawEllipse(null, gridPen, new Point(cx, cy), unitRadius * 0.75, unitRadius * 0.75);
        dc.DrawEllipse(null, unitCirclePen, new Point(cx, cy), unitRadius, unitRadius);

        dc.DrawLine(gridPen, new Point(cx - unitRadius * 1.15, cy), new Point(cx + unitRadius * 1.15, cy));
        dc.DrawLine(gridPen, new Point(cx, cy - unitRadius * 1.15), new Point(cx, cy + unitRadius * 1.15));

        if (_rightSectors.Any(r => r > 0.1f))
            DrawPolarEnvelope(dc, _rightSectors, cx, cy, unitRadius, null, _rightPolygonPen);
        if (_leftSectors.Any(r => r > 0.1f))
            DrawPolarEnvelope(dc, _leftSectors, cx, cy, unitRadius, _leftPolygonFill, _leftPolygonPen);

        float lx = Oscilloscope?.CurrentLeftX ?? 0.0f;
        float ly = Oscilloscope?.CurrentLeftY ?? 0.0f;
        var stickPt = new Point(cx + lx * unitRadius, cy + ly * unitRadius);

        dc.DrawLine(_leftPolygonPen, new Point(cx, cy), stickPt);
        var glowBrush = new SolidColorBrush(Color.FromArgb(95, 14, 165, 233));
        glowBrush.Freeze();
        dc.DrawEllipse(glowBrush, null, stickPt, 7, 7);
        dc.DrawEllipse(Brushes.White, _leftPolygonPen, stickPt, 3.2, 3.2);
    }

    private static void DrawPolarEnvelope(
        DrawingContext dc,
        float[] sectors,
        double cx,
        double cy,
        double unitRadius,
        Brush? fill,
        Pen stroke)
    {
        int n = sectors.Length;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < n; i++)
            {
                double angle = (i * 2.0 * Math.PI) / n;
                double r = Math.Clamp(sectors[i], 0.0f, 1.16f) * unitRadius;
                var pt = new Point(cx + Math.Cos(angle) * r, cy + Math.Sin(angle) * r);

                if (i == 0)
                    ctx.BeginFigure(pt, fill != null, true);
                else
                    ctx.LineTo(pt, true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(fill, stroke, geo);
    }
}
