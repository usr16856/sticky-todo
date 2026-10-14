using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StickyTodo.Core;

namespace StickyTodo;

internal static class Theme {
    internal const string PAPER = "Paper";
    internal const string INK = "Ink";
    internal const string MUTED = "Muted";
    internal const string ACCENT = "Accent";
    internal const string ERROR = "ErrorText";
    internal const string CAPTION_HOVER = "CaptionHover";
    internal const string CAPTION_PRESSED = "CaptionPressed";

    internal static string currentColor { get; private set; } = ThemePalette.DEFAULT_COLOR;

    internal static void install(Application application, string? color = null) {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new System.Uri("/StickyTodo;component/Styles/Controls.xaml", System.UriKind.Relative)
        });
        apply(color ?? ThemePalette.DEFAULT_COLOR);
    }

    // Replacing application-level brushes updates every DynamicResource/SetResourceReference consumer, including open windows.
    internal static void apply(string color) {
        var palette = ThemePalette.from(color);
        currentColor = palette.paper.toHex();
        var resources = Application.Current.Resources;
        resources[PAPER] = brush(palette.paper);
        resources["InputSurface"] = brush(palette.inputSurface);
        resources["InputHover"] = brush(palette.inputHover);
        resources["HoverSurface"] = brush(palette.hoverSurface);
        resources["PressedSurface"] = brush(palette.pressedSurface);
        resources["ControlBorder"] = brush(palette.border);
        resources[INK] = brush(palette.ink);
        resources[MUTED] = brush(palette.muted);
        resources[ACCENT] = brush(palette.accent);
        resources["AccentHover"] = brush(palette.accentHover);
        resources["AccentPressed"] = brush(palette.accentPressed);
        resources["AccentForeground"] = brush(palette.accentForeground);
        resources["AccentFocus"] = brush(palette.accentFocus);
        resources[CAPTION_HOVER] = brush(palette.captionHover);
        resources[CAPTION_PRESSED] = brush(palette.captionPressed);
        resources[ERROR] = brush(palette.errorText);
    }

    internal static Color toColor(ThemeColor color) => Color.FromArgb(color.a, color.r, color.g, color.b);

    internal static void usePaper(Control control) {
        control.SetResourceReference(Control.BackgroundProperty, PAPER);
        control.SetResourceReference(Control.ForegroundProperty, INK);
    }

    internal static TextBlock label(string text, double size = 14) {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, INK);
        return label;
    }

    private static SolidColorBrush brush(ThemeColor color) {
        var result = new SolidColorBrush(toColor(color));
        result.Freeze();
        return result;
    }
}
