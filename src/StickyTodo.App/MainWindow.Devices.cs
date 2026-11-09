using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using StickyTodo.Core;

namespace StickyTodo;

internal sealed partial class MainWindow {
    private readonly IDeviceDiscovery deviceDiscovery;
    private readonly IWirelessAdb wirelessAdb;
    private readonly CancellationTokenSource deviceCancellation = new();
    private readonly DispatcherTimer deviceTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Border deviceSurface = new() { Margin = new Thickness(0, 10, 0, 0), CornerRadius = new CornerRadius(6) };
    private readonly Expander devicesExpander = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock deviceHeading = Theme.label("測試機使用狀態", 15);
    private readonly TextBlock deviceWarning = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly StackPanel deviceRows = new();
    private readonly ScrollViewer deviceScroller = new() { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button refreshDevicesButton = new() { Content = "重新整理測試機" };
    private readonly Button pairWirelessButton = new() { Content = "無線配對" };
    private HashSet<string> connectedSerials = new(StringComparer.Ordinal);
    private bool isDeviceScanComplete;
    private bool isScanningDevices;
    private bool needsDeviceRefresh;
    private DeviceUsageWindow? deviceEditor;
    private WirelessPairingWindow? wirelessPairingWindow;
    private DeviceUsage? deviceEditorBaseline;

    private void initializeDevicePanel() {
        deviceSurface.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        deviceSurface.Child = devicesExpander;
        deviceHeading.FontWeight = FontWeights.SemiBold;
        devicesExpander.Header = deviceHeading;
        devicesExpander.IsExpanded = !settings.isDevicePanelCollapsed;
        AutomationProperties.SetAutomationId(devicesExpander, "DeviceUsagePanel");
        devicesExpander.Expanded += (_, _) => { settings.isDevicePanelCollapsed = false; saveSettings(); };
        devicesExpander.Collapsed += (_, _) => { settings.isDevicePanelCollapsed = true; saveSettings(); };
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        refreshDevicesButton.HorizontalAlignment = HorizontalAlignment.Left;
        refreshDevicesButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        refreshDevicesButton.Click += async (_, _) => await refreshDevices();
        var actions = new WrapPanel();
        actions.Children.Add(refreshDevicesButton);
        AutomationProperties.SetAutomationId(pairWirelessButton, "OpenWirelessPairing");
        pairWirelessButton.Click += (_, _) => openWirelessPairing();
        actions.Children.Add(pairWirelessButton);
        content.Children.Add(actions);
        // Match the standard button's text inset: margin 3 + border 1 + padding 12.
        deviceRows.Margin = new Thickness(16, 0, 0, 0);
        deviceWarning.Margin = new Thickness(16, 0, 0, 0);
        deviceWarning.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        AutomationProperties.SetAutomationId(deviceWarning, "DeviceScanWarning");
        content.Children.Add(deviceWarning);
        deviceScroller.Content = deviceRows;
        deviceScroller.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Application.Current.FindResource("MinimalVerticalScrollBar");
        content.Children.Add(deviceScroller);
        devicesExpander.Content = content;
        updateDeviceHeight();
        deviceTimer.Tick += async (_, _) => await refreshDevices();
    }

    private void updateDeviceHeight() => deviceScroller.MaxHeight = Math.Clamp(Height - 350, 32, 220);

    internal async Task refreshDevices() {
        if (isScanningDevices || isExiting || snapshot == null || hasDataError) { return; }
        isScanningDevices = true;
        refreshDevicesButton.IsEnabled = false;
        try {
            var result = await deviceDiscovery.scan(deviceCancellation.Token);
            if (isExiting) { return; }
            var connections = result.devices.Select(device => device.serial).ToHashSet(StringComparer.Ordinal);
            var hasConnectionChanges = !connectedSerials.SetEquals(connections) || isDeviceScanComplete != result.isComplete;
            connectedSerials = connections;
            isDeviceScanComplete = result.isComplete;
            deviceWarning.Text = result.warning ?? "";
            deviceWarning.Visibility = string.IsNullOrEmpty(result.warning) ? Visibility.Collapsed : Visibility.Visible;
            adoptSnapshot(store.registerDevices(result.devices), hasConnectionChanges);
        } catch (OperationCanceledException) when (isExiting) { }
        catch (Exception exception) {
            var hasConnectionChanges = connectedSerials.Count > 0 || isDeviceScanComplete;
            connectedSerials.Clear();
            isDeviceScanComplete = false;
            deviceWarning.Text = exception is OperationCanceledException ? "ADB 讀取逾時，稍後自動重試。"
                : "測試機資訊尚未更新，稍後自動重試：" + exception.Message;
            deviceWarning.Visibility = Visibility.Visible;
            if (hasConnectionChanges) { renderDevices(); }
        } finally {
            isScanningDevices = false;
            refreshDevicesButton.IsEnabled = true;
            if (needsDeviceRefresh && !isExiting) {
                needsDeviceRefresh = false;
                await refreshDevices();
            }
        }
    }

    private void openWirelessPairing() {
        if (wirelessPairingWindow != null || isExiting || isRequestingExit) { return; }
        wirelessPairingWindow = new WirelessPairingWindow(wirelessAdb, async () => {
            if (isScanningDevices) { needsDeviceRefresh = true; }
            else { await refreshDevices(); }
        }) { Owner = this };
        try { wirelessPairingWindow.ShowDialog(); }
        finally { wirelessPairingWindow = null; }
    }

    private void adoptSnapshot(TodoSnapshot latest, bool hasConnectionChanges = false) {
        var wasDataError = hasDataError;
        var hasSameTodos = snapshot?.document.hasSameTodos(latest.document) == true;
        var hasDeviceChanges = snapshot?.document.devices.SequenceEqual(latest.document.devices) != true;
        if (!hasSameTodos) { editor?.notifyExternalChange(); }
        if (deviceEditorBaseline != null && latest.document.devices.FirstOrDefault(device => device.serial == deviceEditorBaseline.serial)
            != deviceEditorBaseline) { deviceEditor?.notifyExternalChange(); }
        snapshot = latest;
        hasDataError = false;
        addButton.IsEnabled = true;
        reloadButton.Visibility = Visibility.Collapsed;
        if (hasSameTodos && !wasDataError) {
            if (hasDeviceChanges || hasConnectionChanges) { renderDevices(); }
        }
        else { render(); }
    }

    private void renderDevices() {
        var scrollOffset = deviceScroller.VerticalOffset;
        deviceRows.Children.Clear();
        var devices = snapshot?.document.devices ?? [];
        deviceHeading.Text = $"測試機使用狀態 · {devices.Count(device => device.state == DeviceUsageState.IN_USE)} 使用中 / {devices.Count} 台";
        if (devices.Count == 0) {
            deviceRows.Children.Add(Theme.label(isDeviceScanComplete ? "目前沒有已記錄的測試機。" : "等待讀取測試機資訊。", 12));
        }
        foreach (var device in devices.OrderBy(device => device.name, StringComparer.OrdinalIgnoreCase).ThenBy(device => device.serial, StringComparer.Ordinal)) {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
            var edit = new Button { Content = "編輯", IsEnabled = !hasDataError, VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "編輯測試機 " + device.serial };
            AutomationProperties.SetAutomationId(edit, "EditDevice:" + device.serial);
            edit.Click += (_, _) => openDeviceEditor(device.serial);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            var details = new StackPanel();
            var connection = connectedSerials.Contains(device.serial) ? "已連線" : isDeviceScanComplete ? "離線" : "連線未知";
            var title = Theme.label("", 13);
            title.Inlines.Add(new Run($"{device.name} · {connection} · "));
            var stateText = new Run(device.stateName) { FontWeight = FontWeights.SemiBold };
            stateText.SetResourceReference(TextElement.ForegroundProperty, Theme.getDeviceStateBrushKey(device.state));
            title.Inlines.Add(stateText);
            details.Children.Add(title);
            details.Children.Add(Theme.label(device.serial, 11));
            if (device.state == DeviceUsageState.IN_USE) { details.Children.Add(Theme.label($"{device.user}：{device.purpose}", 12)); }
            var updated = Theme.label("更新：" + device.updatedAt.ToLocalTime().ToString("MM/dd HH:mm:ss"), 11);
            updated.ToolTip = device.updatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss zzz");
            updated.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
            details.Children.Add(updated);
            row.Children.Add(details);
            deviceRows.Children.Add(row);
        }
        deviceScroller.UpdateLayout();
        deviceScroller.ScrollToVerticalOffset(scrollOffset);
    }

    private void openDeviceEditor(string serial) {
        if (snapshot == null || hasDataError || editor != null || deviceEditor != null) { return; }
        var device = snapshot.document.devices.Single(device => device.serial == serial);
        deviceEditorBaseline = device;
        deviceEditor = new DeviceUsageWindow(device, () => {
            var latest = store.read();
            var current = latest.document.devices.SingleOrDefault(device => device.serial == serial)
                ?? throw new InvalidOperationException("這台測試機的紀錄已被移除，請取消後重新整理。");
            deviceEditorBaseline = current;
            adoptSnapshot(latest);
            return current;
        }, (changed, expected) => {
            var latest = store.saveDevice(changed, expected);
            deviceEditorBaseline = changed;
            adoptSnapshot(latest);
            showStatus("已儲存測試機狀態 " + DateTime.Now.ToString("HH:mm"));
        }) { Owner = this };
        try { deviceEditor.ShowDialog(); }
        finally { deviceEditor = null; deviceEditorBaseline = null; }
    }
}
