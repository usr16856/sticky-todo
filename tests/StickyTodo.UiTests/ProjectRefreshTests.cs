using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyTodo;
using StickyTodo.Core;

internal static class ProjectRefreshTests {
    internal static void run(MainWindow main, string projectRoot, TodoStore store) {
        var data = File.ReadAllBytes(store.filePath);
        var backup = File.ReadAllBytes(store.filePath + ".bak");
        check(names(main).SequenceEqual(new[] { "A", "B", "其他" }) && warning(main).Visibility == Visibility.Visible,
            "首次掃描失敗未提供歷史分組或提示");
        Directory.Move(projectRoot + ".offline", projectRoot);
        waitFor(() => warning(main).Visibility == Visibility.Collapsed);
        var original = group(main, "A");
        poll(main);
        check(ReferenceEquals(original, group(main, "A")), "清單未變仍重建主畫面");

        Directory.CreateDirectory(Path.Combine(projectRoot, "Empty"));
        waitFor(() => names(main).Contains("Empty"));
        check(names(main).SequenceEqual(new[] { "A", "B", "Empty", "其他" }), "空專案排序錯誤");
        check(descendants(group(main, "Empty")).OfType<TextBlock>().Any(text => text.Text == "尚無待辦"), "空專案沒有提示");
        group(main, "B").IsExpanded = false;
        Directory.CreateDirectory(Path.Combine(projectRoot, "Temporary"));
        poll(main);
        check(!group(main, "B").IsExpanded, "刷新遺失收合狀態");
        Directory.Move(Path.Combine(projectRoot, "Temporary"), Path.Combine(projectRoot, "Renamed"));
        poll(main);
        check(names(main).Contains("Renamed") && !names(main).Contains("Temporary"), "重新命名未反映");
        Directory.Delete(Path.Combine(projectRoot, "Renamed"));
        Directory.Delete(Path.Combine(projectRoot, "A"));
        poll(main);
        check(!names(main).Contains("Renamed") && names(main).Contains("A"), "消失專案的歷史待辦未保留");

        // Real timer updates while the modal editor is open, with a multi-project draft.
        open(main, descendants(group(main, "Empty")).OfType<Button>().Single(), editor => {
            var multi = descendants(editor).OfType<ProjectMultiSelector>().Single();
            var content = input<TextBox>(editor, "TodoContent");
            check(multi.selectedProjects.SequenceEqual(new[] { "Empty" }), "空專案新增沒有預選");
            multi.setSelectedProjects(["A", "Empty"]);
            content.Text = "尚未儲存的草稿\n第二行";
            priority(editor).SelectedIndex = 2;
            Directory.CreateDirectory(Path.Combine(projectRoot, "Live"));
            waitFor(() => projects(editor).Contains("Live"));
            check(multi.selectedProjects.SequenceEqual(new[] { "A", "Empty" }), "自動刷新變更多選");
            check(TodoDocument.normalizeLines(content.Text) == "尚未儲存的草稿\n第二行", "自動刷新變更草稿內容");
            check(priority(editor).SelectedIndex == 2, "自動刷新變更優先等級");
            Directory.Delete(Path.Combine(projectRoot, "Empty"));
            poll(main);
            check(!names(main).Contains("Empty") && projects(editor).Contains("Empty")
                && multi.selectedProjects.Contains("Empty"), "選取中的消失專案遺失");
            multi.setSelectedProjects([]);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Zero"));
            poll(main);
            check(multi.selectedProjects.Count == 0, "自動刷新改變零選取狀態");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Manual"));
            click(editor, "重新整理");
            check(names(main).Contains("Manual") && projects(editor).Contains("Manual")
                && multi.selectedProjects.Count == 0, "手動刷新未同步主畫面或保留零選取");
            check(input<Button>(editor, "DataConflictRecovery").Visibility == Visibility.Collapsed,
                "目錄變化被誤判為待辦衝突");
        });

        Directory.Move(projectRoot, projectRoot + ".offline");
        var cached = names(main).ToArray();
        poll(main);
        check(names(main).SequenceEqual(cached) && warning(main).Visibility == Visibility.Visible,
            "掃描失敗清空快取或未提示");
        open(main, descendants(main).OfType<Button>().Single(button => button.Content as string == "＋ 新增待辦"), editor => {
            check(projects(editor).Contains("A") && projects(editor).Contains("Live"), "掃描失敗時無法使用快取清單");
        });
        Directory.Move(projectRoot + ".offline", projectRoot);
        waitFor(() => warning(main).Visibility == Visibility.Collapsed);
        check(File.ReadAllBytes(store.filePath).SequenceEqual(data) && File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(backup),
            "掃描或開啟草稿改寫待辦或備份");

        // Check scroll preservation with enough empty projects to exceed the viewport.
        for (var index = 0; index < 20; index++) { Directory.CreateDirectory(Path.Combine(projectRoot, $"Z{index:00}")); }
        poll(main);
        var scroller = field<ScrollViewer>(main, "todoScroller");
        main.UpdateLayout();
        scroller.ScrollToVerticalOffset(130);
        main.UpdateLayout();
        var offset = scroller.VerticalOffset;
        Directory.CreateDirectory(Path.Combine(projectRoot, "ZZ"));
        poll(main);
        main.UpdateLayout();
        check(offset > 0 && Math.Abs(scroller.VerticalOffset - offset) < 1, "專案刷新遺失捲動位置");

        // A historical project remains editable, including its pending matching-content sync.
        open(main, descendants(main).OfType<Button>().Single(button =>
            (button.ToolTip as string)?.StartsWith("編輯 A 第 1 項\n", StringComparison.Ordinal) == true), editor => {
            var sync = input<CheckBox>(editor, "SyncMatchingContent");
            check(sync.IsChecked == true, "歷史專案遺失同文同步");
            input<TextBox>(editor, "TodoContent").Text = "歷史專案草稿";
            Directory.CreateDirectory(Path.Combine(projectRoot, "DuringEdit"));
            poll(main);
            check(sync.IsChecked == true && !input<ComboBox>(editor, "ProjectSelector").IsEnabled
                && input<TextBox>(editor, "TodoContent").Text == "歷史專案草稿", "刷新遺失同步設定或編輯草稿");
            sync.IsChecked = false;
            Directory.CreateDirectory(Path.Combine(projectRoot, "SyncOff"));
            poll(main);
            check(sync.IsChecked == false && input<ComboBox>(editor, "ProjectSelector").IsEnabled, "刷新重新開啟同步");
        });

        Directory.CreateDirectory(Path.Combine(projectRoot, "Empty"));
        poll(main);
        open(main, descendants(group(main, "Empty")).OfType<Button>().Single(), editor => {
            input<TextBox>(editor, "TodoContent").Text = "空專案第一筆";
            click(editor, "儲存");
        });
        check(store.read().document.groups.Single(group => group.name == "Empty").items.Single().content == "空專案第一筆",
            "空專案新增未保存");
        check(!store.read().document.groups.Any(group => group.name == "Live"), "儲存時寫入其他空專案");

        scroller.ScrollToTop();
        main.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)main.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var imagePath = Path.Combine(Path.GetDirectoryName(store.filePath)!, "project-refresh.png");
        using (var stream = File.Create(imagePath)) { encoder.Save(stream); }
        Console.WriteLine("PASS 專案實際計時更新、空專案新增、歷史分組、改名刪除、編輯草稿/多選/零選取/同步保留、掃描失敗恢復、捲動收合及資料備份不變");
        Console.WriteLine(imagePath);
    }

    private static Expander group(MainWindow main, string name) => field<StackPanel>(main, "groupPanel").Children
        .OfType<Expander>().Single(group => ((TextBlock)group.Header).Text == name);
    private static IEnumerable<string> names(MainWindow main) => field<StackPanel>(main, "groupPanel").Children
        .OfType<Expander>().Select(group => ((TextBlock)group.Header).Text);
    private static TextBlock warning(MainWindow main) => field<TextBlock>(main, "projectWarning");
    private static IEnumerable<string> projects(EditorWindow editor) => input<ComboBox>(editor, "ProjectSelector").Items.Cast<string>();
    private static ComboBox priority(EditorWindow editor) => input<ComboBox>(editor, "PrioritySelector");
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T input<T>(DependencyObject root, string id) where T : DependencyObject => descendants(root).OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static void poll(MainWindow main) => typeof(MainWindow).GetMethod("poll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
    private static void click(EditorWindow editor, string name) => descendants(editor).OfType<Button>()
        .Single(button => button.Content as string == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void open(MainWindow main, Button button, Action<EditorWindow> action) {
        Exception? failure = null;
        main.Dispatcher.BeginInvoke(() => {
            var editor = Application.Current.Windows.OfType<EditorWindow>().Single(window => window.Owner == main);
            try { action(editor); }
            catch (Exception exception) { failure = exception; }
            finally {
                if (editor.IsVisible) {
                    typeof(EditorWindow).GetField("isSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true);
                    editor.Close();
                }
            }
        });
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure != null) { throw new Exception("Project refresh test failed", failure); }
    }

    private static void waitFor(Func<bool> condition) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) {
            check(DateTime.UtcNow < deadline, "實際計時器未在五秒內更新專案");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }

    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
