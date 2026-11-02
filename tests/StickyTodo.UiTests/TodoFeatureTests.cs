using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StickyTodo;
using StickyTodo.Core;

internal static class TodoFeatureTests {
    private sealed record SavedEdit(IReadOnlyList<string> projects, string content, TodoPriority priority, bool sync);

    internal static void run() {
        testAdd();
        testSyncAndSingleEdit();
        testConflictRecovery();
        testPriorityDisplay();
        Console.WriteLine("PASS 優先等級新增、同步預設與範圍、單筆移動、等級草稿、衝突恢復、最小尺寸及即時主題顯示");
    }

    private static void testAdd() {
        SavedEdit? result = null;
        var editor = new EditorWindow(["A", "B"], "", false, () => ["A", "B", "其他"],
            (projects, text, priority, sync) => result = new(projects, text, priority, sync), () => { });
        var root = layout(editor);
        var selector = input<ComboBox>(root, "PrioritySelector");
        check((TodoPriority)((ComboBoxItem)selector.SelectedItem).Tag == TodoPriority.NORMAL, "新增未預設一般");
        check(((FrameworkElement)input<CheckBox>(root, "SyncMatchingContent").Parent).Visibility == Visibility.Collapsed, "新增顯示了同步選項");
        selectPriority(root, TodoPriority.HIGH);
        input<TextBox>(root, "TodoContent").Text = "新增\n多行內容";
        showAndSave(editor);
        check(result != null && result.projects.SequenceEqual(new[] { "A", "B" }) && result.priority == TodoPriority.HIGH
            && !result.sync && TodoDocument.normalizeLines(result.content) == "新增\n多行內容", "新增未傳遞所有專案、文字與等級");
    }

    private static void testSyncAndSingleEdit() {
        IReadOnlyList<TodoMatch> matches = [new("B", 0, false), new("B", 1, true), new("很長的歷史專案名稱用來驗證摘要換行", 0, true)];
        SavedEdit? result = null;
        var editor = new EditorWindow(["A"], "共同需求", true, () => ["A", "B", "其他"],
            (projects, text, priority, sync) => result = new(projects, text, priority, sync), () => { },
            priority: TodoPriority.HIGH, matchingItems: matches);
        var root = layout(editor);
        var syncCheck = input<CheckBox>(root, "SyncMatchingContent");
        var project = input<ComboBox>(root, "ProjectSelector");
        check(syncCheck.IsChecked == true && !project.IsEnabled && !editor.hasDraftChanges, "同步預設或初始草稿狀態錯誤");
        var summary = input<TextBlock>(root, "SyncMatchingSummary").Text;
        check(summary.Contains("B：2 筆（已完成 1）") && summary.Contains("很長的歷史專案名稱"), "同步摘要缺少筆數、完成數或專案");
        check(descendants(root).OfType<TextBlock>().Single(block => block.Text == "僅套用目前待辦").Visibility == Visibility.Visible,
            "同步等級範圍未說明");
        selectPriority(root, TodoPriority.LOW);
        check(editor.hasDraftChanges, "只改優先等級未保護草稿");
        layout(editor);
        assertLayout(root);
        render(root, "sync-editor-minimum.png", 350, 520);
        showAndSave(editor);
        check(result != null && result.sync && result.priority == TodoPriority.LOW && result.content == "共同需求"
            && result.projects.SequenceEqual(new[] { "A" }), "只改等級時同步或優先等級傳遞錯誤");

        result = null;
        var single = new EditorWindow(["A"], "共同需求", true, () => ["A", "B", "其他"],
            (projects, text, priority, sync) => result = new(projects, text, priority, sync), () => { }, matchingItems: matches);
        root = layout(single);
        syncCheck = input<CheckBox>(root, "SyncMatchingContent");
        project = input<ComboBox>(root, "ProjectSelector");
        syncCheck.IsChecked = false;
        check(project.IsEnabled, "取消同步後不能移動單筆");
        project.SelectedItem = "B";
        syncCheck.IsChecked = true;
        check((string)project.SelectedItem == "A" && !project.IsEnabled, "重啟同步未還原目前專案");
        syncCheck.IsChecked = false;
        project.SelectedItem = "B";
        input<TextBox>(root, "TodoContent").Text = "單筆修改";
        showAndSave(single);
        check(result != null && !result.sync && result.projects.SequenceEqual(new[] { "B" }) && result.content == "單筆修改",
            "取消同步未恢復單筆編輯與移動");
    }

