using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Fursoy.Browser.App.Controls;

/// <summary>
/// Sağ toolbar popup'larını, görünen yüzeylerinin sağ üst köşesi
/// hedef butonun sağ alt köşesine denk gelecek şekilde yerleştirir.
/// Popup'ın saydam gölge payı hizalama hesabına dahil edilmez.
/// </summary>
public static class ToolbarPopupAnchor
{
    private const double VerticalGap = 4;
    private const double ContentTopInset = 6;

    public static readonly DependencyProperty AlignRightToTargetProperty =
        DependencyProperty.RegisterAttached(
            "AlignRightToTarget", typeof(bool), typeof(ToolbarPopupAnchor),
            new PropertyMetadata(false, OnAlignRightToTargetChanged));

    public static bool GetAlignRightToTarget(DependencyObject element) =>
        (bool)element.GetValue(AlignRightToTargetProperty);

    public static void SetAlignRightToTarget(DependencyObject element, bool value) =>
        element.SetValue(AlignRightToTargetProperty, value);

    public static readonly DependencyProperty AlignTopRightInsideTargetProperty =
        DependencyProperty.RegisterAttached(
            "AlignTopRightInsideTarget", typeof(bool), typeof(ToolbarPopupAnchor),
            new PropertyMetadata(false, OnAlignTopRightInsideTargetChanged));

    public static bool GetAlignTopRightInsideTarget(DependencyObject element) =>
        (bool)element.GetValue(AlignTopRightInsideTargetProperty);

    public static void SetAlignTopRightInsideTarget(DependencyObject element, bool value) =>
        element.SetValue(AlignTopRightInsideTargetProperty, value);

    public static readonly DependencyProperty HorizontalAnchorProperty =
        DependencyProperty.RegisterAttached(
            "HorizontalAnchor", typeof(FrameworkElement), typeof(ToolbarPopupAnchor));

    public static FrameworkElement? GetHorizontalAnchor(DependencyObject element) =>
        (FrameworkElement?)element.GetValue(HorizontalAnchorProperty);

    public static void SetHorizontalAnchor(DependencyObject element, FrameworkElement? value) =>
        element.SetValue(HorizontalAnchorProperty, value);

    private static void OnAlignTopRightInsideTargetChanged(
        DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Popup popup) return;

        if ((bool)args.NewValue)
        {
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var inset = GetSurfaceInset(popup.Child);
                var rightEdge = targetSize.Width;
                var anchor = GetHorizontalAnchor(popup);
                if (anchor != null && popup.PlacementTarget is UIElement target)
                    rightEdge = anchor.TranslatePoint(new Point(anchor.ActualWidth, 0), target).X;
                var origin = CalculateContentOrigin(rightEdge, popupSize.Width,
                    inset.Right, inset.Top, ContentTopInset);
                return [new CustomPopupPlacement(
                    new Point(origin.X + offset.X, origin.Y + offset.Y),
                    PopupPrimaryAxis.Vertical)];
            };
        }
        else
        {
            popup.CustomPopupPlacementCallback = null;
            popup.Placement = PlacementMode.Bottom;
        }
    }

    private static void OnAlignRightToTargetChanged(
        DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Popup popup) return;

        if ((bool)args.NewValue)
        {
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var inset = GetSurfaceInset(popup.Child);
                var origin = CalculateOrigin(targetSize.Width, targetSize.Height,
                    popupSize.Width, inset.Right, inset.Top, VerticalGap);

                return [new CustomPopupPlacement(
                    new Point(origin.X + offset.X, origin.Y + offset.Y),
                    PopupPrimaryAxis.Vertical)];
            };
        }
        else
        {
            popup.CustomPopupPlacementCallback = null;
            popup.Placement = PlacementMode.Bottom;
        }
    }

    private static Thickness GetSurfaceInset(UIElement? child)
    {
        // Ortak gölge host'u Grid > Border; yalın popup'larda Border köktür.
        if (child is Grid grid)
            return grid.Children.OfType<Border>().FirstOrDefault()?.Margin ?? new Thickness();

        return child is Border border ? border.Margin : new Thickness();
    }

    public static Point CalculateOrigin(double targetWidth, double targetHeight,
        double popupWidth, double surfaceRightInset, double surfaceTopInset, double gap)
    {
        return new Point(
            targetWidth - popupWidth + surfaceRightInset,
            targetHeight + gap - surfaceTopInset);
    }

    public static Point CalculateContentOrigin(double rightEdge, double popupWidth,
        double surfaceRightInset, double surfaceTopInset, double topInset)
    {
        return new Point(
            rightEdge - popupWidth + surfaceRightInset,
            topInset - surfaceTopInset);
    }
}
