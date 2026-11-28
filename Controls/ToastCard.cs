using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamGuard.Models;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace StreamGuard.Controls;

public class ToastCard : Border
{
    public ToastNotification Notification { get; }
    public string Message => Notification.Message;
    public ToastType Type => Notification.Type;
    public int RepeatCount => Notification.RepeatCount;
    public bool IsPaused { get; private set; }
    public bool IsDismissed { get; private set; }
    public double Progress { get; private set; } = 1.0;

    public event EventHandler? Dismissed;

    private readonly Border _countdownBar;
    private readonly ScaleTransform _countdownBarScale;
    private readonly Border _repeatBadgeBorder;
    private readonly TextBlock _repeatBadgeText;
    private readonly TextBlock _messageTextBlock;
    private readonly TranslateTransform _translateTransform;
    private readonly ScaleTransform _scaleTransform;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch;
    private int _totalDurationMs;

    private bool _isDragging;
    private WpfPoint _dragStartPoint;

    public ToastCard(ToastNotification notification)
    {
        Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        _totalDurationMs = Math.Max(200, notification.DurationMs);

        Width = 340;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Margin = new Thickness(0, 0, 0, 8);
        Cursor = WpfCursors.Arrow;
        ClipToBounds = false; // Allow outer drop shadow to extend beyond border

        var (accentColor, bgResKey, borderResKey, bgFallback, borderFallback, iconGeom) = GetTypePalette(notification.Type);
        var accentBrush = new SolidColorBrush(accentColor);
        accentBrush.Freeze();

        ApplyBrush(this, BackgroundProperty, bgResKey, bgFallback);
        ApplyBrush(this, BorderBrushProperty, borderResKey, borderFallback);
        Effect = new DropShadowEffect
        {
            BlurRadius = 14,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = 0.45,
            Color = Colors.Black
        };

        _translateTransform = new TranslateTransform(0, 0);
        _scaleTransform = new ScaleTransform(1, 1);
        RenderTransform = new TransformGroup
        {
            Children = { _translateTransform, _scaleTransform }
        };
        RenderTransformOrigin = new WpfPoint(0.5, 0.5);

        // Visual tree layout (clip internal content so corners and tracks stay tidy)
        var outerGrid = new Grid { ClipToBounds = true };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Pixel) });

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) }); // Left accent bar
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Message & badge
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Close button

        // Column 0: Left accent strip
        var leftAccent = new Border
        {
            Background = accentBrush,
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(leftAccent, 0);
        contentGrid.Children.Add(leftAccent);

        // Column 1: Icon
        var iconPath = new Path
        {
            Data = iconGeom,
            Stroke = accentBrush,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 15,
            Height = 15,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 10, 8, 10)
        };
        Grid.SetColumn(iconPath, 1);
        contentGrid.Children.Add(iconPath);

        // Column 2: Text and Repeat Badge
        var textContainer = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 4, 8)
        };

        _messageTextBlock = new TextBlock
        {
            Text = notification.Message,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 230,
            MaxHeight = 48,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBrush(_messageTextBlock, TextBlock.ForegroundProperty, "ToastTextBrush", WpfColor.FromRgb(0xf1, 0xf5, 0xf9));
        textContainer.Children.Add(_messageTextBlock);

        var badgeBgBrush = new SolidColorBrush(WpfColor.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B));
        badgeBgBrush.Freeze();
        _repeatBadgeBorder = new Border
        {
            Background = badgeBgBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = notification.RepeatCount > 1 ? Visibility.Visible : Visibility.Collapsed
        };
        _repeatBadgeText = new TextBlock
        {
            Text = $"x{notification.RepeatCount}",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var repeatTextKey = notification.Type switch
        {
            ToastType.Success => "SuccessTextBrush",
            ToastType.Error => "DangerTextBrush",
            ToastType.Warning => "WarningTextBrush",
            _ => "InfoTextBrush"
        };
        ApplyBrush(_repeatBadgeText, TextBlock.ForegroundProperty, repeatTextKey, accentColor);
        _repeatBadgeBorder.Child = _repeatBadgeText;
        textContainer.Children.Add(_repeatBadgeBorder);

        Grid.SetColumn(textContainer, 2);
        contentGrid.Children.Add(textContainer);

        // Column 3: Close button
        var closeButton = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = WpfBrushes.Transparent,
            Cursor = WpfCursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 8, 0)
        };
        var closeCross = new Path
        {
            Data = Geometry.Parse("M5,5 L13,13 M13,5 L5,13"),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 9,
            Height = 9,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBrush(closeCross, System.Windows.Shapes.Shape.StrokeProperty, "ToastCloseStrokeBrush", WpfColor.FromRgb(0x94, 0xa3, 0xb8));
        closeButton.Child = closeCross;
        closeButton.MouseEnter += (s, e) =>
        {
            ApplyBrush(closeButton, BackgroundProperty, "ToastCloseHoverBgBrush", WpfColor.FromArgb(0x22, 0xff, 0xff, 0xff));
            ApplyBrush(closeCross, System.Windows.Shapes.Shape.StrokeProperty, "ToastTextBrush", Colors.White);
        };
        closeButton.MouseLeave += (s, e) =>
        {
            closeButton.Background = WpfBrushes.Transparent;
            ApplyBrush(closeCross, System.Windows.Shapes.Shape.StrokeProperty, "ToastCloseStrokeBrush", WpfColor.FromRgb(0x94, 0xa3, 0xb8));
        };
        closeButton.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            Dismiss(animated: true);
        };
        Grid.SetColumn(closeButton, 3);
        contentGrid.Children.Add(closeButton);

        Grid.SetRow(contentGrid, 0);
        outerGrid.Children.Add(contentGrid);

        // Row 1: Countdown bar
        var track = new Grid
        {
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Height = 2
        };
        ApplyBrush(track, System.Windows.Controls.Panel.BackgroundProperty, "ToastTrackBgBrush", WpfColor.FromArgb(0x18, 0xff, 0xff, 0xff));
        _countdownBarScale = new ScaleTransform(1.0, 1.0);
        _countdownBar = new Border
        {
            Background = accentBrush,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Height = 2,
            RenderTransformOrigin = new WpfPoint(0, 0.5),
            RenderTransform = _countdownBarScale
        };
        track.Children.Add(_countdownBar);
        Grid.SetRow(track, 1);
        outerGrid.Children.Add(track);

        Child = outerGrid;

        // Timer setup
        _stopwatch = new Stopwatch();
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _timer.Tick += Timer_Tick;

        // Event hooks
        MouseEnter += ToastCard_MouseEnter;
        MouseLeave += ToastCard_MouseLeave;
        MouseLeftButtonDown += ToastCard_MouseLeftButtonDown;
        MouseMove += ToastCard_MouseMove;
        MouseLeftButtonUp += ToastCard_MouseLeftButtonUp;
        LostMouseCapture += ToastCard_LostMouseCapture;
        PreviewMouseWheel += ToastCard_PreviewMouseWheel;

        // Slide-in entrance animation
        _translateTransform.X = 100;
        Opacity = 0.0;
        var animSlideIn = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var animFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160));
        animSlideIn.Completed += (_, _) =>
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _translateTransform.X = 0.0;
        };
        animFadeIn.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1.0;
        };
        _translateTransform.BeginAnimation(TranslateTransform.XProperty, animSlideIn);
        BeginAnimation(OpacityProperty, animFadeIn);

        // Start timer
        _stopwatch.Start();
        _timer.Start();
    }

    private static void ApplyBrush(FrameworkElement target, DependencyProperty property, string resourceKey, WpfColor fallbackColor)
    {
        if (System.Windows.Application.Current != null && System.Windows.Application.Current.TryFindResource(resourceKey) != null)
        {
            target.SetResourceReference(property, resourceKey);
            return;
        }

        var brush = new SolidColorBrush(fallbackColor);
        brush.Freeze();
        target.SetValue(property, brush);
    }

    private static (WpfColor Accent, string BgResKey, string BorderResKey, WpfColor BgFallback, WpfColor BorderFallback, Geometry Icon) GetTypePalette(ToastType type)
    {
        return type switch
        {
            ToastType.Success => (
                WpfColor.FromRgb(0x10, 0xb9, 0x81), // #10b981
                "ToastSuccessBgBrush",
                "ToastSuccessBorderBrush",
                WpfColor.FromArgb(0xf2, 0x0c, 0x1d, 0x1b), // Deep dark teal/green
                WpfColor.FromArgb(0x44, 0x10, 0xb9, 0x81),
                Geometry.Parse("M4,12 L9,17 L20,6") // Check
            ),
            ToastType.Error => (
                WpfColor.FromRgb(0xf4, 0x3f, 0x5e), // #f43f5e
                "ToastErrorBgBrush",
                "ToastErrorBorderBrush",
                WpfColor.FromArgb(0xf2, 0x24, 0x10, 0x15), // Deep dark rose/red
                WpfColor.FromArgb(0x55, 0xf4, 0x3f, 0x5e),
                Geometry.Parse("M12,2 L23,22 H1 Z M12,8 V14 M12,18 H12.1") // Alert
            ),
            ToastType.Warning => (
                WpfColor.FromRgb(0xf5, 0x9e, 0x0b), // #f59e0b
                "ToastWarningBgBrush",
                "ToastWarningBorderBrush",
                WpfColor.FromArgb(0xf2, 0x24, 0x1b, 0x0b), // Deep dark amber
                WpfColor.FromArgb(0x55, 0xf5, 0x9e, 0x0b),
                Geometry.Parse("M12,2 L23,22 H1 Z M12,8 V14 M12,18 H12.1") // Alert
            ),
            _ => (
                WpfColor.FromRgb(0x38, 0xbd, 0xf8), // #38bdf8
                "ToastInfoBgBrush",
                "ToastInfoBorderBrush",
                WpfColor.FromArgb(0xf2, 0x0c, 0x19, 0x29), // Deep dark cyan/blue
                WpfColor.FromArgb(0x44, 0x38, 0xbd, 0xf8),
                Geometry.Parse("M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,11 V17 M12,7 H12.01") // Info
            )
        };
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (IsDismissed) return;

        var elapsed = _stopwatch.ElapsedMilliseconds;
        var remaining = _totalDurationMs - elapsed;

        if (remaining <= 0)
        {
            Progress = 0.0;
            _countdownBarScale.ScaleX = 0.0;
            _timer.Stop();
            Dismiss(animated: true);
        }
        else
        {
            Progress = Math.Clamp((double)remaining / _totalDurationMs, 0.0, 1.0);
            _countdownBarScale.ScaleX = Progress;
        }
    }

    public void Pause()
    {
        if (IsPaused || IsDismissed) return;
        IsPaused = true;
        _stopwatch.Stop();
        _timer.Stop();
    }

    public void Resume()
    {
        if (!IsPaused || IsDismissed) return;
        IsPaused = false;
        _stopwatch.Start();
        _timer.Start();
    }

    private void ToastCard_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        Pause();
    }

    private void ToastCard_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging && !IsDismissed)
        {
            Resume();
        }
    }

    private void ToastCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        try
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            var pt = e.GetPosition(window);
            DependencyObject? hitVisual = null;
            VisualTreeHelper.HitTest(
                window,
                filter =>
                {
                    if (filter is FrameworkElement fe && (fe.Name == "ToastOverlayHost" || fe is ToastCard))
                        return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
                    return HitTestFilterBehavior.Continue;
                },
                result =>
                {
                    hitVisual = result.VisualHit;
                    return HitTestResultBehavior.Stop;
                },
                new PointHitTestParameters(pt));

            if (hitVisual is UIElement targetElement)
            {
                e.Handled = true;
                var forwardedArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = targetElement
                };
                targetElement.RaiseEvent(forwardedArgs);
            }
        }
        catch { }
    }

    private void ToastCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDismissed) return;
        _isDragging = true;
        // Detach any running or held animations on translation and opacity so manual drag applies directly
        _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        BeginAnimation(OpacityProperty, null);

        var root = Window.GetWindow(this) as IInputElement ?? Parent as IInputElement ?? this;
        _dragStartPoint = e.GetPosition(root);
        CaptureMouse();
        Pause();
        e.Handled = true;
    }

    private void ToastCard_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging || IsDismissed) return;

        var root = Window.GetWindow(this) as IInputElement ?? Parent as IInputElement ?? this;
        var currentPoint = e.GetPosition(root);
        var deltaX = currentPoint.X - _dragStartPoint.X;

        _translateTransform.X = deltaX;
        Opacity = Math.Clamp(1.0 - (Math.Abs(deltaX) / 220.0), 0.1, 1.0);
    }

    private void ToastCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        FinishDrag();
    }

    private void ToastCard_LostMouseCapture(object sender, WpfMouseEventArgs e)
    {
        if (_isDragging)
        {
            FinishDrag();
        }
    }

    private void FinishDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        if (IsDismissed) return;

        var deltaX = _translateTransform.X;
        if (Math.Abs(deltaX) >= 60.0)
        {
            // Swipe threshold reached: Fly-out
            DismissWithFlyout(deltaX > 0 ? 380.0 : -380.0);
        }
        else
        {
            // Snap back
            var animSnap = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var animRestore = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100));
            animSnap.Completed += (_, _) =>
            {
                _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _translateTransform.X = 0.0;
            };
            animRestore.Completed += (_, _) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
            };
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, animSnap);
            BeginAnimation(OpacityProperty, animRestore);

            if (!IsMouseOver)
            {
                Resume();
            }
        }
    }

    private void DismissWithFlyout(double targetX)
    {
        if (IsDismissed) return;
        IsDismissed = true;

        _timer.Stop();
        _stopwatch.Stop();

        var animFly = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var animFade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));
        animFly.Completed += (s, args) =>
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);
            Dismissed?.Invoke(this, EventArgs.Empty);
        };
        _translateTransform.BeginAnimation(TranslateTransform.XProperty, animFly);
        BeginAnimation(OpacityProperty, animFade);
    }

    public void SimulateSwipe(double deltaX, bool animated = false)
    {
        if (Math.Abs(deltaX) >= 60.0)
        {
            if (animated)
            {
                DismissWithFlyout(deltaX > 0 ? 380.0 : -380.0);
            }
            else
            {
                Dismiss(animated: false);
            }
        }
        else
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);
            _translateTransform.X = 0;
            Opacity = 1.0;
        }
    }

    public void UpdateRepeat(int newCount, int newDurationMs = 2000)
    {
        if (IsDismissed) return;

        Notification.RepeatCount = newCount;
        _repeatBadgeText.Text = $"x{newCount}";
        _repeatBadgeBorder.Visibility = Visibility.Visible;

        // Reset timer
        _totalDurationMs = Math.Max(200, newDurationMs);
        _stopwatch.Reset();
        Progress = 1.0;
        _countdownBarScale.ScaleX = 1.0;

        if (!IsPaused)
        {
            _stopwatch.Start();
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        // Tactile bounce pulse
        var bounce = new DoubleAnimationUsingKeyFrames();
        bounce.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        bounce.KeyFrames.Add(new LinearDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75))));
        bounce.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        bounce.Completed += (_, _) =>
        {
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
        };
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);

        // Subtle tactile vibration / shake ("rung nhẹ")
        var shake = new DoubleAnimationUsingKeyFrames();
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-3.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(35))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(3.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(115))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        shake.Completed += (_, _) =>
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _translateTransform.X = 0.0;
        };
        _translateTransform.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    public void AdjustDuration(int newDurationMs)
    {
        if (IsDismissed) return;
        _totalDurationMs = Math.Max(200, newDurationMs);
        if (_stopwatch.ElapsedMilliseconds >= _totalDurationMs)
        {
            Progress = 0.0;
            _countdownBarScale.ScaleX = 0.0;
            Dismiss(animated: true);
        }
    }

    public void Dismiss(bool animated = true)
    {
        if (IsDismissed) return;
        IsDismissed = true;

        _timer.Stop();
        _stopwatch.Stop();

        if (animated)
        {
            var animX = new DoubleAnimation(_translateTransform.X + 80.0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var animOp = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));
            animX.Completed += (s, e) =>
            {
                _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                BeginAnimation(OpacityProperty, null);
                Dismissed?.Invoke(this, EventArgs.Empty);
            };
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, animX);
            BeginAnimation(OpacityProperty, animOp);
        }
        else
        {
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, null);
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }
}
