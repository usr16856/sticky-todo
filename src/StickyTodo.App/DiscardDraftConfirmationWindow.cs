using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyTodo;

internal sealed class DiscardDraftConfirmationWindow : Window {
    internal DiscardDraftConfirmationWindow(Window owner) {
        Title = "保留草稿";
        Width = 390;
        Height = 270;
        MinWidth = 340;
        MinHeight = 250;
        if (owner.IsLoaded) {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        } else {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        ShowInTaskbar = false;
        Background = Theme.paper;
        Foreground = Theme.ink;
        FontFamily = owner.FontFamily;
        FontSize = 14;
        Opacity = 1;

        var frame = new DockPanel { Background = Theme.paper };
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);

        var body = new DockPanel { Margin = new Thickness(18, 8, 18, 18) };
        var actions = new StackPanel {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var keepEditing = new Button { Content = "繼續編輯", IsCancel = true, MinWidth = 88 };
        keepEditing.Click += (_, _) => DialogResult = false;
        var discard = new Button { Content = "放棄草稿", MinWidth = 88 };
        discard.SetResourceReference(StyleProperty, "DangerButton");
        discard.Click += (_, _) => DialogResult = true;
        actions.Children.Add(keepEditing);
        actions.Children.Add(discard);
        DockPanel.SetDock(actions, Dock.Bottom);
        body.Children.Add(actions);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var question = Theme.label("放棄尚未儲存的內容？", 18);
        question.FontWeight = FontWeights.SemiBold;
        text.Children.Add(question);
        var hint = Theme.label("關閉後，這次修改將無法復原。", 13);
        hint.Foreground = Theme.muted;
        hint.Margin = new Thickness(0, 12, 0, 0);
        text.Children.Add(hint);
        body.Children.Add(text);

        frame.Children.Add(body);
        Content = frame;
        Loaded += (_, _) => keepEditing.Focus();
    }
}
