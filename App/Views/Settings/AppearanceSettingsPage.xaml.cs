using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Lertaro.App.Views.Settings;

public partial class AppearanceSettingsPage : System.Windows.Controls.UserControl
{
    private static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        "ScrollOffset", typeof(double), typeof(AppearanceSettingsPage), new PropertyMetadata(0d, (target, args) =>
            ((AppearanceSettingsPage)target).PageScroll.ScrollToVerticalOffset((double)args.NewValue)));
    private bool _scrolling;
    private bool _directScrollPending;
    private double _scrollTarget;
    private int _scrollGeneration;

    public AppearanceSettingsPage()
    {
        InitializeComponent();
        Unloaded += PageScroll_StopAnimation;
    }

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || SystemParameters.WheelScrollLines == 0) return;
        // Leave wheel input to editable controls and independently scrollable nested content.
        for (var source = e.OriginalSource as DependencyObject; source != null && source != PageScroll;
             source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.ComboBox or RangeBase || source is ScrollViewer { ScrollableHeight: > 0 })
            {
                PageScroll_StopAnimation(null, EventArgs.Empty);
                return;
            }
        }
        var current = PageScroll.VerticalOffset;
        var distance = -e.Delta / 120d * (SystemParameters.WheelScrollLines < 0
            ? PageScroll.ViewportHeight : SystemParameters.WheelScrollLines * 16d);
        var start = (_scrolling || _directScrollPending) && Math.Sign(_scrollTarget - current) == Math.Sign(distance)
            ? _scrollTarget : current;
        var target = Math.Clamp(start + distance, 0, PageScroll.ScrollableHeight);
        if (target == current)
        {
            if (_scrolling || _directScrollPending)
            {
                var direct = _directScrollPending;
                PageScroll_StopAnimation(null, EventArgs.Empty);
                if (direct) PageScroll.ScrollToVerticalOffset(current);
                e.Handled = true;
            }
            return;
        }
        e.Handled = true;
        PageScroll_StopAnimation(null, EventArgs.Empty);
        _scrollTarget = target;
        // Without a rendering tier the animation clock can stall, leaving the wheel one step behind.
        if (!SystemParameters.ClientAreaAnimation || RenderCapability.Tier == 0)
        {
            _directScrollPending = true;
            var directGeneration = ++_scrollGeneration;
            PageScroll.ScrollToVerticalOffset(target);
            Dispatcher.BeginInvoke(() =>
            {
                if (directGeneration == _scrollGeneration) _directScrollPending = false;
            }, DispatcherPriority.Loaded);
            return;
        }
        _scrolling = true;
        var generation = ++_scrollGeneration;
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _scrollGeneration) return;
            _scrolling = false;
            SetValue(ScrollOffsetProperty, _scrollTarget);
            BeginAnimation(ScrollOffsetProperty, null);
        };
        BeginAnimation(ScrollOffsetProperty, animation);
    }

    private void PageScroll_StopAnimation(object? sender, EventArgs e)
    {
        if (_directScrollPending)
        {
            _directScrollPending = false;
            ++_scrollGeneration;
        }
        if (!_scrolling) return;
        ++_scrollGeneration;
        _scrolling = false;
        SetValue(ScrollOffsetProperty, PageScroll.VerticalOffset);
        BeginAnimation(ScrollOffsetProperty, null);
    }

    // See ThemeCardContainerStyle's EventSetter: swallows the auto-scroll WPF triggers whenever a
    // theme card is selected (by click or programmatically), instead of letting it yank the whole
    // settings page to wherever that card happens to sit.
    private void ThemeCard_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        => e.Handled = true;
}
