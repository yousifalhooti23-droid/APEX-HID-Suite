using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ApexHid.Core.Services;
using ApexHid.Core.Themes;

namespace ApexHid.Core.Controls;

/// <summary>
/// 60fps RawInput (WM_INPUT) + QueryPerformanceCounter Microsecond Oscilloscope Waveform Renderer.
/// Renders ONLY real hardware packets. Shows "0 Hz / Waiting for Input" when no controller is sending packets.
/// </summary>
public sealed class OscilloscopeGraphControl : FrameworkElement
{
    private readonly double[] _samples = new double[MicrosecondOscilloscope.WaveformBufferCapacity];
    private int _sampleCount;
    private readonly Pen _target1MsPen;
    private readonly Pen _stock4MsPen;
    private readonly Pen _waveformPen;
    private readonly Brush _areaFillBrush;
    private readonly Typeface _monoTypeface;

    public MicrosecondOscilloscope? Oscilloscope { get; set; }

    public OscilloscopeGraphControl()
    {
        FlowDirection = FlowDirection.LeftToRight;
        ClipToBounds = true;
        MinHeight = 210;

        var targetBrush = new SolidColorBrush(Color.FromArgb(145, 5, 150, 105));
        targetBrush.Freeze();
        _target1MsPen = new Pen(targetBrush, 1.1) { DashStyle = new DashStyle([4, 4], 0) };
        _target1MsPen.Freeze();

        var stockBrush = new SolidColorBrush(Color.FromArgb(135, 217, 119, 6));
        stockBrush.Freeze();
        _stock4MsPen = new Pen(stockBrush, 1.1) { DashStyle = new DashStyle([4, 4], 0) };
        _stock4MsPen.Freeze();

        var strokeGrad = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#0284C7"),
            (Color)ColorConverter.ConvertFromString("#38BDF8"),
            new Point(0, 0.5),
            new Point(1, 0.5));
        strokeGrad.Freeze();
        _waveformPen = new Pen(strokeGrad, 2.3)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        _waveformPen.Freeze();

        var fillGrad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(85, 14, 165, 233), 0.0),
                new GradientStop(Color.FromArgb(26, 56, 189, 248), 0.65),
                new GradientStop(Color.FromArgb(0, 56, 189, 248), 1.0)
            ]
        };
        fillGrad.Freeze();
        _areaFillBrush = fillGrad;

        _monoTypeface = new Typeface(new FontFamily("Inter, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        Loaded += (_, _) => ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, ArcticThemeMode e) => InvalidateVisual();

    public void RefreshFrame()
    {
        _sampleCount = Oscilloscope?.CopyWaveformSnapshot(_samples) ?? 0;
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

        dc.DrawRoundedRectangle(bgBrush, gridPen, new Rect(0, 0, w, h), 12, 12);

        double padLeft = 56;
        double padRight = 16;
        double padTop = 18;
        double padBottom = 22;
        double plotW = Math.Max(10, w - padLeft - padRight);
        double plotH = Math.Max(10, h - padTop - padBottom);

        const double maxMs = 5.5;
        double MapY(double ms) => padTop + plotH * (1.0 - Math.Clamp(ms / maxMs, 0.0, 1.0));

        for (int i = 0; i <= 8; i++)
        {
            double x = padLeft + (plotW * i / 8.0);
            dc.DrawLine(gridPen, new Point(x, padTop), new Point(x, padTop + plotH));
        }

        double y1Ms = MapY(1.0);
        double y25Ms = MapY(2.5);
        double y4Ms = MapY(4.0);

        dc.DrawLine(_target1MsPen, new Point(padLeft, y1Ms), new Point(padLeft + plotW, y1Ms));
        dc.DrawLine(gridPen, new Point(padLeft, y25Ms), new Point(padLeft + plotW, y25Ms));
        dc.DrawLine(_stock4MsPen, new Point(padLeft, y4Ms), new Point(padLeft + plotW, y4Ms));

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawAxisLabel(dc, "1.00 ms", 8, y1Ms - 7, dark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105), dpi);
        DrawAxisLabel(dc, "2.50 ms", 8, y25Ms - 7, dark ? Color.FromRgb(148, 179, 208) : Color.FromRgb(59, 85, 114), dpi);
        DrawAxisLabel(dc, "4.00 ms", 8, y4Ms - 7, dark ? Color.FromRgb(251, 191, 36) : Color.FromRgb(217, 119, 6), dpi);

        if (_sampleCount < 2 || Oscilloscope?.IsReceivingPackets != true)
        {
            string waitingText = "0 Hz · Waiting for Real WM_INPUT Controller Packets";
            var brush = new SolidColorBrush(dark ? Color.FromRgb(148, 179, 208) : Color.FromRgb(59, 85, 114));
            brush.Freeze();
            var ft = new FormattedText(
                waitingText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _monoTypeface,
                13.0,
                brush,
                dpi);
            dc.DrawText(ft, new Point(padLeft + (plotW - ft.Width) * 0.5, padTop + (plotH - ft.Height) * 0.5));
            return;
        }

        var strokeGeo = new StreamGeometry();
        var fillGeo = new StreamGeometry();

        using (var sCtx = strokeGeo.Open())
        using (var fCtx = fillGeo.Open())
        {
            for (int i = 0; i < _sampleCount; i++)
            {
                double x = padLeft + (plotW * i / Math.Max(1.0, _sampleCount - 1.0));
                double ms = _samples[i];
                double y = MapY(ms);
                var pt = new Point(x, y);

                if (i == 0)
                {
                    sCtx.BeginFigure(pt, false, false);
                    fCtx.BeginFigure(new Point(x, padTop + plotH), true, true);
                    fCtx.LineTo(pt, true, false);
                }
                else
                {
                    sCtx.LineTo(pt, true, true);
                    fCtx.LineTo(pt, true, true);
                }

                if (i == _sampleCount - 1)
                {
                    fCtx.LineTo(new Point(x, padTop + plotH), true, false);
                }
            }
        }

        strokeGeo.Freeze();
        fillGeo.Freeze();

        dc.DrawGeometry(_areaFillBrush, null, fillGeo);
        dc.DrawGeometry(null, _waveformPen, strokeGeo);

        double lastMs = _samples[_sampleCount - 1];
        var headPt = new Point(padLeft + plotW, MapY(lastMs));
        var headGlow = new SolidColorBrush(Color.FromArgb(100, 14, 165, 233));
        headGlow.Freeze();
        dc.DrawEllipse(headGlow, null, headPt, 7, 7);
        dc.DrawEllipse(Brushes.White, _waveformPen, headPt, 3.2, 3.2);
    }

    private void DrawAxisLabel(DrawingContext dc, string text, double x, double y, Color color, double dpi)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _monoTypeface,
            10.5,
            brush,
            dpi);
        dc.DrawText(ft, new Point(x, y));
    }
}
