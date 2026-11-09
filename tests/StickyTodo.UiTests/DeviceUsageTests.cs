using System;
using System.IO;
using System.Linq;
using System.Reflection;
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

internal static class DeviceUsageTests {
    internal sealed class FakeDiscovery : IDeviceDiscovery {
        internal DeviceScanResult result = new([]);
        internal TaskCompletionSource<DeviceScanResult>? pending;
        internal Exception? error;
        internal int calls;
        internal CancellationToken lastToken;
        public Task<DeviceScanResult> scan(CancellationToken cancellationToken) {
            calls++;
            lastToken = cancellationToken;
            if (error != null) { return Task.FromException<DeviceScanResult>(error); }
            return pending?.Task ?? Task.FromResult(result);
        }
    }

    internal static void run(MainWindow main, TodoStore store, FakeDiscovery discovery, string settingsDirectory) {
        var initialTasks = store.read().document.clone();
        check(field<DispatcherTimer>(main, "deviceTimer").Interval == TimeSpan.FromSeconds(5), "背景掃描頻率錯誤");
        discovery.result = new([new("UI-A", "測試手機"), new("UI-B", "測試平板")]);
        waitFor(() => store.read().document.devices.Count == 2, "五秒計時器未自動登記真機");
        var devices = store.read().document.devices;
        check(devices.All(device => device.state == DeviceUsageState.UNMARKED), "自動登記猜測使用狀態");
        var bytes = File.ReadAllBytes(store.filePath);
        var backup = File.ReadAllBytes(store.filePath + ".bak");
        var editButton = input<Button>(main, "EditDevice:UI-A");
        main.refreshDevices().GetAwaiter().GetResult();
        check(ReferenceEquals(editButton, input<Button>(main, "EditDevice:UI-A")), "相同掃描結果重建控制項，可能打斷焦點");
        check(File.ReadAllBytes(store.filePath).SequenceEqual(bytes) && File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(backup), "定期掃描反覆寫檔");

        var panel = input<Expander>(main, "DeviceUsagePanel");
        main.UpdateLayout();
        var refreshText = descendants(field<Button>(main, "refreshDevicesButton")).OfType<TextBlock>().Single();
        var deviceTitle = descendants(field<StackPanel>(main, "deviceRows")).OfType<TextBlock>().First(text =>
            new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text.StartsWith("測試", StringComparison.Ordinal));
        check(Math.Abs(refreshText.TranslatePoint(new Point(), main).X - deviceTitle.TranslatePoint(new Point(), main).X) < 1,
            "重新整理文字沒有與裝置名稱左緣對齊");
        var hideBox = field<CheckBox>(main, "hideBox");
        var projects = field<ScrollViewer>(main, "todoScroller");
        var panelTop = panel.TranslatePoint(new Point(), main).Y;
        check(panelTop + panel.ActualHeight <= hideBox.TranslatePoint(new Point(), main).Y, "測試機區域不在隱藏已完成上方");
        projects.ScrollToVerticalOffset(120);
        main.UpdateLayout();
        check(projects.VerticalOffset > 0 && Math.Abs(panel.TranslatePoint(new Point(), main).Y - panelTop) < 1,
            "測試機區域跟著專案清單捲動");
        var originalHeight = main.Height;
        main.Height = main.MinHeight;
        main.UpdateLayout();
        check(projects.ViewportHeight >= 30 && hideBox.TranslatePoint(new Point(), main).Y + hideBox.ActualHeight < main.ActualHeight,
            $"最小視窗時固定區域擠掉專案清單或隱藏已完成：viewport={projects.ViewportHeight}, hideY={hideBox.TranslatePoint(new Point(), main).Y}, height={main.ActualHeight}");
        main.Height = originalHeight;
        projects.ScrollToTop();
        main.UpdateLayout();
        panel.IsExpanded = false;
        check(new SettingsStore(Path.Combine(settingsDirectory, "settings.json")).read().isDevicePanelCollapsed, "未記住收合狀態");
        panel.IsExpanded = true;
        renderRefreshStates(main, Path.GetDirectoryName(store.filePath)!);
        openDevice(main, "UI-A", editor => {
            choose(editor, DeviceUsageState.IN_USE);
            click(editor, "儲存");
            check(editor.IsVisible && descendants(editor).OfType<TextBlock>().Any(text => text.Text.Contains("填寫使用者與用途")), "未阻擋缺少使用資訊");
            input<TextBox>(editor, "DeviceUsageUser").Text = "Codex";
            input<TextBox>(editor, "DeviceUsagePurpose").Text = "介面驗證 | 測試";
            editor.Width = 350; editor.Height = 420; editor.UpdateLayout();
            var save = descendants(editor).OfType<Button>().Single(button => button.Content as string == "儲存");
            var bounds = save.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(save.RenderSize));
            check(bounds.Bottom <= 420 && bounds.Right <= 350, "最小尺寸裁切儲存按鈕");
            click(editor, "儲存");
        });
        check(store.read().document.devices.Single(device => device.serial == "UI-A") is { state: DeviceUsageState.IN_USE, user: "Codex" }, "介面沒有保存使用中狀態");
        discovery.result = new([]);
        main.refreshDevices().GetAwaiter().GetResult();
        check(field<StackPanel>(main, "deviceRows").Children.Count == 2
            && descendants(field<StackPanel>(main, "deviceRows")).OfType<TextBlock>().Any(text =>
                new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text.Contains("離線 · 使用中")), "離線未保留使用紀錄");
        discovery.error = new IOException("找不到 ADB");
        main.refreshDevices().GetAwaiter().GetResult();
        check(input<TextBlock>(main, "DeviceScanWarning").Visibility == Visibility.Visible
            && descendants(field<StackPanel>(main, "deviceRows")).OfType<TextBlock>().Any(text =>
                new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text.Contains("連線未知 · 使用中")), "掃描失敗誤判閒置或未提示");
        discovery.error = null;
        discovery.result = new([new("UI-A", "測試手機"), new("UI-B", "測試平板")]);
        main.refreshDevices().GetAwaiter().GetResult();
        check(input<TextBlock>(main, "DeviceScanWarning").Visibility == Visibility.Collapsed, "掃描恢復未清除提示");

