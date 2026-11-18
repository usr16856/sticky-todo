using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyTodo;
using StickyTodo.Core;

// Responsive main layout: narrow tabs show one section, wide windows show the list beside the side column.
internal static class MainLayoutTests {
    internal static void run(MainWindow main, TodoStore store, DeviceUsageTests.FakeDiscovery discovery, string settingsDirectory,
        string evidenceDirectory) {
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var originalTheme = Theme.currentColor;
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var tabBar = field<Grid>(main, "tabBar");
        var todoSection = field<DockPanel>(main, "todoSection");
        var deviceSurface = field<Border>(main, "deviceSurface");
        var worktreeSurface = field<Border>(main, "worktreeSurface");
        var deviceToggle = field<ToggleButton>(main, "deviceToggle");
        var worktreeToggle = field<ToggleButton>(main, "worktreeToggle");
        var deviceBody = field<DockPanel>(main, "deviceBody");
        var worktreeBody = field<DockPanel>(main, "worktreeBody");
        var projects = field<ScrollViewer>(main, "todoScroller");
        var todoTab = input<RadioButton>(main, "MainSection:" + MainSections.TODOS);
        var deviceTab = input<RadioButton>(main, "MainSection:" + MainSections.DEVICES);
        var worktreeTab = input<RadioButton>(main, "MainSection:" + MainSections.WORKTREES);
        var wideWidth = MainWindow.WIDE_LAYOUT_MIN_WIDTH + 140;
        var wideHeight = MainWindow.WIDE_LAYOUT_MIN_HEIGHT + 100;
        var originalDeviceOpen = deviceToggle.IsChecked == true;
        var originalWorktreeOpen = worktreeToggle.IsChecked == true;
        try {
            // Narrow: tabs, one section at a time, choice remembered.
            resize(main, 380, originalHeight);
            main.selectMainSection(MainSections.TODOS);
            main.UpdateLayout();
            check(tabBar.IsVisible && todoSection.IsVisible && !deviceSurface.IsVisible && !worktreeSurface.IsVisible
                && todoTab.IsChecked == true, "窄版選擇待辦後未只顯示待辦分頁");
            deviceTab.IsChecked = true;
            main.UpdateLayout();
            check(!todoSection.IsVisible && deviceSurface.IsVisible && !worktreeSurface.IsVisible && todoTab.IsChecked != true
                && new SettingsStore(settingsPath).read().mainSection == MainSections.DEVICES, "點測試機分頁未切換或未記住");
            check(Math.Abs(deviceSurface.TranslatePoint(new Point(), main).X - tabBar.TranslatePoint(new Point(), main).X) < 1
                && deviceSurface.ActualWidth >= tabBar.ActualWidth - 1, "測試機分頁沒有使用整個內容寬度");
            check(!deviceToggle.IsHitTestVisible && !deviceToggle.Focusable && !deviceToggle.IsEnabled, "窄版分頁仍可收合測試機區域");
            worktreeTab.IsChecked = true;
            main.UpdateLayout();
            check(!todoSection.IsVisible && !deviceSurface.IsVisible && worktreeSurface.IsVisible && worktreeBody.IsVisible,
                "工作樹分頁未顯示完整區域");

            // A hidden list keeps its scroll position through a data refresh that rebuilds its rows.
            todoTab.IsChecked = true;
            main.UpdateLayout();
            projects.ScrollToVerticalOffset(120);
            main.UpdateLayout();
            var hiddenOffset = projects.VerticalOffset;
            deviceTab.IsChecked = true;
            main.UpdateLayout();
            var beforeRefresh = store.read();
            var refreshed = beforeRefresh.document.clone();
            refreshed.add("Z05", "隱藏分頁期間的外部新增");
            store.saveTodoChanges(refreshed, beforeRefresh);
            poll(main);
            main.UpdateLayout();
            todoTab.IsChecked = true;
            main.UpdateLayout();
            check(hiddenOffset > 0 && Math.Abs(projects.VerticalOffset - hiddenOffset) < 1,
                $"隱藏分頁期間重建清單遺失捲動位置：{hiddenOffset} → {projects.VerticalOffset}");
            store.saveTodoChanges(beforeRefresh.document.clone(), store.read());
            poll(main);
            worktreeTab.IsChecked = true;
            main.UpdateLayout();

            // Wide: every section visible together; the list scrolls without moving the side column.
            resize(main, wideWidth, wideHeight);
            check(!tabBar.IsVisible && todoSection.IsVisible && deviceSurface.IsVisible && worktreeSurface.IsVisible,
                "寬版未同時顯示待辦、測試機與工作樹");
            var todoRight = todoSection.TranslatePoint(new Point(todoSection.ActualWidth, 0), main).X;
            var deviceLeft = deviceSurface.TranslatePoint(new Point(), main).X;
            check(deviceLeft > todoRight && Math.Abs(worktreeSurface.TranslatePoint(new Point(), main).X - deviceLeft) < 1
                && worktreeSurface.TranslatePoint(new Point(), main).Y > deviceSurface.TranslatePoint(new Point(), main).Y,
                $"寬版側欄位置錯誤：todoRight={todoRight}, deviceLeft={deviceLeft}");
            check(deviceToggle.IsHitTestVisible && deviceToggle.Focusable && deviceToggle.IsEnabled && worktreeToggle.IsHitTestVisible
                && worktreeToggle.IsEnabled, "寬版側欄無法收合");
            check(deviceSurface.ActualWidth >= 280 && todoSection.ActualWidth > deviceSurface.ActualWidth, "寬版欄寬比例錯誤");
            projects.ScrollToTop();
            main.UpdateLayout();
            var deviceTop = deviceSurface.TranslatePoint(new Point(), main).Y;
            projects.ScrollToVerticalOffset(120);
            main.UpdateLayout();
            check(projects.VerticalOffset > 0 && Math.Abs(deviceSurface.TranslatePoint(new Point(), main).Y - deviceTop) < 1,
                "側欄跟著專案清單捲動");
            projects.ScrollToTop();

            // Wide collapse is remembered and gives the space to the open panel; narrow tabs ignore it.
            deviceToggle.IsChecked = true;
            worktreeToggle.IsChecked = true;
            main.UpdateLayout();
            var bothOpenDeviceHeight = deviceSurface.ActualHeight;
            worktreeToggle.IsChecked = false;
            main.UpdateLayout();
            check(!worktreeBody.IsVisible && worktreeSurface.IsVisible && deviceSurface.ActualHeight > bothOpenDeviceHeight
                && new SettingsStore(settingsPath).read().isWorktreePanelCollapsed, "收合工作樹未讓出空間或未記住");
            deviceToggle.IsChecked = false;
            main.UpdateLayout();
            check(!deviceBody.IsVisible && new SettingsStore(settingsPath).read().isDevicePanelCollapsed, "收合測試機未生效或未記住");
            resize(main, 380, originalHeight);
            check(tabBar.IsVisible && worktreeTab.IsChecked == true && worktreeSurface.IsVisible && worktreeBody.IsVisible,
                "回到窄版未還原上次分頁，或寬版收合影響分頁");
            deviceTab.IsChecked = true;
            main.UpdateLayout();
            check(deviceBody.IsVisible, "寬版收合影響窄版測試機分頁");

            // Boundary: the switch happens exactly at the documented size, and the smallest wide window still fits
            // both side lists when they are open.
            resize(main, MainWindow.WIDE_LAYOUT_MIN_WIDTH - 1, wideHeight);
            check(tabBar.IsVisible, "臨界寬度以下仍為寬版");
            resize(main, MainWindow.WIDE_LAYOUT_MIN_WIDTH, MainWindow.WIDE_LAYOUT_MIN_HEIGHT - 1);
            check(tabBar.IsVisible, "臨界高度以下仍為寬版");
            resize(main, MainWindow.WIDE_LAYOUT_MIN_WIDTH, MainWindow.WIDE_LAYOUT_MIN_HEIGHT);
            check(!tabBar.IsVisible, "臨界尺寸未切成寬版");
            deviceToggle.IsChecked = true;
            worktreeToggle.IsChecked = true;
            main.UpdateLayout();
            var deviceList = field<ScrollViewer>(main, "deviceScroller");
            var worktreeList = field<ScrollViewer>(main, "worktreeScroller");
            Console.WriteLine(capture(main, Path.Combine(evidenceDirectory, "layout-wide-minimum.png")));
            check(deviceList.ViewportHeight >= 30 && worktreeList.ViewportHeight >= 30,
                $"最小寬版兩區展開時清單被擠掉：device={deviceList.ViewportHeight}, worktree={worktreeList.ViewportHeight}");
            deviceToggle.IsChecked = originalDeviceOpen;
            worktreeToggle.IsChecked = originalWorktreeOpen;

            // The device tab badge follows the in-use count without a rebuild of the tab.
            resize(main, 380, originalHeight);
            var badge = field<Border>(main, "deviceTabBadge");
            var badgeText = field<TextBlock>(main, "deviceTabBadgeText");
            var current = store.read();
            void checkBadge(int count, string message) => check(count == 0 ? badge.Visibility == Visibility.Collapsed
                : badge.Visibility == Visibility.Visible && badgeText.Text == count.ToString() && (deviceTab.ToolTip as string)!.Contains($"{count} 使用中"),
                message + $"：expected={count}, text={badgeText.Text}, visibility={badge.Visibility}");
            var target = current.document.devices.First();
            var inUse = current.document.devices.Count(device => device.state == DeviceUsageState.IN_USE);
            checkBadge(inUse, "測試機分頁徽章與使用中數量不符");
            var isTargetInUse = target.state == DeviceUsageState.IN_USE;
            var changed = isTargetInUse ? target with { state = DeviceUsageState.IDLE, user = "", purpose = "", updatedAt = DateTimeOffset.Now }
                : target with { state = DeviceUsageState.IN_USE, user = "版面測試", purpose = "徽章", updatedAt = DateTimeOffset.Now };
            store.saveDevice(changed, target);
            poll(main);
            checkBadge(isTargetInUse ? inUse - 1 : inUse + 1, "使用中數量變化未更新分頁徽章");
            var restored = store.read().document.devices.Single(device => device.serial == target.serial);
            store.saveDevice(target with { updatedAt = DateTimeOffset.Now }, restored);
            poll(main);
            checkBadge(inUse, "恢復後分頁徽章未更新");

            // ADB scan warnings are visible from any tab, and clear with the warning.
            var alert = field<TextBlock>(main, "deviceTabAlert");
            discovery.error = new IOException("ADB unavailable");
            main.refreshDevices().GetAwaiter().GetResult();
            check(alert.Visibility == Visibility.Visible && (deviceTab.ToolTip as string)!.Contains("ADB unavailable"), "掃描失敗未反映到測試機分頁");
            discovery.error = null;
            main.refreshDevices().GetAwaiter().GetResult();
            check(alert.Visibility == Visibility.Collapsed && !(deviceTab.ToolTip as string)!.Contains("ADB unavailable"), "掃描恢復後分頁警示未清除");

            // Minimum window: every tab keeps its own list usable and the tabs fit the width.
            resize(main, main.MinWidth, main.MinHeight);
            foreach (var (section, scrollerName) in new[] { (MainSections.TODOS, "todoScroller"), (MainSections.DEVICES, "deviceScroller"),
                (MainSections.WORKTREES, "worktreeScroller") }) {
                main.selectMainSection(section);
                main.UpdateLayout();
                var scroller = field<ScrollViewer>(main, scrollerName);
                Console.WriteLine(capture(main, Path.Combine(evidenceDirectory, $"layout-minimum-{section}.png")));
                check(scroller.IsVisible && scroller.ViewportHeight >= 30, $"最小視窗時 {section} 分頁清單被擠掉：{scroller.ViewportHeight}");
            }
            foreach (var tab in new[] { todoTab, deviceTab, worktreeTab }) {
                var right = tab.TranslatePoint(new Point(tab.ActualWidth, 0), main).X;
                check(tab.ActualHeight > 0 && right <= tabBar.TranslatePoint(new Point(tabBar.ActualWidth, 0), main).X + 0.5, "最小寬度時分頁超出視窗");
            }

            // Evidence for manual review in a light and a dark theme.
            var outputs = new List<string>();
            foreach (var color in new[] { "#F3E1F7", "#2B2D33" }) {
                Theme.apply(color);
                foreach (var section in new[] { MainSections.TODOS, MainSections.DEVICES, MainSections.WORKTREES }) {
                    resize(main, 380, 560);
                    main.selectMainSection(section);
                    main.UpdateLayout();
                    outputs.Add(capture(main, Path.Combine(evidenceDirectory, $"layout-narrow-{section}-{color[1..]}.png")));
                }
                resize(main, 960, 620);
                outputs.Add(capture(main, Path.Combine(evidenceDirectory, $"layout-wide-{color[1..]}.png")));
            }
            Console.WriteLine("PASS 主畫面窄版分頁切換與記住、隱藏分頁重建保留捲動、寬版雙欄同時顯示與獨立捲動、側欄收合記住且不影響分頁、臨界寬高與最小寬版、使用中徽章與掃描警示、最小視窗及主題截圖");
            foreach (var output in outputs) { Console.WriteLine(output); }
        } finally {
            discovery.error = null;
            deviceToggle.IsChecked = originalDeviceOpen;
            worktreeToggle.IsChecked = originalWorktreeOpen;
            Theme.apply(originalTheme);
            main.selectMainSection(MainSections.TODOS);
            resize(main, originalWidth, originalHeight);
        }
    }

