using System;
using System.Numerics;
using Lutris.Interop;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Lutris.Controls;

/// <summary>
/// Mouse wheel scrolling for a <see cref="ScrollView"/> that keeps up with the wheel. ScrollView's own
/// wheel handling turns each notch into a slow glide of about 40 pixels, so the content crawls behind
/// the wheel. This gives each notch a brisker push instead, sized from the Windows "lines to scroll"
/// setting. The motion still runs on the compositor as inertia, so quick notches add up smoothly and it
/// settles in about a third of a second. Touch, touchpad, keyboard and scroll bar input stay with ScrollView.
/// </summary>
internal static class WheelScrolling
{
    private const double LineHeight = 40;
    private const uint ScrollByPage = uint.MaxValue;

    // The velocity falls off as e^(-k t). With k = 10 per second it is down to 5% after 0.3 s, and an
    // initial velocity v covers v / k in total.
    private const double DecayPerSecond = 10;
    private static readonly float InertiaDecayRate = (float)(1 - Math.Exp(-DecayPerSecond));

    public static void Attach(ScrollView scrollView)
    {
        scrollView.IgnoredInputKinds |= ScrollingInputKinds.MouseWheel;
        scrollView.PointerWheelChanged += OnPointerWheelChanged;
    }

    private static void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var scrollView = (ScrollView)sender;
        var properties = e.GetCurrentPoint(scrollView).Properties;
        if (properties.IsHorizontalMouseWheel || scrollView.ScrollableHeight <= 0) return;

        var lines = WindowInterop.GetWheelScrollLines();
        var notchDistance = lines == ScrollByPage ? scrollView.ViewportHeight : lines * LineHeight;
        var distance = -properties.MouseWheelDelta / 120.0 * notchDistance;

        scrollView.AddScrollVelocity(
            new Vector2(0, (float)(distance * DecayPerSecond)),
            new Vector2(InertiaDecayRate, InertiaDecayRate));
        e.Handled = true;
    }
}
