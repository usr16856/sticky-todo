using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyTodo;

internal sealed class DeleteConfirmationWindow : Window {
    internal DeleteConfirmationWindow(Window owner, string project, string preview) {
        Owner = owner;
        Title = "刪除待辦";
        Width = 390;
        Height = 330;
        MinWidth = 340;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.paper;
        Foreground = Theme.ink;
        FontFamily = owner.FontFamily;
        FontSize = 14;
        Opacity = 1;
        var frame = new DockPanel();
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var body = new DockPanel { Margin = new Thickness(18, 8, 18, 18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 76 };
        cancel.Click += (_, _) => DialogResult = false;
        var delete = new Button { Content = "刪除", MinWidth = 76 };
        delete.SetResourceReference(StyleProperty, "DangerButton");
        delete.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(delete);
        DockPanel.SetDock(actions, Dock.Bottom);
        body.Children.Add(actions);
        var text = new StackPanel();
        var question = Theme.label("刪除這筆待辦？", 18);
        question.FontWeight = FontWeights.SemiBold;
        text.Children.Add(question);
        var projectLabel = Theme.label(project, 12);
        projectLabel.Foreground = Theme.muted;
        projectLabel.Margin = new Thickness(0, 12, 0, 6);
        text.Children.Add(projectLabel);
        text.Children.Add(Theme.label(preview));
        var hint = Theme.label("表單內尚未儲存的修改也會放棄。", 12);
        hint.Foreground = Theme.muted;
        hint.Margin = new Thickness(0, 14, 0, 0);
        text.Children.Add(hint);
        body.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        frame.Children.Add(body);
        Content = frame;
        Loaded += (_, _) => cancel.Focus();
    }
}
