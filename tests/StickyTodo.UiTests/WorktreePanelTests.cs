using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyTodo;
using StickyTodo.Core;

internal static class WorktreePanelTests {
    internal sealed class FakeRunner : IWorktreeReportRunner {
        internal readonly List<ProcessStartInfo> calls = [];
        internal TaskCompletionSource<WorktreeRunResult>? pending;
        internal Exception? error;
        internal WorktreeRunResult result = new(0, "{}", "");
        internal Action? onRun;
        public Task<WorktreeRunResult> run(ProcessStartInfo startInfo, CancellationToken cancellationToken) {
            calls.Add(startInfo);
            onRun?.Invoke();
            if (error != null) { return Task.FromException<WorktreeRunResult>(error); }
            if (pending is not TaskCompletionSource<WorktreeRunResult> current) { return Task.FromResult(result); }
            cancellationToken.Register(() => current.TrySetCanceled(cancellationToken));
            return current.Task;
        }
    }

    private static string createReport(DateTimeOffset createdAt) => JsonSerializer.Serialize(new Dictionary<string, object?> {
        ["schemaVersion"] = 1, ["createdAt"] = createdAt.ToString("yyyy-MM-ddTHH:mm:sszzz"), ["deletionAuthorized"] = false,
        ["counts"] = new Dictionary<string, int> { ["broken"] = 1, ["active"] = 1, ["keep"] = 1, ["primary"] = 1 }, ["gaps"] = Array.Empty<object>(),
        ["worktrees"] = new object[] {
            new Dictionary<string, object?> { ["project"] = "Main", ["path"] = "C:/repo/Main", ["primary"] = true, ["status"] = "primary" },
            new Dictionary<string, object?> { ["project"] = "Keep", ["path"] = "C:/wt/keep", ["branch"] = "feature/keep", ["owner"] = "Claude/ui",
                ["sizeBytes"] = 1073741824L, ["idleDays"] = 30.0, ["warnings"] = Array.Empty<string>(), ["status"] = "keep" },
            new Dictionary<string, object?> { ["project"] = "Active", ["path"] = "C:/wt/active-with-a-very-long-directory-name-for-wrapping/Active",
                ["branch"] = "", ["head"] = "0123456789abcdef", ["detached"] = true, ["sizeBytes"] = 1826454690L, ["idleDays"] = 4.3,
                ["warnings"] = new[] { "unregistered", "outside-worktree-root" }, ["status"] = "active" },
            new Dictionary<string, object?> { ["project"] = "Broken", ["path"] = "C:/wt/broken", ["locked"] = "initializing", ["broken"] = true,
                ["warnings"] = new[] { "locked" }, ["sizeBytes"] = 0L, ["idleDays"] = null, ["status"] = "broken" }
        }
    });