        var calls = discovery.calls;
        discovery.pending = new();
        var firstScan = main.refreshDevices();
        var secondScan = main.refreshDevices();
        check(discovery.calls == calls + 1 && !firstScan.IsCompleted && secondScan.IsCompleted, "背景掃描重疊或阻塞");
        discovery.pending.SetResult(discovery.result);
        waitFor(() => firstScan.IsCompleted, "掃描完成未回到介面");
        firstScan.GetAwaiter().GetResult();
        discovery.pending = null;

        // A stale device editor must preserve its draft and respect another actor's update.
        openDevice(main, "UI-A", editor => {
            input<TextBox>(editor, "DeviceUsagePurpose").Text = "保留的裝置草稿";
            var external = store.read().document.devices.Single(device => device.serial == "UI-A");
            store.saveDevice(external.changeUsage(DeviceUsageState.IN_USE, "我", "人工測試"), external);
            poll(main);
            check(input<Button>(editor, "ReloadDeviceUsage").Visibility == Visibility.Visible, "外部更新未提示裝置衝突");
            click(editor, "儲存");
            check(editor.IsVisible && store.read().document.devices.Single(device => device.serial == "UI-A").user == "我", "裝置草稿覆蓋其他人更新");
            click(editor, "重新載入紀錄（保留草稿）");
            check(input<TextBox>(editor, "DeviceUsagePurpose").Text == "保留的裝置草稿", "重新載入遺失裝置草稿");
            click(editor, "儲存");
        });
        openDevice(main, "UI-B", editor => {
            choose(editor, DeviceUsageState.IN_USE);
            input<TextBox>(editor, "DeviceUsageUser").Text = "我";
            input<TextBox>(editor, "DeviceUsagePurpose").Text = "暫時用途";
            choose(editor, DeviceUsageState.IDLE);
            check(input<TextBox>(editor, "DeviceUsageUser").Text == "" && input<TextBox>(editor, "DeviceUsagePurpose").Text == "", "閒置未清空使用資訊");
            click(editor, "儲存");
        });

        // Device-only external edits must not invalidate an open task editor or get overwritten by it.
        var taskButton = descendants(main).OfType<Button>().Single(button =>
            (button.ToolTip as string)?.StartsWith("編輯 A 第 1 項\n", StringComparison.Ordinal) == true);
        open(main, taskButton, window => {
            var editor = (EditorWindow)window;
            input<TextBox>(editor, "TodoContent").Text = "裝置更新後仍可儲存的待辦草稿";
            input<CheckBox>(editor, "SyncMatchingContent").IsChecked = false;
            var external = store.read().document.devices.Single(device => device.serial == "UI-B");
            // Exercise the actual external Markdown reload path, bypassing the App writer.
            var changed = store.read().document;
            changed.devices[changed.devices.FindIndex(device => device.serial == "UI-B")] = external.changeUsage(DeviceUsageState.IN_USE, "人工", "外部 Markdown 更新");
            File.WriteAllText(store.filePath, MarkdownCodec.serialize(changed));
            poll(main);
            check(input<Button>(editor, "DataConflictRecovery").Visibility == Visibility.Collapsed, "裝置更新造成待辦假衝突");
            click(editor, "儲存");
        });
        var saved = store.read();
        check(saved.document.groups.Single(group => group.name == "A").items[0].content == "裝置更新後仍可儲存的待辦草稿"
            && saved.document.devices.Single(device => device.serial == "UI-B").user == "人工", "待辦保存遺失草稿或覆蓋裝置");
        store.saveTodoChanges(initialTasks, saved);
        poll(main);

