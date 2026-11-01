using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BGInfoPlus;

internal static class UITheme
{
    public static readonly Brush WindowBrush = Brush("#F3F3F3");
    public static readonly Brush CardBrush = Brush("#FFFFFF");
    public static readonly Brush TextBrush = Brush("#1B1B1B");
    public static readonly Brush SecondaryTextBrush = Brush("#5D5D5D");
    public static readonly Brush BorderBrush = Brush("#E0E0E0");

    public static void Apply(Window window)
    {
        window.Background = WindowBrush; window.Foreground = TextBrush;
        window.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); window.FontSize = 14;
        window.Resources[typeof(Button)] = ButtonStyle(false);
    }

    public static Style AccentButtonStyle() => ButtonStyle(true);

    private static Style ButtonStyle(bool accent)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(accent ? "#0067C0" : "#FBFBFB")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, accent ? Brushes.White : TextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(accent ? "#005A9E" : "#C9C9C9")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 5, 12, 5)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome"; border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(FrameworkElement.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush(accent ? "#1975C5" : "#F0F0F0"), "Chrome")); template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Brush(accent ? "#005A9E" : "#E5E5E5"), "Chrome")); template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush;
    }
}
