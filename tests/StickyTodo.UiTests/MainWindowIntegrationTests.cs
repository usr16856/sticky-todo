using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StickyTodo;
using StickyTodo.Core;

internal static class MainWindowIntegrationTests {
    internal static void run() {
        var root = Path.Combine(Path.GetTempPath(), "StickyTodo.UiTests", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "projects");
        Directory.CreateDirectory(Path.Combine(projectRoot, "A"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "B"));
        var store = new TodoStore(Path.Combine(root, "data", "TODO.md"));
        store.initialize();
        var initial = store.read();
        initial.document.add("A", "共同需求\n第二行", TodoPriority.HIGH);
        initial.document.add("B", "共同需求\n第二行", TodoPriority.LOW);
        initial.document.groups[1].items.Add(new("共同需求\n第二行", true));
        store.save(initial.document, initial.version);
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        var discovery = new DeviceUsageTests.FakeDiscovery();
        var wireless = new WirelessPairingTests.FakeWirelessAdb();
        var main = new MainWindow(projectRoot, store.filePath, Path.Combine(root, "settings"), true, activationEvent, discovery, wireless);
        try {
            Directory.Move(projectRoot, projectRoot + ".offline");
            main.Show();
            main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            ProjectRefreshTests.run(main, projectRoot, store);
            DeviceUsageTests.run(main, store, discovery, Path.Combine(root, "settings"));
            DeviceBatteryTests.run(main, store, discovery, root);
            WirelessPairingTests.runMainWindow(main, discovery, wireless);
            edit(main, "A", editor => {
                var content = input<TextBox>(editor, "TodoContent");
                check(input<CheckBox>(editor, "SyncMatchingContent").IsChecked == true, "實際主視窗未帶入同步目標");
                content.Text = "同步更新\n第二行";
                selectPriority(editor, TodoPriority.NORMAL);
                click(editor, "儲存");
            });
            var saved = store.read();
            check(saved.document.groups[0].items[0] == new TodoItem("同步更新\n第二行"), "主視窗沒有保存目前項目等級");
            check(saved.document.groups[1].items.SequenceEqual(new TodoItem[] {
                new("同步更新\n第二行", false, TodoPriority.LOW), new("同步更新\n第二行", true)
            }), "主視窗同步未保留其他專案的等級或完成狀態");
            check(File.ReadAllText(store.filePath + ".bak").Contains("共同需求"), "主視窗同步未備份原版本");

            edit(main, "A", editor => {
                input<CheckBox>(editor, "SyncMatchingContent").IsChecked = false;
                input<TextBox>(editor, "TodoContent").Text = "只改本筆";
                selectPriority(editor, TodoPriority.HIGH);
                click(editor, "儲存");
            });
            saved = store.read();
            check(saved.document.groups[0].items[0] == new TodoItem("只改本筆", false, TodoPriority.HIGH)
                && saved.document.groups[1].items[0].content == "同步更新\n第二行", "主視窗單筆修改仍連動其他專案");

            // Restore equal text so the conflict also exercises a pending batch edit.
            edit(main, "A", editor => {
                input<TextBox>(editor, "TodoContent").Text = "同步更新\n第二行";
                click(editor, "儲存");
            });
            edit(main, "A", editor => {
                input<TextBox>(editor, "TodoContent").Text = "衝突後保留的草稿";
                selectPriority(editor, TodoPriority.LOW);
                var external = store.read();
                external.document.add("B", "外部新增");
                var externalText = MarkdownCodec.serialize(external.document);
                File.WriteAllText(store.filePath, externalText);
                typeof(MainWindow).GetMethod("poll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                click(editor, "儲存");
                check(editor.IsVisible && File.ReadAllText(store.filePath) == externalText, "外部變更未阻擋實際整批儲存");
                click(editor, "保留草稿，重新載入並改為新增");
                check(editor.Title == "新增待辦（保留的草稿）", "主視窗沒有切換新增草稿");
                click(editor, "儲存");
            });
            saved = store.read();
            check(saved.document.groups[0].items.SequenceEqual(new TodoItem[] {
                new("同步更新\n第二行", false, TodoPriority.HIGH), new("衝突後保留的草稿", false, TodoPriority.LOW)
            }), "衝突恢復覆蓋原項目或遺失草稿等級");
            check(saved.document.groups[1].items.Count == 3 && saved.document.groups[1].items[2].content == "外部新增"
                && saved.document.groups[1].items[0].content == "同步更新\n第二行", "恢復草稿仍同步或覆蓋外部資料");
            Console.WriteLine("PASS 實際主視窗同步存檔、獨立等級與完成狀態、單筆修改、備份、衝突阻擋及草稿另存");
            Console.WriteLine("Integration evidence: " + root);
            WirelessPairingTests.runExit(main, wireless, discovery);
        } finally {
            typeof(MainWindow).GetMethod("exit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        }
    }

    private static void edit(MainWindow main, string project, Action<EditorWindow> action) {
        main.UpdateLayout();
        main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var editButton = descendants(main).OfType<Button>().Single(button => (button.ToolTip as string)?.StartsWith($"編輯 {project} 第 1 項\n", StringComparison.Ordinal) == true);
        Exception? failure = null;
        main.Dispatcher.BeginInvoke(() => {
            var editor = Application.Current.Windows.OfType<EditorWindow>().Single(window => window.Owner == main);
            try { action(editor); }
            catch (Exception exception) {
                failure = exception;
                typeof(EditorWindow).GetField("isSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true);
                editor.Close();
            }
        });
        editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure != null) { throw new Exception("Main window edit failed", failure); }
    }

    private static void click(EditorWindow editor, string name) => descendants(editor).OfType<Button>()
        .Single(button => button.Content as string == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T input<T>(DependencyObject root, string id) where T : DependencyObject => descendants(root).OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static void selectPriority(EditorWindow editor, TodoPriority priority) {
        var selector = input<ComboBox>(editor, "PrioritySelector");
        selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(option => (TodoPriority)option.Tag == priority);
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
