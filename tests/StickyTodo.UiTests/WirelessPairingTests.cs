using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyTodo;
using StickyTodo.Core;

internal static class WirelessPairingTests {
    private static readonly List<Exception> failures = [];
    internal sealed class FakeWirelessAdb : IWirelessAdb {
        internal IReadOnlyList<WirelessAdbDevice> found = [new("adb-phone", "192.168.1.10:37001")];
        internal Func<CancellationToken, Task<IReadOnlyList<WirelessAdbDevice>>>? discoverAction;
        internal Func<string, string, CancellationToken, Task>? pairAction;
        internal Func<string, string?, CancellationToken, Task<WirelessAdbConnectionResult>>? connectAction;
        internal WirelessAdbConnectionResult connectionResult = new(true, "已連線");
        internal int searches;
        internal int pairs;
        internal int connects;
        internal CancellationToken lastToken;
        internal string? lastPairAddress;
        internal string? lastCode;
        internal string? lastConnectionAddress;
        public Task<IReadOnlyList<WirelessAdbDevice>> discover(CancellationToken cancellationToken) {
            searches++; lastToken = cancellationToken;
            return discoverAction?.Invoke(cancellationToken) ?? Task.FromResult(found);
        }
        public Task pair(string address, string pairingCode, CancellationToken cancellationToken) {
            pairs++; lastToken = cancellationToken; lastPairAddress = address; lastCode = pairingCode;
            return pairAction?.Invoke(address, pairingCode, cancellationToken) ?? Task.CompletedTask;
        }
        public Task<WirelessAdbConnectionResult> connect(string pairingAddress, string? connectionAddress, CancellationToken cancellationToken) {
            connects++; lastToken = cancellationToken; lastConnectionAddress = connectionAddress;
            return connectAction?.Invoke(pairingAddress, connectionAddress, cancellationToken) ?? Task.FromResult(connectionResult);
        }
    }

