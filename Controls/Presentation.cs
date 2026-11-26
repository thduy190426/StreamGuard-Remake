using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace StreamGuard.Controls;

/// <summary>Decorative vector data; control content, command parameters and tags stay intact.</summary>
public static class Icon
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null));
    public static Geometry? GetData(DependencyObject value) => (Geometry?)value.GetValue(DataProperty);
    public static void SetData(DependencyObject value, Geometry? data) => value.SetValue(DataProperty, data);
}

public sealed class SectionHeader : System.Windows.Controls.Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(SectionHeader));
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(nameof(IconBrush), typeof(Brush), typeof(SectionHeader), new PropertyMetadata(null));
    public static readonly DependencyProperty BadgeBackgroundProperty = DependencyProperty.Register(nameof(BadgeBackground), typeof(Brush), typeof(SectionHeader), new PropertyMetadata(null));
    public static readonly DependencyProperty BadgeBorderProperty = DependencyProperty.Register(nameof(BadgeBorder), typeof(Brush), typeof(SectionHeader), new PropertyMetadata(null));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public Brush? BadgeBackground { get => (Brush?)GetValue(BadgeBackgroundProperty); set => SetValue(BadgeBackgroundProperty, value); }
    public Brush? BadgeBorder { get => (Brush?)GetValue(BadgeBorderProperty); set => SetValue(BadgeBorderProperty, value); }
}

public static class NavTab
{
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.RegisterAttached(
        "AccentBrush", typeof(Brush), typeof(NavTab), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static Brush? GetAccentBrush(DependencyObject d) => (Brush?)d.GetValue(AccentBrushProperty);
    public static void SetAccentBrush(DependencyObject d, Brush? value) => d.SetValue(AccentBrushProperty, value);

    public static readonly DependencyProperty ActiveBackgroundBrushProperty = DependencyProperty.RegisterAttached(
        "ActiveBackgroundBrush", typeof(Brush), typeof(NavTab), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static Brush? GetActiveBackgroundBrush(DependencyObject d) => (Brush?)d.GetValue(ActiveBackgroundBrushProperty);
    public static void SetActiveBackgroundBrush(DependencyObject d, Brush? value) => d.SetValue(ActiveBackgroundBrushProperty, value);

    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(NavTab), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static Brush? GetIconBrush(DependencyObject d) => (Brush?)d.GetValue(IconBrushProperty);
    public static void SetIconBrush(DependencyObject d, Brush? value) => d.SetValue(IconBrushProperty, value);
}
