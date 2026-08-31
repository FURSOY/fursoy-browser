using System.Windows;
using System.Windows.Media;

namespace Fursoy.Browser.App.Controls;

/// <summary>
/// Chrome'un sekme silueti. Düz bir yuvarlatılmış dikdörtgen DEĞİLDİR:
/// üst iki köşe içe doğru yuvarlaktır (konveks), alt iki köşe ise dışa
/// doğru açılan ters (konkav) yaylarla araç çubuğuna akar. Chromium'un
/// kendi kaynağındaki şema (horizontal_tab_style_views.cc):
///
///     ╭─────────╮
///     │ Content │
///   ┌─╯         ╰─┐
///
/// Yarıçaplar Chromium'un tab_style.cc dosyasındaki gerçek değerlerdir:
/// üst köşe 10, alt (uzantı) köşe 12 DIP. Yay yönleri de aynı: üst köşeler
/// saat yönünde, alt köşeler saat yönünün tersine.
///
/// Şekil genişliğe göre yeniden hesaplandığı için sekme genişliği
/// değişse de doğru kalır.
/// </summary>
public class TabShape : FrameworkElement
{
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(TabShape),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TopRadiusProperty =
        DependencyProperty.Register(nameof(TopRadius), typeof(double), typeof(TabShape),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomRadiusProperty =
        DependencyProperty.Register(nameof(BottomRadius), typeof(double), typeof(TabShape),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double TopRadius
    {
        get => (double)GetValue(TopRadiusProperty);
        set => SetValue(TopRadiusProperty, value);
    }

    public double BottomRadius
    {
        get => (double)GetValue(BottomRadiusProperty);
        set => SetValue(BottomRadiusProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Fill is null) return;

        var geometry = BuildGeometry(RenderSize.Width, RenderSize.Height);
        if (geometry is null) return;

        drawingContext.DrawGeometry(Fill, null, geometry);
    }

    private Geometry? BuildGeometry(double width, double height)
    {
        if (width <= 0 || height <= 0) return null;

        var bottom = Math.Min(BottomRadius, Math.Min(width / 2, height));
        var top = Math.Min(TopRadius, Math.Min(width / 2 - bottom, height - bottom));
        if (top < 0) top = 0;

        var figure = new PathFigure { StartPoint = new Point(0, height), IsClosed = true, IsFilled = true };

        // Alt-sol uzantı: dışa açılan ters yay (araç çubuğuna akar).
        figure.Segments.Add(new ArcSegment(new Point(bottom, height - bottom), new Size(bottom, bottom),
            0, false, SweepDirection.Counterclockwise, true));
        // Sol kenar yukarı.
        figure.Segments.Add(new LineSegment(new Point(bottom, top), true));
        // Üst-sol köşe: normal (konveks) yuvarlaklık.
        figure.Segments.Add(new ArcSegment(new Point(bottom + top, 0), new Size(top, top),
            0, false, SweepDirection.Clockwise, true));
        // Üst kenar.
        figure.Segments.Add(new LineSegment(new Point(width - bottom - top, 0), true));
        // Üst-sağ köşe.
        figure.Segments.Add(new ArcSegment(new Point(width - bottom, top), new Size(top, top),
            0, false, SweepDirection.Clockwise, true));
        // Sağ kenar aşağı.
        figure.Segments.Add(new LineSegment(new Point(width - bottom, height - bottom), true));
        // Alt-sağ uzantı.
        figure.Segments.Add(new ArcSegment(new Point(width, height), new Size(bottom, bottom),
            0, false, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