    private static void testConflictRecovery() {
        SavedEdit? result = null;
        var recovered = false;
        var editor = new EditorWindow(["A"], "原文", true, () => ["A", "B", "其他"],
            (projects, text, priority, sync) => result = new(projects, text, priority, sync), () => recovered = true, () => { },
            matchingItems: [new("B", 0, true)]);
        var root = layout(editor);
        input<CheckBox>(root, "SyncMatchingContent").IsChecked = false;
        input<ComboBox>(root, "ProjectSelector").SelectedItem = "B";
        input<TextBox>(root, "TodoContent").Text = "保留的草稿";
        selectPriority(root, TodoPriority.LOW);
        editor.notifyExternalChange();
        layout(editor);
        check(!button(root, "刪除待辦").IsEnabled && !input<CheckBox>(root, "SyncMatchingContent").IsEnabled,
            "外部變更未停用刪除或同步切換");
        render(root, "priority-conflict-minimum.png", 350, 520);
        assertLayout(root);
        button(root, "保留草稿，重新載入並改為新增").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        layout(editor);
        check(recovered && input<TextBox>(root, "TodoContent").Text == "保留的草稿", "恢復沒有保留文字");
        check(descendants(root).OfType<ProjectMultiSelector>().Single().selectedProjects.SequenceEqual(new[] { "B" }), "恢復遺失專案草稿");
        check(((FrameworkElement)input<CheckBox>(root, "SyncMatchingContent").Parent).Visibility == Visibility.Collapsed, "恢復後仍有同步目標");
        showAndSave(editor);
        check(result != null && !result.sync && result.priority == TodoPriority.LOW && result.projects.SequenceEqual(new[] { "B" }),
            "恢復後優先等級、專案或新增範圍錯誤");
    }

    private static void testPriorityDisplay() {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.SetResourceReference(Panel.BackgroundProperty, Theme.PAPER);
        var high = MainWindow.createTodoText(new("高等級多行\n第二行", priority: TodoPriority.HIGH), 0);
        var normal = MainWindow.createTodoText(new("一般等級"), 1);
        var low = MainWindow.createTodoText(new("低等級", priority: TodoPriority.LOW), 2);
        var completed = MainWindow.createTodoText(new("已完成的高等級", true, TodoPriority.HIGH), 3);
        foreach (var label in new[] { high, normal, low, completed }) { panel.Children.Add(label); }
        var host = new Window { Content = panel };
        foreach (var color in ThemePalette.presets.Select(preset => preset.color).Concat(new[] { "#123456", "#767676", "#FFFFFF" })) {
            Theme.apply(color);
            var palette = ThemePalette.from(color);
            // Settings menus are recreated each time they open, including after a theme change.
            var legend = MainWindow.createPriorityMenu();
            var legendLabels = legend.Items.OfType<MenuItem>().Select(item => item.Header).OfType<TextBlock>().ToList();
            check(((SolidColorBrush)high.Foreground).Color == Theme.toColor(palette.highPriority), "高等級未即時更新");
            check(((SolidColorBrush)normal.Foreground).Color == Theme.toColor(palette.ink), "一般等級未沿用主題文字色");
            check(((SolidColorBrush)low.Foreground).Color == Theme.toColor(palette.lowPriority), "低等級未即時更新");
            check(((SolidColorBrush)completed.Foreground).Color == Theme.toColor(palette.muted)
                && completed.TextDecorations == TextDecorations.Strikethrough, "已完成項目未維持淡色刪除線");
            check(((SolidColorBrush)legendLabels[0].Foreground).Color == Theme.toColor(palette.highPriority)
                && ((SolidColorBrush)legendLabels[2].Foreground).Color == Theme.toColor(palette.lowPriority), "說明顏色與待辦不同步");
        }
        check(high.Text == "1. 高等級多行\n第二行" && high.ToolTip as string == "優先等級：高"
            && AutomationProperties.GetHelpText(high) == "優先等級：高", "優先等級改寫內容或缺少輔助說明");
        Theme.apply(ThemePalette.DEFAULT_COLOR);
        panel.Measure(new Size(400, 220)); panel.Arrange(new Rect(0, 0, 400, 220)); panel.UpdateLayout();
        render(panel, "priority-colors.png", 400, 220);
        host.Close();
    }

    private static FrameworkElement layout(EditorWindow editor) {
        var root = (FrameworkElement)editor.Content;
        root.Measure(new Size(350, 520)); root.Arrange(new Rect(0, 0, 350, 520)); root.UpdateLayout();
        return root;
    }

    private static void assertLayout(FrameworkElement root) {
        var saveBounds = button(root, "儲存").TransformToAncestor(root).TransformBounds(new Rect(button(root, "儲存").RenderSize));
        var content = input<TextBox>(root, "TodoContent");
        var contentBounds = content.TransformToAncestor(root).TransformBounds(new Rect(content.RenderSize));
        check(saveBounds.Bottom <= 520 && saveBounds.Right <= 350 && contentBounds.Height >= 100,
            $"最小視窗裁切儲存按鈕或內容：save={saveBounds}, content={contentBounds}");
        check(!saveBounds.IntersectsWith(contentBounds), "新增欄位造成內容與儲存按鈕重疊");
    }

    private static void selectPriority(DependencyObject root, TodoPriority priority) {
        var selector = input<ComboBox>(root, "PrioritySelector");
        selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(option => (TodoPriority)option.Tag == priority);
    }

    private static void showAndSave(EditorWindow editor) {
        editor.Loaded += (_, _) => editor.Dispatcher.BeginInvoke(() => button((FrameworkElement)editor.Content, "儲存")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        check(editor.ShowDialog() == true, "表單沒有成功儲存並關閉");
    }

    private static T input<T>(DependencyObject root, string id) where T : DependencyObject =>
        descendants(root).OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static Button button(DependencyObject root, string content) => descendants(root).OfType<Button>().Single(control => control.Content as string == content);

    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }

    private static void render(Visual root, string name, int width, int height) {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(AppContext.BaseDirectory, name);
        using (var stream = File.Create(path)) { encoder.Save(stream); }
        Console.WriteLine(path);
    }

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