    internal static void run(MainWindow main, FakeRunner runner, string root, string settingsDirectory) {
        var projects = field<ScrollViewer>(main, "todoScroller");
        var hideBox = field<CheckBox>(main, "hideBox");
        var expander = field<Expander>(main, "worktreesExpander");
        var heading = field<TextBlock>(main, "worktreeHeading");
        var counts = field<TextBlock>(main, "worktreeCounts");
        var alert = field<TextBlock>(main, "worktreeAlert");
        var regenerate = field<Button>(main, "regenerateWorktreesButton");
        var openFolder = field<Button>(main, "openWorktreeFolderButton");
        var rows = field<StackPanel>(main, "worktreeRows");
        var surface = field<Border>(main, "worktreeSurface");
        main.UpdateLayout();
        check(!expander.IsExpanded && heading.Text == "工作樹 · 未設定" && !regenerate.IsEnabled && !openFolder.IsEnabled
            && alert.Visibility == Visibility.Collapsed, "未設定狀態錯誤");
        var groups = field<StackPanel>(main, "groupPanel");
        check(surface.Parent is StackPanel content && projects.Content == content && content.Children.IndexOf(surface) == 0
            && content.Children.IndexOf(groups) == 1 && hideBox.TranslatePoint(new Point(), main).Y < surface.TranslatePoint(new Point(), main).Y,
            "工作樹區域不在專案清單最上方");

        var directory = Path.Combine(root, "worktree-panel");
        var reportDirectory = Path.Combine(directory, "報告");
        Directory.CreateDirectory(reportDirectory);
        var python = Path.Combine(directory, "python.exe"); File.WriteAllText(python, "");
        var script = Path.Combine(directory, "worktree_report.py"); File.WriteAllText(script, "");
        var profile = Path.Combine(directory, "profile.json");
        File.WriteAllText(profile, JsonSerializer.Serialize(new { worktrees = new { reportDir = reportDirectory },
            tools = new { python, worktreeReportScript = script } }));
        var reportPath = Path.Combine(reportDirectory, WorktreeToolConfig.REPORT_FILE_NAME);

        main.setWorktreeProfile(profile);
        check(heading.Text == "工作樹 · 尚無報告" && alert.Visibility == Visibility.Visible && alert.Text.Contains("找不到報告")
            && regenerate.IsEnabled && openFolder.IsEnabled, "缺少報告未提示：" + alert.Text);
        check(new SettingsStore(Path.Combine(settingsDirectory, "settings.json")).read().worktreeProfilePath == profile, "profile 未保存在本機設定");

        File.WriteAllText(reportPath, createReport(DateTimeOffset.Now.AddHours(-2)));
        main.loadWorktreeReport(false);
        check(heading.Text == "工作樹 · 3 個" && counts.Text == "有風險 0 · 可能已完成 0 · 損壞 1 · 使用中 1 · 保留 1"
            && alert.Visibility == Visibility.Collapsed, $"報告標題或計數錯誤：{heading.Text} / {counts.Text} / {alert.Text}");
        var rowIds = rows.Children.OfType<StackPanel>().Select(row => AutomationProperties.GetAutomationId(row)).ToList();
        check(rowIds.SequenceEqual(new[] { "WorktreeRow:C:/wt/broken", "WorktreeRow:C:/wt/active-with-a-very-long-directory-name-for-wrapping/Active",
            "WorktreeRow:C:/wt/keep" }), "列順序錯誤：" + string.Join(",", rowIds));
        var activeText = texts(rows.Children[1]);
        check(activeText.SequenceEqual(new[] { "Active · 使用中", "detached 0123456 · 負責人未登記",
            "C:/wt/active-with-a-very-long-directory-name-for-wrapping/Active", "閒置 4.3 天 · 1.70 GiB", "警示：unregistered, outside-worktree-root" }),
            "列內容錯誤：" + string.Join(" | ", activeText));
        check(texts(rows.Children[2]).Contains("feature/keep · 負責人：Claude/ui"), "負責人未顯示");
        check(texts(rows.Children[0]).Contains("閒置未知 · 大小未量測"), "損壞列大小顯示錯誤：" + string.Join(" | ", texts(rows.Children[0])));

        expander.IsExpanded = true;
        main.UpdateLayout();
        check(!new SettingsStore(Path.Combine(settingsDirectory, "settings.json")).read().isWorktreePanelCollapsed, "未記住展開狀態");
        var originalHeight = main.Height;
        main.Height = main.MinHeight;
        main.UpdateLayout();
        check(projects.ViewportHeight >= 30 && hideBox.TranslatePoint(new Point(), main).Y + hideBox.ActualHeight < main.ActualHeight,
            $"兩區展開時最小視窗擠掉專案清單：viewport={projects.ViewportHeight}");
        main.Height = 520;
        main.UpdateLayout();
        checkWheelForwarding(main, projects);
        main.Height = originalHeight;
        main.UpdateLayout();
        var screenshot = render(surface, Path.Combine(root, "worktree-panel.png"));

        // Time alone can make an unchanged report stale.
        main.worktreeClock = () => DateTimeOffset.Now.AddDays(9);
        main.loadWorktreeReport(false);
        check(alert.Visibility == Visibility.Visible && alert.Text.Contains("天未更新"), "檔案未變時未隨時間顯示過期：" + alert.Text);
        main.worktreeClock = () => DateTimeOffset.Now;
        main.loadWorktreeReport(false);
        check(alert.Visibility == Visibility.Collapsed, "時間恢復後過期警示未清除");

        File.WriteAllText(reportPath, createReport(DateTimeOffset.Now.AddDays(-10)) + "\n");
        main.loadWorktreeReport(false);
        check(alert.Visibility == Visibility.Visible && alert.Text.Contains("報告已 10 天未更新"), "過期未警示：" + alert.Text);

        File.WriteAllText(reportPath, "{\"worktrees\": [");
        main.loadWorktreeReport(false);
        check(heading.Text == "工作樹 · 無法讀取" && alert.Text.Contains("報告無法解析") && rows.Children.Count == 1, "損壞報告未安全提示");

        // Regenerate runs the configured tool in the background, disables the button and reloads afterwards.
        runner.pending = new();
        var task = main.regenerateWorktreeReport();
        var chooseProfile = field<Button>(main, "chooseWorktreeProfileButton");
        check(!regenerate.IsEnabled && (string)regenerate.Content == "產生中…" && !chooseProfile.IsEnabled, "產生中未停用按鈕");
        main.setWorktreeProfile(null);
        check(new SettingsStore(Path.Combine(settingsDirectory, "settings.json")).read().worktreeProfilePath == profile, "產生中仍可更換 profile");
        check(main.regenerateWorktreeReport().IsCompleted && !task.IsCompleted && runner.calls.Count == 1, "產生中重複啟動");
        var call = runner.calls.Single();
        check(call.FileName == python && call.ArgumentList.SequenceEqual(new[] { "-B", script, "report", "--profile", profile })
            && call.CreateNoWindow && !call.UseShellExecute, "產生報告命令錯誤：" + string.Join(" ", call.ArgumentList));
        File.WriteAllText(reportPath, createReport(DateTimeOffset.Now));
        runner.pending.SetResult(new(0, "{\"writeFailures\":[]}", ""));
        waitFor(() => task.IsCompleted, "產生報告未完成");
        check(regenerate.IsEnabled && chooseProfile.IsEnabled && (string)regenerate.Content == "重新產生報告" && heading.Text == "工作樹 · 3 個"
            && alert.Visibility == Visibility.Collapsed && field<TextBlock>(main, "status").Text.StartsWith("已重新產生工作樹報告", StringComparison.Ordinal),
            "產生後未重新載入：" + heading.Text + alert.Text);

        using (new FileStream(reportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            main.loadWorktreeReport();
            check(heading.Text == "工作樹 · 無法讀取" && alert.Text.Contains("報告無法讀取"), "報告被占用未提示：" + alert.Text);
        }
        main.loadWorktreeReport(false);
        check(heading.Text == "工作樹 · 3 個" && alert.Visibility == Visibility.Collapsed, "暫時無法讀取後，檔案未變就不再重試");

        runner.pending = null;
        runner.result = new(1, "{\"writeFailures\":[\"worktree-report.txt\"]}", "");
        main.regenerateWorktreeReport().GetAwaiter().GetResult();
        check(alert.Text.Contains("報告產生失敗（結束碼 1）：報告檔寫入失敗：worktree-report.txt") && regenerate.IsEnabled
            && heading.Text == "工作樹 · 3 個", "失敗結束碼未提示：" + alert.Text);
        runner.error = new System.ComponentModel.Win32Exception("找不到檔案");
        main.regenerateWorktreeReport().GetAwaiter().GetResult();
        check(alert.Text.Contains("報告產生失敗：找不到檔案") && regenerate.IsEnabled, "啟動失敗未提示：" + alert.Text);
        runner.error = null;
        runner.result = new(0, "{}", "");
        main.regenerateWorktreeReport().GetAwaiter().GetResult();
        check(alert.Visibility == Visibility.Collapsed, "成功後未清除失敗訊息：" + alert.Text);

        File.Delete(script);
        main.loadWorktreeReport(false);
        check(!regenerate.IsEnabled && (regenerate.ToolTip as string)!.StartsWith("找不到報告腳本", StringComparison.Ordinal)
            && heading.Text == "工作樹 · 3 個", "缺腳本時仍可產生或報告消失");

        File.WriteAllText(profile, "{bad");
        main.loadWorktreeReport(false);
        check(heading.Text == "工作樹 · 無法讀取" && alert.Text.Contains("profile 無法使用") && !regenerate.IsEnabled && !openFolder.IsEnabled,
            "profile 損壞未提示：" + alert.Text);

        main.setWorktreeProfile(null);
        check(heading.Text == "工作樹 · 未設定" && alert.Visibility == Visibility.Collapsed, "清除設定後狀態錯誤");
        check(File.Exists(reportPath) && Directory.GetFiles(reportDirectory).Length == 1, "面板改動了報告資料夾");
        expander.IsExpanded = false;
        check(runner.calls.Count == 4, "產生次數錯誤");
        Console.WriteLine("PASS 工作樹面板未設定、缺檔、載入排序與計數、過期與損壞警示、背景產生停用與重新載入、失敗訊息、profile 錯誤、收合保存與最小視窗");
        Console.WriteLine(screenshot);
    }

    private static void checkWheelForwarding(MainWindow main, ScrollViewer projects) {
        var inner = field<ScrollViewer>(main, "worktreeScroller");
        check(inner.ScrollableHeight > 0 && projects.ScrollableHeight > 0, $"滾輪測試前提不成立：inner={inner.ScrollableHeight}, outer={projects.ScrollableHeight}");
        void wheel(int delta) {
            var preview = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) {
                RoutedEvent = UIElement.PreviewMouseWheelEvent };
            inner.RaiseEvent(preview);
            if (!preview.Handled) {
                inner.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) {
                    RoutedEvent = UIElement.MouseWheelEvent });
            }
            main.UpdateLayout();
        }
        projects.ScrollToTop(); inner.ScrollToTop(); main.UpdateLayout();
        wheel(-120);
        check(inner.VerticalOffset > 0 && projects.VerticalOffset == 0, "工作樹清單中段滾輪未捲動內層");
        inner.ScrollToBottom(); main.UpdateLayout();
        wheel(-120);
        check(projects.VerticalOffset > 0, "工作樹清單到底後滾輪未交給專案清單");
        var outerOffset = projects.VerticalOffset;
        inner.ScrollToTop(); main.UpdateLayout();
        wheel(120);
        check(projects.VerticalOffset < outerOffset && inner.VerticalOffset == 0, "工作樹清單到頂後向上滾輪未交給專案清單");
        projects.ScrollToTop(); inner.ScrollToTop(); main.UpdateLayout();
    }

    private static string render(FrameworkElement surface, string path) {
        var originalTheme = Theme.currentColor;
        try {
            var images = new List<BitmapSource>();
            foreach (var theme in new[] { StickyTodo.Core.ThemePalette.DEFAULT_COLOR, "#2B2D33" }) {
                Theme.apply(theme);
                surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                images.Add(bitmap);
            }
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) {
                var x = 0.0;
                foreach (var image in images) { context.DrawImage(image, new Rect(x, 0, image.Width, image.Height)); x += image.Width + 12; }
            }
            var width = (int)Math.Ceiling(images.Sum(image => image.Width) + 12);
            var combined = new RenderTargetBitmap(width, (int)Math.Ceiling(images.Max(image => image.Height)), 96, 96, PixelFormats.Pbgra32);
            combined.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(combined));
            using (var stream = File.Create(path)) { encoder.Save(stream); }
            return path;
        } finally { Theme.apply(originalTheme); }
    }

    private static List<string> texts(UIElement row) => ((StackPanel)row).Children.OfType<TextBlock>()
        .Select(block => new System.Windows.Documents.TextRange(block.ContentStart, block.ContentEnd).Text).ToList();
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void waitFor(Func<bool> condition, string message) {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition()) {
            check(DateTime.UtcNow < deadline, message);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
