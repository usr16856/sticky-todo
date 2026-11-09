using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StickyTodo.Core;

namespace StickyTodo;

internal sealed class WirelessPairingWindow : Window {
    private readonly IWirelessAdb wirelessAdb;
    private readonly Func<Task> onConnected;
    private readonly CancellationTokenSource cancellation = new();
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ComboBox devices = new() { MinHeight = 32, MaxDropDownHeight = 180 };
    private readonly CheckBox manualInput = new() { Content = "手動輸入配對位址", Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBox pairingAddressInput = new() { Height = 38, Padding = new Thickness(10, 2, 10, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox codeInput = new() { Height = 38, Padding = new Thickness(10, 2, 10, 2), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 6 };
    private readonly TextBox connectionAddressInput = new() { Height = 38, Padding = new Thickness(10, 2, 10, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel manualPairingFields = new();
    private readonly StackPanel pairingFields = new();
    private readonly StackPanel connectionFields = new();
    private readonly TextBlock searchStatus = Theme.label("正在搜尋可配對裝置…", 12);
    private readonly TextBlock pairingStatus = Theme.label("尚未配對", 14);
    private readonly TextBlock pairedAddressLabel = Theme.label("", 12);
    private readonly TextBlock connectionStatus = Theme.label("尚未連線", 14);
    private readonly TextBlock message = Theme.label("", 12);
    private readonly Button searchButton = new() { Content = "重新搜尋" };
    private readonly Button pairButton = new() { Content = "配對並連線" };
    private readonly Button connectButton = new() { Content = "連線" };
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task searchTask = Task.CompletedTask;
    private Task operationTask = Task.CompletedTask;
    private string? pairedAddress;
    private bool isConnected;
    private bool isBusy;
    private bool isSearching;
    private bool hasSearched;
    private bool isClosing;
    private bool canClose;

    internal WirelessPairingWindow(IWirelessAdb wirelessAdb, Func<Task> onConnected) {
        this.wirelessAdb = wirelessAdb;
        this.onConnected = onConnected;
        Title = "無線偵錯配對";
        Width = 510; Height = 620; MinWidth = 380; MinHeight = 440;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        FontSize = 14;
        Theme.usePaper(this);
        var frame = new DockPanel();
        frame.SetResourceReference(Panel.BackgroundProperty, Theme.PAPER);
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var root = new DockPanel { Margin = new Thickness(18, 0, 18, 18) };
        var footer = new StackPanel();
        identify(message, "WirelessPairingMessage");
        footer.Children.Add(new ScrollViewer { Content = message, MaxHeight = 78, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var closeButton = new Button { Content = "關閉" };
        identify(closeButton, "CloseWirelessPairing");
        closeButton.Click += (_, _) => Close();
        pairButton.SetResourceReference(StyleProperty, "PrimaryButton");
        identify(pairButton, "PairWirelessDevice");
        identify(connectButton, "ConnectWirelessDevice");
        pairButton.Click += (_, _) => { if (!isBusy && pairedAddress == null && !isClosing) { operationTask = pairAndConnect(); } };
        connectButton.Click += (_, _) => { if (!isBusy && pairedAddress != null && !isConnected && !isClosing) { operationTask = connect(); } };
        actions.Children.Add(closeButton);
        actions.Children.Add(pairButton);
        actions.Children.Add(connectButton);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var fields = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        fields.Children.Add(pairingFields);
        pairingFields.Children.Add(Theme.label("電腦與手機／平板須在同一區網，裝置需支援 Android 11 以上的無線偵錯。", 13));
        pairingFields.Children.Add(Theme.label("在手機的「開發人員選項 → 無線偵錯」開啟「使用配對碼配對裝置」，並保持配對視窗開啟。", 13));
        addField(pairingFields, "可配對裝置", devices, "WirelessPairingDevices");
        searchStatus.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        identify(searchStatus, "WirelessPairingSearchStatus");
        pairingFields.Children.Add(searchStatus);
        searchButton.HorizontalAlignment = HorizontalAlignment.Left;
        identify(searchButton, "SearchWirelessDevices");
        searchButton.Click += (_, _) => beginSearch();
        devices.SelectionChanged += (_, _) => devices.ToolTip = (devices.SelectedItem as WirelessAdbDevice)?.displayName;
        pairingFields.Children.Add(searchButton);
        identify(manualInput, "ManualWirelessPairing");
        manualInput.Checked += (_, _) => updateControls();
        manualInput.Unchecked += (_, _) => updateControls();
        pairingFields.Children.Add(manualInput);
        pairingAddressInput.ToolTip = "手機配對視窗中的 IP:連接埠，例如 192.168.1.10:37001";
        addField(manualPairingFields, "配對位址（IP:連接埠）", pairingAddressInput, "WirelessPairingAddress");
        pairingFields.Children.Add(manualPairingFields);
        addField(pairingFields, "六位數配對碼", codeInput, "WirelessPairingCode");
        pairingStatus.Margin = new Thickness(0, 14, 0, 3);
        identify(pairingStatus, "WirelessPairingStatus");
        identify(connectionStatus, "WirelessConnectionStatus");
        fields.Children.Add(pairingStatus);
        pairedAddressLabel.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        pairedAddressLabel.Margin = new Thickness(0, 2, 0, 8);
        fields.Children.Add(pairedAddressLabel);
        fields.Children.Add(connectionStatus);
        connectionFields.Children.Add(Theme.label("請回到手機的無線偵錯主畫面，填入該處的 IP 與連接埠；它與配對連接埠不同。", 12));
        addField(connectionFields, "連線位址（IP:連接埠）", connectionAddressInput, "WirelessConnectionAddress");
        fields.Children.Add(connectionFields);
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        frame.Children.Add(root);
        Content = frame;
        searchTimer.Tick += (_, _) => beginSearch();
        Loaded += (_, _) => { searchTimer.Start(); beginSearch(); };
        Closing += onClosing;
        Closed += (_, _) => { cancellation.Dispose(); closed.TrySetResult(); };
        updateControls();
    }

    private void beginSearch() {
        if (isSearching || isBusy || isClosing || pairedAddress != null) { return; }
        searchTask = refreshSearch();
    }

    private async Task refreshSearch() {
        if (isSearching || isBusy || isClosing || pairedAddress != null) { return; }
        isSearching = true;
        updateControls();
        try {
            var found = await wirelessAdb.discover(cancellation.Token);
            if (isClosing || isBusy || pairedAddress != null) { return; }
            var previous = devices.SelectedItem as WirelessAdbDevice;
            if (!devices.Items.Cast<WirelessAdbDevice>().SequenceEqual(found)) {
                devices.ItemsSource = found;
                devices.SelectedItem = previous == null ? (!hasSearched && found.Count == 1 ? found[0] : null)
                    : found.FirstOrDefault(device => device == previous);
            }
            hasSearched = true;
            searchStatus.Text = previous != null && devices.SelectedItem == null ? "已選裝置已停止提供配對，請在手機重新開啟配對視窗。"
                : found.Count == 0 ? "找不到可配對裝置，可重新搜尋或手動輸入配對位址。" : $"找到 {found.Count} 台可配對裝置，每五秒更新。";
        } catch (OperationCanceledException) when (isClosing) { }
        catch (Exception exception) {
            if (!isClosing && !isBusy && pairedAddress == null) {
                // A failed search must not leave an old advertisement available for submission.
                devices.ItemsSource = Array.Empty<WirelessAdbDevice>();
                hasSearched = true;
                searchStatus.Text = exception is OperationCanceledException ? "搜尋逾時，可重新搜尋或手動輸入配對位址。"
                    : "搜尋失敗，可改用手動輸入：" + exception.Message;
            }
        } finally { isSearching = false; updateControls(); }
    }

    private async Task pairAndConnect() {
        var address = manualInput.IsChecked == true ? pairingAddressInput.Text.Trim() : (devices.SelectedItem as WirelessAdbDevice)?.address;
        if (string.IsNullOrEmpty(address)) { showMessage("請選取可配對裝置，或勾選手動輸入配對位址。", true); return; }
        var code = codeInput.Text.Trim();
        isBusy = true;
        showMessage("正在配對…");
        updateControls();
        try {
            await wirelessAdb.pair(address, code, cancellation.Token);
            if (isClosing) { return; }
            pairedAddress = address;
            pairingStatus.Text = "配對成功";
            codeInput.Clear();
            searchTimer.Stop();
            showMessage("配對成功，正在確認連線…");
            await connectTarget(null);
        } catch (OperationCanceledException) when (isClosing) { }
        catch (OperationCanceledException) {
            if (pairedAddress == null) {
                pairingStatus.Text = "配對結果尚未確認";
                codeInput.Clear();
                showMessage("配對逾時，結果尚未確認。請先在手機確認已配對的電腦，或重新開啟配對碼後再試；程式不會自動重送。", true);
            } else { showMessage("配對成功；確認連線逾時，可填入連線位址後重試。", true); }
        } catch (Exception exception) {
            if (!isClosing) { showMessage((pairedAddress == null ? "配對未完成：" : "配對成功；尚未確認連線：") + exception.Message, true); }
        } finally { isBusy = false; updateControls(); }
    }

    private async Task connect() {
        if (string.IsNullOrWhiteSpace(connectionAddressInput.Text)) { showMessage("請填入手機無線偵錯主畫面的連線位址。", true); return; }
        isBusy = true;
        showMessage("配對成功，正在連線…");
        updateControls();
        try { await connectTarget(connectionAddressInput.Text.Trim()); }
        catch (OperationCanceledException) when (isClosing) { }
        catch (Exception exception) {
            if (!isClosing) { showMessage(exception is OperationCanceledException ? "連線逾時，請確認手機的連線位址後重試。"
                : "配對成功；尚未確認連線：" + exception.Message, true); }
        } finally { isBusy = false; updateControls(); }
    }

    private async Task connectTarget(string? connectionAddress) {
        var result = await wirelessAdb.connect(pairedAddress!, connectionAddress, cancellation.Token);
        if (isClosing) { return; }
        isConnected = result.isConnected;
        connectionStatus.Text = isConnected ? "已連線" : "尚未確認連線";
        showMessage(result.message, !isConnected);
        if (isConnected) {
            try { await onConnected(); }
            catch (Exception exception) { if (!isClosing) { showMessage("已連線，但更新測試機清單失敗：" + exception.Message, true); } }
        }
    }

    private void updateControls() {
        var canPair = !isBusy && !isClosing && pairedAddress == null;
        pairingFields.Visibility = pairedAddress == null ? Visibility.Visible : Visibility.Collapsed;
        pairedAddressLabel.Visibility = pairedAddress == null ? Visibility.Collapsed : Visibility.Visible;
        pairedAddressLabel.Text = pairedAddress == null ? "" : "配對位址：" + pairedAddress;
        manualInput.IsEnabled = canPair;
        devices.IsEnabled = canPair && manualInput.IsChecked != true;
        pairingAddressInput.IsEnabled = codeInput.IsEnabled = canPair;
        manualPairingFields.Visibility = manualInput.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        searchButton.IsEnabled = canPair && !isSearching;
        pairButton.IsEnabled = canPair;
        pairButton.Visibility = pairedAddress == null ? Visibility.Visible : Visibility.Collapsed;
        connectionFields.Visibility = pairedAddress != null && !isConnected ? Visibility.Visible : Visibility.Collapsed;
        connectButton.Visibility = connectionFields.Visibility;
        connectionAddressInput.IsEnabled = connectButton.IsEnabled = !isBusy && !isClosing && pairedAddress != null && !isConnected;
    }

    internal Task closeAndWait() { Close(); return closed.Task; }

    private void onClosing(object? sender, CancelEventArgs args) {
        if (canClose) { return; }
        args.Cancel = true;
        if (isClosing) { return; }
        isClosing = true;
        searchTimer.Stop();
        cancellation.Cancel();
        codeInput.Clear();
        updateControls();
        if (searchTask.IsCompleted && operationTask.IsCompleted) {
            canClose = true;
            args.Cancel = false;
            return;
        }
        _ = finishClosing();
    }

    private async Task finishClosing() {
        // Never call Close recursively from the original Closing event, even if cancellation completed synchronously.
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.WhenAll(searchTask, operationTask);
        canClose = true;
        Close();
    }

    private void showMessage(string text, bool isError = false) {
        message.Text = text;
        message.SetResourceReference(TextBlock.ForegroundProperty, isError ? Theme.ERROR : Theme.INK);
    }

    private static void addField(StackPanel panel, string name, Control input, string id) {
        var label = Theme.label(name, 13);
        label.Margin = new Thickness(0, 12, 0, 5);
        panel.Children.Add(label);
        identify(input, id);
        AutomationProperties.SetName(input, name);
        panel.Children.Add(input);
    }

    private static void identify(DependencyObject target, string id) => AutomationProperties.SetAutomationId(target, id);
}
