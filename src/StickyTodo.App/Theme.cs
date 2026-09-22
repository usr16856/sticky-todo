using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyTodo;

internal static class Theme {
    internal static readonly Brush paper = new SolidColorBrush(Color.FromRgb(255, 249, 218));
    internal static readonly Brush ink = new SolidColorBrush(Color.FromRgb(56, 49, 34));
    internal static readonly Brush muted = new SolidColorBrush(Color.FromRgb(116, 103, 74));
    internal static readonly Brush accent = new SolidColorBrush(Color.FromRgb(110, 86, 36));

    internal static void install(Application application) {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new System.Uri("/StickyTodo;component/Styles/Controls.xaml", System.UriKind.Relative)
        });
    }

    internal static TextBlock label(string text, double size = 14) => new() {
        Text = text, FontSize = size, Foreground = ink, TextWrapping = TextWrapping.Wrap
    };
}
