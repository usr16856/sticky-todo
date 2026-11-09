using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StickyTodo;
using StickyTodo.Core;

internal static class DeviceBatteryTests {
    internal static void run(MainWindow main, TodoStore store, DeviceUsageTests.FakeDiscovery discovery, string evidenceDirectory) {
        var initialResult = discovery.result;
        var originalTheme = Theme.currentColor;
        var originalWidth = main.Width;
        var originalHeight = main.Height;
        var scroller = field<ScrollViewer>(main, "deviceScroller");
        var originalMaxHeight = scroller.MaxHeight;
        var bytes = File.ReadAllBytes(store.filePath);
        var backup = File.ReadAllBytes(store.filePath + ".bak");
        var devices = store.read().document.devices.ToList();
        try {
            setBattery(new(78, DeviceChargingState.CHARGING));
            var label = input<TextBlock>(main, "DeviceBattery:UI-A");
            var edit = input<Button>(main, "EditDevice:UI-A");
            check(label.Text == "電量 78% · 充電中", "主視窗沒有顯示電量與充電狀態");
            scroller.MaxHeight = 60;
            main.UpdateLayout();
            edit.Focus();
            scroller.ScrollToVerticalOffset(20);
            main.UpdateLayout();
            var offset = scroller.VerticalOffset;
            var focused = Keyboard.FocusedElement;
            check(offset > 0, "測試未建立可捲動的裝置清單");
            setBattery(new(56, DeviceChargingState.DISCHARGING));
            check(ReferenceEquals(label, input<TextBlock>(main, "DeviceBattery:UI-A")) && ReferenceEquals(edit, input<Button>(main, "EditDevice:UI-A"))
                && ReferenceEquals(focused, Keyboard.FocusedElement) && Math.Abs(offset - scroller.VerticalOffset) < 0.1,
                "電量更新重建控制項、打斷焦點或改變捲動");
            check(label.Text == "電量 56% · 使用電池", "相同裝置的電量變化未即時更新");
            setBattery(new(56, DeviceChargingState.FULL));
            check(label.Text == "電量 56% · 已充滿", "只有充電狀態變化時未更新");
            setBattery(null);
            check(label.Text == "電量未知" && field<HashSet<string>>(main, "connectedSerials").Contains("UI-A"), "電量失敗沿用舊值或誤判離線");
            scroller.MaxHeight = originalMaxHeight;

            setBattery(new(80, DeviceChargingState.NOT_CHARGING));
            open(main, input<Button>(main, "EditDevice:UI-A"), window => {
                var selector = input<ComboBox>(window, "DeviceUsageState");
                selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(option => (DeviceUsageState)option.Tag == DeviceUsageState.IN_USE);
                input<TextBox>(window, "DeviceUsagePurpose").Text = "電量更新期間保留的裝置草稿";
                setBattery(new(81, DeviceChargingState.CHARGING));
                check(input<TextBox>(window, "DeviceUsagePurpose").Text == "電量更新期間保留的裝置草稿"
                    && input<Button>(window, "ReloadDeviceUsage").Visibility == Visibility.Collapsed, "電量更新打斷裝置草稿或造成假衝突");
            });
            var todoButton = descendants(main).OfType<Button>().Single(button =>
                (button.ToolTip as string)?.StartsWith("編輯 A 第 1 項\n", StringComparison.Ordinal) == true);
            open(main, todoButton, window => {
                input<TextBox>(window, "TodoContent").Text = "電量更新期間保留的待辦草稿";
                setBattery(new(82, DeviceChargingState.CHARGING));
                check(input<TextBox>(window, "TodoContent").Text == "電量更新期間保留的待辦草稿"
                    && input<Button>(window, "DataConflictRecovery").Visibility == Visibility.Collapsed, "電量更新打斷待辦草稿或造成假衝突");
            });

            discovery.result = new([]);
            main.refreshDevices().GetAwaiter().GetResult();
            check(input<TextBlock>(main, "DeviceBattery:UI-A").Text == "電量未知", "離線仍顯示舊電量");
            setBattery(new(83, DeviceChargingState.CHARGING));
            check(input<TextBlock>(main, "DeviceBattery:UI-A").Text == "電量 83% · 充電中", "重新連線未恢復電量");
            discovery.error = new IOException("ADB unavailable");
            main.refreshDevices().GetAwaiter().GetResult();
            check(input<TextBlock>(main, "DeviceBattery:UI-A").Text == "電量未知", "整輪掃描失敗仍顯示舊電量");
            discovery.error = null;

            setBattery(new(78, DeviceChargingState.CHARGING));
            foreach (var color in new[] { "#F3E1F7", "#2B2D33" }) {
                Theme.apply(color);
                foreach (var minimum in new[] { false, true }) {
                    main.Width = minimum ? main.MinWidth : originalWidth;
                    main.Height = minimum ? main.MinHeight : originalHeight;
                    main.UpdateLayout();
                    var hide = field<CheckBox>(main, "hideBox");
                    var projects = field<ScrollViewer>(main, "todoScroller");
                    check(projects.ViewportHeight >= 30 && hide.TranslatePoint(new Point(), main).Y + hide.ActualHeight < main.ActualHeight,
                        "電量列在最小視窗擠掉專案清單或操作區域");
                    capture(main, Path.Combine(evidenceDirectory, "device-battery-" + color[1..] + (minimum ? "-minimum" : "") + ".png"));
                }
            }
            check(File.ReadAllBytes(store.filePath).SequenceEqual(bytes) && File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(backup)
                && store.read().document.devices.SequenceEqual(devices), "電量更新修改待辦、備份、使用狀態或更新時間");
            Console.WriteLine("PASS 電量與充電狀態即時更新、失敗/離線清除、焦點捲動與兩種草稿保留、資料備份不變及主題/最小視窗");
        } finally {
            discovery.error = null;
            discovery.result = initialResult;
            main.Width = originalWidth; main.Height = originalHeight;
            scroller.MaxHeight = originalMaxHeight;
            Theme.apply(originalTheme);
            main.refreshDevices().GetAwaiter().GetResult();
        }

        void setBattery(DeviceBatteryInfo? battery) {
            discovery.result = new(initialResult.devices.Select(device => device with { battery = battery }).ToList());
            main.refreshDevices().GetAwaiter().GetResult();
            main.UpdateLayout();
        }
    }

    private static void open(MainWindow main, Button button, Action<Window> action) {
        Exception? failure = null;
        main.Dispatcher.BeginInvoke(() => {
            var window = Application.Current.Windows.Cast<Window>().Single(window => window.Owner == main);
            try { action(window); }
            catch (Exception exception) { failure = exception; }
            finally {
                window.GetType().GetField("isSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                window.Close();
            }
        });
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure != null) { throw new Exception("電量更新草稿驗證失敗", failure); }
    }
    private static void capture(MainWindow window, string path) {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) { encoder.Save(stream); }
        Console.WriteLine(path);
    }
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T input<T>(DependencyObject root, string id) where T : DependencyObject => descendants(root).OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