    internal static void run() {
        failures.Clear();
        test("搜尋計時器保留選取、六位碼與焦點，服務消失時不改選其他裝置", () => {
            var fake = new FakeWirelessAdb { found = [new("a", "192.168.1.10:37001"), new("b", "192.168.1.11:37002")] };
            withWindow(fake, window => {
                var selector = input<ComboBox>(window, "WirelessPairingDevices");
                selector.SelectedItem = fake.found[1];
                var code = input<TextBox>(window, "WirelessPairingCode");
                code.Text = "012345";
                code.Focus();
                var focused = Keyboard.FocusedElement;
                var timer = field<DispatcherTimer>(window, "searchTimer");
                check(timer.Interval == TimeSpan.FromSeconds(5), "搜尋間隔不是五秒");
                fake.found = [fake.found[1], fake.found[0], new("c", "192.168.1.12:37003")];
                timer.Interval = TimeSpan.FromMilliseconds(30);
                waitFor(() => fake.searches >= 2, "計時器未搜尋");
                timer.Stop();
                check(((WirelessAdbDevice)selector.SelectedItem).serviceName == "b" && code.Text == "012345"
                    && ReferenceEquals(focused, Keyboard.FocusedElement), "背景搜尋打斷選取或輸入");
                fake.found = [fake.found[1]];
                click(window, "SearchWirelessDevices");
                check(selector.SelectedItem == null && text(window, "WirelessPairingSearchStatus").Contains("停止提供配對"), "服務消失後改選其他手機");
                fake.found = [new("new-phone", "192.168.1.20:37001")];
                click(window, "SearchWirelessDevices");
                check(selector.SelectedItem == null && code.Text == "012345", "後續搜尋偷偷選取另一台裝置並沿用舊配對碼");
                click(window, "PairWirelessDevice");
                check(fake.pairs == 0 && text(window, "WirelessPairingMessage").Contains("請選取"), "無選取仍配對");
            });
        });
        test("搜尋無結果或失敗時能手動配對；配對成功與連線成功分別顯示", () => {
            var fake = new FakeWirelessAdb { found = [], connectionResult = new(false, "請填入主畫面的連線位址") };
            var callbacks = 0;
            withWindow(fake, window => {
                check(text(window, "WirelessPairingSearchStatus").Contains("找不到"), "空清單沒有提示");
                fake.discoverAction = _ => throw new IOException("找不到 ADB");
                click(window, "SearchWirelessDevices");
                check(text(window, "WirelessPairingSearchStatus").Contains("搜尋失敗"), "搜尋失敗沒有提示");
                input<CheckBox>(window, "ManualWirelessPairing").IsChecked = true;
                input<TextBox>(window, "WirelessPairingAddress").Text = "192.168.1.10:37001";
                input<TextBox>(window, "WirelessPairingCode").Text = "012345";
                fake.pairAction = (_, _, _) => throw new FormatException("請輸入六位數配對碼");
                click(window, "PairWirelessDevice");
                check(fake.connects == 0 && text(window, "WirelessPairingMessage").Contains("六位數"), "配對驗證失敗後仍連線");
                fake.pairAction = null;
                click(window, "PairWirelessDevice");
                check(fake.lastPairAddress == "192.168.1.10:37001" && fake.lastCode == "012345", "手動配對參數錯誤");
                check(text(window, "WirelessPairingStatus") == "配對成功" && text(window, "WirelessConnectionStatus") == "尚未確認連線"
                    && callbacks == 0 && input<TextBox>(window, "WirelessPairingCode").Text == "", "混淆配對與連線，或配對碼未清除");
                input<TextBox>(window, "WirelessConnectionAddress").Text = "192.168.1.10:38001";
                checkInputFits(window, "WirelessConnectionAddress");
                checkActionFits(window, "ConnectWirelessDevice");
                capture(window, "wireless-pairing-manual-connection.png");
                check(field<StackPanel>(window, "pairingFields").Visibility == Visibility.Collapsed, "配對完成仍佔用配對輸入區域");
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                checkActionFits(window, "ConnectWirelessDevice");
                capture(window, "wireless-pairing-manual-connection-minimum.png");
                window.Width = 510; window.Height = 620;
                fake.connectAction = (_, _, _) => throw new IOException("連線失敗");
                click(window, "ConnectWirelessDevice");
                check(text(window, "WirelessPairingStatus") == "配對成功" && callbacks == 0, "連線失敗抹除配對成功");
                fake.connectAction = null; fake.connectionResult = new(true, "已連線");
                click(window, "ConnectWirelessDevice");
                check(fake.lastConnectionAddress == "192.168.1.10:38001" && fake.pairs == 2 && callbacks == 1
                    && text(window, "WirelessConnectionStatus") == "已連線", "手動連線重做配對、未刷新或未確認成功");
                render(window);
            }, () => { callbacks++; return Task.CompletedTask; });
        });
        test("配對與連線待完成期間禁止重複提交，關閉時不處理晚到結果", () => {
            var pairing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = new TaskCompletionSource<WirelessAdbConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeWirelessAdb { pairAction = (_, _, _) => pairing.Task, connectAction = (_, _, _) => connection.Task };
            var callbacks = 0;
            withWindow(fake, window => {
                input<TextBox>(window, "WirelessPairingCode").Text = "123456";
                click(window, "PairWirelessDevice"); click(window, "PairWirelessDevice");
                check(fake.pairs == 1 && !input<Button>(window, "PairWirelessDevice").IsEnabled, "重複送出配對");
                pairing.SetResult();
                waitFor(() => fake.connects == 1, "配對完成沒有連線");
                check(text(window, "WirelessPairingStatus") == "配對成功" && callbacks == 0, "連線尚未完成就回報成功");
                var closing = window.closeAndWait();
                check(fake.lastToken.IsCancellationRequested && !closing.IsCompleted && window.IsVisible, "未取消或未等待未完成作業");
                connection.SetResult(new(true, "已連線"));
                waitFor(() => closing.IsCompleted, "晚到連線結果後未完成關閉");
                check(callbacks == 0 && fake.pairs == 1 && !window.IsVisible, "關閉後仍更新主視窗或重送配對");
            }, () => { callbacks++; return Task.CompletedTask; });
        });
        test("關閉時取消並等待搜尋與配對的清理", () => {
            foreach (var phase in new[] { "search", "pair" }) {
                var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var fake = new FakeWirelessAdb();
                if (phase == "search") {
                    fake.discoverAction = async token => { await pending.Task; token.ThrowIfCancellationRequested(); return fake.found; };
                } else { fake.pairAction = async (_, _, token) => { await pending.Task; token.ThrowIfCancellationRequested(); }; }
                var window = new WirelessPairingWindow(fake, () => throw new Exception("關閉後不得回呼"));
                window.Show(); window.UpdateLayout();
                if (phase == "pair") {
                    input<TextBox>(window, "WirelessPairingCode").Text = "123456";
                    click(window, "PairWirelessDevice");
                }
                var closing = window.closeAndWait();
                check(fake.lastToken.IsCancellationRequested && !closing.IsCompleted, "未等待 " + phase + " 清理");
                pending.SetResult();
                waitFor(() => closing.IsCompleted, "未完成 " + phase + " 關閉");
                check(!window.IsVisible && fake.connects == 0, "關閉後仍顯示或連線");
            }
        });
        test("配對逾時不自動重送，後續連線失敗仍保留配對成功", () => {
            var fake = new FakeWirelessAdb { pairAction = (_, _, _) => Task.FromException(new OperationCanceledException()) };
            withWindow(fake, window => {
                input<TextBox>(window, "WirelessPairingCode").Text = "123456";
                click(window, "PairWirelessDevice");
                check(text(window, "WirelessPairingStatus") == "配對結果尚未確認" && fake.pairs == 1 && fake.connects == 0
                    && text(window, "WirelessPairingMessage").Contains("不會自動重送"), "逾時誤報或自動重試");
                fake.pairAction = null;
                fake.connectAction = (_, _, _) => throw new IOException("mDNS 不可用");
                input<TextBox>(window, "WirelessPairingCode").Text = "012345";
                click(window, "PairWirelessDevice");
                check(text(window, "WirelessPairingStatus") == "配對成功" && input<Button>(window, "ConnectWirelessDevice").IsEnabled,
                    "配對成功後搜尋連線位址失敗，無法使用手動備援");
            });
        });
        test("最小視窗、主題與手動備援畫面", () => {
            var originalTheme = Theme.currentColor;
            try {
                withWindow(new FakeWirelessAdb(), window => {
                    var selector = input<ComboBox>(window, "WirelessPairingDevices");
                    var displayName = ((WirelessAdbDevice)selector.SelectedItem).displayName;
                    check(descendants(selector).OfType<TextBlock>().Any(block => block.Text == displayName), "裝置選單顯示資料物件而非服務名稱及位址");
                    selector.IsDropDownOpen = true;
                    window.UpdateLayout();
                    var option = (ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(0);
                    check(descendants(option).OfType<TextBlock>().Any(block => block.Text == displayName), "下拉選項顯示資料物件內容");
                    selector.IsDropDownOpen = false;
                    input<CheckBox>(window, "ManualWirelessPairing").IsChecked = true;
                    input<TextBox>(window, "WirelessPairingAddress").Text = "192.168.1.10:37001";
                    input<TextBox>(window, "WirelessPairingCode").Text = "012345";
                    foreach (var color in new[] { "#F3E1F7", "#2B2D33" }) {
                        Theme.apply(color);
                        foreach (var minimum in new[] { false, true }) {
                            window.Width = minimum ? window.MinWidth : 510;
                            window.Height = minimum ? window.MinHeight : 620;
                            window.UpdateLayout();
                            var button = input<Button>(window, "PairWirelessDevice");
                            var root = (FrameworkElement)window.Content;
                            var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                            check(button.ActualHeight >= 28 && bounds.Bottom <= root.ActualHeight && bounds.Right <= root.ActualWidth,
                                "最小視窗裁切配對按鈕");
                            if (!minimum) {
                                foreach (var id in new[] { "WirelessPairingAddress", "WirelessPairingCode" }) {
                                    checkInputFits(window, id);
                                }
                            }
                            capture(window, "wireless-pairing-" + color[1..] + (minimum ? "-minimum" : "") + ".png");
                        }
                    }
                });
            } finally { Theme.apply(originalTheme); }
        });
        if (failures.Count > 0) { throw new AggregateException("無線配對 WPF 測試失敗", failures); }
    }

    internal static void runMainWindow(MainWindow main, DeviceUsageTests.FakeDiscovery discovery, FakeWirelessAdb wireless) {
        var scans = discovery.calls;
        var pending = new TaskCompletionSource<DeviceScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.pending = pending;
        var scan = main.refreshDevices();
        openMainWindow(main, window => {
            input<TextBox>(window, "WirelessPairingCode").Text = "012345";
            click(window, "PairWirelessDevice");
            check(wireless.pairs == 1 && text(window, "WirelessConnectionStatus") == "已連線", "主視窗未接上配對服務");
            discovery.pending = null;
            pending.SetResult(discovery.result);
            waitFor(() => scan.IsCompleted, "既有掃描結束後未完成配對後刷新");
            scan.GetAwaiter().GetResult();
            check(discovery.calls == scans + 2, "配對成功未排入掃描後的清單刷新");
        });
        Console.WriteLine("PASS 主視窗無線配對入口、服務注入與掃描重疊時刷新清單");
    }

    internal static void runExit(MainWindow main, FakeWirelessAdb wireless, DeviceUsageTests.FakeDiscovery discovery) {
        var scanCleanup = new TaskCompletionSource<DeviceScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.pending = scanCleanup;
        var scanCalls = discovery.calls;
        var scan = main.refreshDevices();
        // Queue a post-pairing refresh through the real callback before exiting during another operation.
        openMainWindow(main, window => {
            input<TextBox>(window, "WirelessPairingCode").Text = "012345";
            click(window, "PairWirelessDevice");
            check(text(window, "WirelessConnectionStatus") == "已連線", "未建立配對後排隊刷新情境");
        });
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wireless.pairAction = async (_, _, token) => { await pending.Task; token.ThrowIfCancellationRequested(); };
        openMainWindow(main, window => {
            input<TextBox>(window, "WirelessPairingCode").Text = "123456";
            click(window, "PairWirelessDevice");
            var exiting = (Task)typeof(MainWindow).GetMethod("exit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            check(wireless.lastToken.IsCancellationRequested && !exiting.IsCompleted, "App 結束未等待配對清理");
            pending.SetResult();
            waitFor(() => !window.IsVisible, "配對視窗未完成清理");
            try {
                check(discovery.lastToken.IsCancellationRequested && !exiting.IsCompleted && !scan.IsCompleted,
                    "App 結束未等待仍在清理的裝置/電量掃描");
            } finally { discovery.pending = null; scanCleanup.TrySetResult(discovery.result); }
            waitFor(() => exiting.IsCompleted, "App 結束等待配對未完成");
            exiting.GetAwaiter().GetResult();
            check(discovery.calls == scanCalls + 1, "退出時仍啟動排隊的裝置掃描");
        });
        Console.WriteLine("PASS App 結束取消並等待配對及裝置/電量掃描完成清理");
    }

    private static void openMainWindow(MainWindow main, Action<WirelessPairingWindow> action) {
        Exception? error = null;
        main.Dispatcher.BeginInvoke(() => {
            var window = Application.Current.Windows.OfType<WirelessPairingWindow>().Single();
            try { action(window); }
            catch (Exception exception) { error = exception; }
            finally { if (window.IsVisible) { window.Close(); } }
        });
        click(main, "OpenWirelessPairing");
        if (error != null) { throw new Exception("主視窗無線配對測試失敗", error); }
    }

    private static void withWindow(FakeWirelessAdb fake, Action<WirelessPairingWindow> action, Func<Task>? onConnected = null) {
        var window = new WirelessPairingWindow(fake, onConnected ?? (() => Task.CompletedTask));
        try { window.Show(); window.UpdateLayout(); action(window); }
        finally { if (window.IsVisible) { var closing = window.closeAndWait(); waitFor(() => closing.IsCompleted, "視窗未清理完成"); } }
    }
    private static void render(WirelessPairingWindow window) { window.UpdateLayout(); capture(window, "wireless-pairing-connected.png"); }
    private static void checkInputFits(Window window, string id) {
        var inputBox = input<TextBox>(window, id);
        var contentHost = (ScrollViewer)inputBox.Template.FindName("PART_ContentHost", inputBox);
        check(contentHost.ExtentHeight <= contentHost.ViewportHeight + 1,
            $"單行輸入文字被裁切：{id} extent={contentHost.ExtentHeight}, viewport={contentHost.ViewportHeight}");
    }
    private static void checkActionFits(Window window, string id) {
        var button = input<Button>(window, id);
        var root = (FrameworkElement)window.Content;
        var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
        check(button.ActualHeight >= 28 && bounds.Bottom <= root.ActualHeight && bounds.Right <= root.ActualWidth,
            "操作按鈕未完整顯示：" + id);
        for (var parent = VisualTreeHelper.GetParent(button); parent != null && parent != root; parent = VisualTreeHelper.GetParent(parent)) {
            if (parent is ScrollContentPresenter viewport) {
                var clippedBounds = button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
                check(clippedBounds.Top >= 0 && clippedBounds.Bottom <= viewport.ActualHeight,
                    "操作按鈕被捲動區域裁切：" + id);
            }
        }
    }
    private static void capture(Window window, string name) {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(AppContext.BaseDirectory, name);
        using (var stream = File.Create(path)) { encoder.Save(stream); }
        Console.WriteLine(path);
    }
    private static void test(string name, Action action) {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures.Add(exception); Console.WriteLine("FAIL " + name + "\n" + exception); }
    }
    private static string text(Window window, string id) => input<TextBlock>(window, id).Text;
    private static void click(Window window, string id) => input<Button>(window, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T input<T>(Window window, string id) where T : DependencyObject {
        window.UpdateLayout();
        return descendants(window).OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    }
    private static T field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
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
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