    // A saved tab and a saved wide size are applied when the window is built, before any user action.
    internal static void runStartup() {
        foreach (var (section, width, height, isWide) in new[] { (MainSections.DEVICES, 400.0, 560.0, false),
            (MainSections.WORKTREES, 960.0, 640.0, true) }) {
            var root = Path.Combine(Path.GetTempPath(), "StickyTodo.UiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "projects", "A"));
            var store = new TodoStore(Path.Combine(root, "data", "TODO.md"));
            store.initialize();
            new SettingsStore(Path.Combine(root, "settings", "settings.json")).save(new AppSettings { mainSection = section, width = width, height = height });
            using var activation = new EventWaitHandle(false, EventResetMode.AutoReset);
            var window = new MainWindow(Path.Combine(root, "projects"), store.filePath, Path.Combine(root, "settings"), true, activation,
                new DeviceUsageTests.FakeDiscovery(), new WirelessPairingTests.FakeWirelessAdb(), new WorktreePanelTests.FakeRunner());
            try {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var tabBar = field<Grid>(window, "tabBar");
                var todoSection = field<DockPanel>(window, "todoSection");
                var deviceSurface = field<Border>(window, "deviceSurface");
                var worktreeSurface = field<Border>(window, "worktreeSurface");
                if (isWide) {
                    check(!tabBar.IsVisible && todoSection.IsVisible && deviceSurface.IsVisible && worktreeSurface.IsVisible, "保存的寬版尺寸啟動時未套用");
                } else {
                    check(tabBar.IsVisible && !todoSection.IsVisible && deviceSurface.IsVisible && !worktreeSurface.IsVisible
                        && field<RadioButton>(window, "deviceTab").IsChecked == true, "保存的分頁啟動時未套用");
                }
                check(new SettingsStore(Path.Combine(root, "settings", "settings.json")).read().mainSection == section, "啟動改寫了保存的分頁");
            } finally { closeWithoutShutdown(window); }
        }
        Console.WriteLine("PASS 啟動時套用保存的窄版分頁與寬版尺寸");
    }

    // Lowering the opacity fades the note but never the slider that controls it.
    internal static void runOpacity() {
        var root = Path.Combine(Path.GetTempPath(), "StickyTodo.UiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "projects", "A"));
        var store = new TodoStore(Path.Combine(root, "data", "TODO.md"));
        store.initialize();
        new SettingsStore(Path.Combine(root, "settings", "settings.json")).save(new AppSettings { windowOpacity = 0.3 });
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset);
        var window = new MainWindow(Path.Combine(root, "projects"), store.filePath, Path.Combine(root, "settings"), true, activation,
            new DeviceUsageTests.FakeDiscovery(), new WirelessPairingTests.FakeWirelessAdb(), new WorktreePanelTests.FakeRunner());
        try {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var slider = field<Slider>(window, "opacitySlider");
            var fadingLayer = field<DockPanel>(window, "fadingLayer");
            var slot = field<Border>(window, "opacitySliderSlot");
            static double effectiveOpacity(DependencyObject element) {
                var opacity = 1.0;
                for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) {
                    if (current is UIElement visual) { opacity *= visual.Opacity; }
                }
                return opacity;
            }
            check(window.Opacity == 1.0 && Math.Abs(fadingLayer.Opacity - 0.3) < 0.001, "保存的透明度未套用到便條紙內容");
            check(effectiveOpacity(slider) == 1.0, "透明度拖曳桿跟著變透明");
            Console.WriteLine(capture(window, Path.Combine(root, "opacity-30.png")));
            slider.Value = 55;
            check(Math.Abs(fadingLayer.Opacity - 0.55) < 0.001 && effectiveOpacity(slider) == 1.0, "調整後拖曳桿或內容透明度錯誤");
            foreach (var (width, height) in new[] { (400.0, 560.0), (960.0, 640.0) }) {
                resize(window, width, height);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var expected = slot.TranslatePoint(new Point(0, 0), window);
                var actual = slider.TranslatePoint(new Point(0, 0), window);
                check(Math.Abs(expected.X - actual.X) < 0.5 && Math.Abs(expected.Y - actual.Y) < 0.5, $"拖曳桿沒有對齊頁尾位置（{width}x{height}）");
            }
        } finally { closeWithoutShutdown(window); }
        Console.WriteLine("PASS 透明度只套用到便條紙內容，拖曳桿保持不透明並對齊頁尾");
    }

    // MainWindow.exit shuts the shared test Application down, so a secondary window is stopped and closed directly.
    private static void closeWithoutShutdown(MainWindow window) {
        typeof(MainWindow).GetField("isExiting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        foreach (var name in new[] { "pollTimer", "deviceTimer", "worktreeTimer", "settingsTimer" }) { field<DispatcherTimer>(window, name).Stop(); }
        field<CancellationTokenSource>(window, "deviceCancellation").Cancel();
        field<CancellationTokenSource>(window, "worktreeCancellation").Cancel();
        var tray = field<IDisposable>(window, "trayIcon");
        tray.GetType().GetProperty("Visible")!.SetValue(tray, false);
        tray.Dispose();
        window.Close();
    }

    private static void resize(MainWindow main, double width, double height) {
        main.Width = width;
        main.Height = height;
        main.UpdateLayout();
        main.UpdateLayout();
    }

    private static string capture(MainWindow window, string path) {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) { encoder.Save(stream); }
        return path;
    }

    private static void poll(MainWindow main) => typeof(MainWindow).GetMethod("poll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T input<T>(DependencyObject root, string id) where T : DependencyObject => descendants(root).OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