        main.UpdateLayout();
        var imagePath = Path.Combine(Path.GetDirectoryName(store.filePath)!, "device-usage-panel.png");
        var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        field<ScrollViewer>(main, "todoScroller").ScrollToTop();
        main.UpdateLayout(); bitmap.Render((Visual)main.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(imagePath)) { encoder.Save(stream); }
        Console.WriteLine("PASS 測試機五秒背景登記、掃描不重疊、狀態表單與必填、離線及失敗恢復、收合保存、同台衝突、外部 Markdown 與待辦共同編輯");
        Console.WriteLine(imagePath);
    }

    private static void renderRefreshStates(MainWindow main, string outputDirectory) {
        var button = field<Button>(main, "refreshDevicesButton");
        var surface = field<Border>(main, "deviceSurface");
        var mouseKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var focusKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var pressedKey = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase)
            .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var originalTheme = Theme.currentColor;
        var originalMouse = button.IsMouseOver;
        var originalFocus = button.IsKeyboardFocused;
        try {
            foreach (var theme in new[] { "#F3E1F7", "#2B2D33" }) {
                Theme.apply(theme);
                main.UpdateLayout();
                var width = (int)Math.Ceiling(surface.ActualWidth);
                var height = (int)Math.Ceiling(surface.ActualHeight);
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen()) {
                    context.DrawRectangle((Brush)Application.Current.FindResource(Theme.PAPER), null, new Rect(0, 0, (width + 12) * 5, height + 32));
                    var names = new[] { "一般", "懸停", "按下", "鍵盤焦點", "停用" };
                    for (var index = 0; index < names.Length; index++) {
                        // Render the actual WPF style triggers without operating a physical device or moving the user's cursor.
                        button.SetValue(mouseKey, index is 1 or 2);
                        button.SetValue(pressedKey, index == 2);
                        button.SetValue(focusKey, index == 3);
                        button.IsEnabled = index != 4;
                        main.UpdateLayout();
                        var text = descendants(button).OfType<TextBlock>().Single();
                        var textBounds = text.TransformToAncestor(button).TransformBounds(new Rect(text.RenderSize));
                        check(textBounds.Left >= 12 && button.ActualWidth - textBounds.Right >= 12,
                            names[index] + "狀態的按鈕底色貼住文字");
                        var title = descendants(field<StackPanel>(main, "deviceRows")).OfType<TextBlock>().First(block =>
                            new System.Windows.Documents.TextRange(block.ContentStart, block.ContentEnd).Text.StartsWith("測試", StringComparison.Ordinal));
                        check(Math.Abs(text.TranslatePoint(new Point(), main).X - title.TranslatePoint(new Point(), main).X) < 1,
                            names[index] + "狀態的文字左緣未對齊");
                        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        var preview = new DrawingVisual();
                        using (var canvas = preview.RenderOpen()) {
                            canvas.DrawRectangle(new VisualBrush(surface), null, new Rect(0, 0, width, height));
                        }
                        bitmap.Render(preview);
                        context.DrawText(new FormattedText(names[index], System.Globalization.CultureInfo.GetCultureInfo("zh-TW"),
                            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, (Brush)Application.Current.FindResource(Theme.INK), 1),
                            new Point(index * (width + 12), 4));
                        context.DrawImage(bitmap, new Rect(index * (width + 12), 28, width, height));
                    }
                }
                var result = new RenderTargetBitmap((width + 12) * 5, height + 32, 96, 96, PixelFormats.Pbgra32);
                result.Render(drawing);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
                var path = Path.Combine(outputDirectory, "refresh-states-" + theme[1..] + ".png");
                using (var stream = File.Create(path)) { encoder.Save(stream); }
                Console.WriteLine(path);
            }
        } finally {
            button.SetValue(mouseKey, originalMouse);
            button.SetValue(pressedKey, false);
            button.SetValue(focusKey, originalFocus);
            button.IsEnabled = true;
            Theme.apply(originalTheme);
        }
    }

    private static void openDevice(MainWindow main, string serial, Action<DeviceUsageWindow> action) =>
        open(main, input<Button>(main, "EditDevice:" + serial), window => action((DeviceUsageWindow)window));

    private static void open(MainWindow main, Button button, Action<Window> action) {
        Exception? failure = null;
        main.Dispatcher.BeginInvoke(() => {
            var window = Application.Current.Windows.Cast<Window>().Single(window => window.Owner == main);
            try { action(window); }
            catch (Exception exception) { failure = exception; }
            finally {
                if (window.IsVisible) {
                    window.GetType().GetField("isSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    window.Close();
                }
            }
        });
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure != null) { throw new Exception("Device usage integration failed", failure); }
    }

    private static void choose(DeviceUsageWindow editor, DeviceUsageState state) {
        var selector = input<ComboBox>(editor, "DeviceUsageState");
        selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(item => (DeviceUsageState)item.Tag == state);
    }
    private static void click(Window window, string name) => descendants(window).OfType<Button>().Single(button => button.Content as string == name)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void poll(MainWindow main) => typeof(MainWindow).GetMethod("poll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T input<T>(DependencyObject root, string id) where T : DependencyObject => descendants(root).OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static System.Collections.Generic.IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }
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
