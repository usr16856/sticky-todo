using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using StickyTodo.Core;

namespace StickyTodo;

internal sealed class DeviceUsageWindow : Window {
    private readonly ComboBox stateSelector = new() { MinHeight = 32 };
    private readonly TextBox userInput = new() { MinHeight = 32 };
    private readonly TextBox purposeInput = new() { MinHeight = 32 };
    private readonly TextBlock errorLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock deviceName = Theme.label("", 17);
    private readonly TextBlock updatedLabel = Theme.label("", 12);
    private readonly Button reloadButton = new() { Content = "重新載入紀錄（保留草稿）", Visibility = Visibility.Collapsed };
    private readonly Func<DeviceUsage> getLatest;
    private readonly Action<DeviceUsage, DeviceUsage> saveUsage;
    private DeviceUsage baseline;
    private readonly DeviceUsage original;
    private bool isSaved;

    internal DeviceUsageWindow(DeviceUsage device, Func<DeviceUsage> getLatest, Action<DeviceUsage, DeviceUsage> saveUsage) {
        baseline = original = device;
        this.getLatest = getLatest;
        this.saveUsage = saveUsage;
        Title = "編輯測試機使用狀態";
        Width = 420;
        Height = 510;
        MinWidth = 350;
        MinHeight = 420;
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
        errorLabel.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        footer.Children.Add(new ScrollViewer { Content = errorLabel, MaxHeight = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        AutomationProperties.SetAutomationId(reloadButton, "ReloadDeviceUsage");
        reloadButton.Click += (_, _) => {
            try {
                baseline = getLatest();
                updateIdentity();
                reloadButton.Visibility = Visibility.Collapsed;
                errorLabel.Text = "已載入最新紀錄，草稿仍保留；按儲存將套用你的修改。";
            } catch (Exception exception) { errorLabel.Text = exception.Message; }
        };
        footer.Children.Add(reloadButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "儲存", MinWidth = 82 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += (_, _) => {
            try {
                saveUsage(baseline.changeUsage(selectedState, userInput.Text, purposeInput.Text), baseline);
                isSaved = true;
                DialogResult = true;
            } catch (DataConflictException) { notifyExternalChange(); }
            catch (Exception exception) { errorLabel.Text = "未儲存，草稿仍保留：" + exception.Message; }
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var fields = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        updateIdentity();
        fields.Children.Add(deviceName);
        fields.Children.Add(Theme.label("序號：" + device.serial, 12));
        fields.Children.Add(updatedLabel);
        foreach (var state in Enum.GetValues<DeviceUsageState>()) {
            var option = new ComboBoxItem { Content = DeviceUsage.getStateName(state), Tag = state };
            stateSelector.Items.Add(option);
            if (state == device.state) { stateSelector.SelectedItem = option; }
        }
        userInput.Text = device.user;
        purposeInput.Text = device.purpose;
        addField(fields, "使用狀態", stateSelector, "DeviceUsageState");
        addField(fields, "使用者（使用中必填）", userInput, "DeviceUsageUser");
        addField(fields, "用途（使用中必填）", purposeInput, "DeviceUsagePurpose");
        stateSelector.SelectionChanged += (_, _) => updateFields();
        updateFields();
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        frame.Children.Add(root);
        Content = frame;
        Closing += (_, args) => {
            var state = selectedState;
            var hasChanges = state != original.state || (state == DeviceUsageState.IN_USE
                && (userInput.Text != original.user || purposeInput.Text != original.purpose));
            if (!isSaved && hasChanges) { args.Cancel = new DiscardDraftConfirmationWindow(this).ShowDialog() != true; }
        };
    }

    internal void notifyExternalChange() {
        errorLabel.Text = "這台測試機的紀錄已在外部變更，草稿仍保留。請重新載入紀錄後再儲存。";
        reloadButton.Visibility = Visibility.Visible;
    }

    private void updateFields() {
        var isInUse = selectedState == DeviceUsageState.IN_USE;
        userInput.IsEnabled = purposeInput.IsEnabled = isInUse;
        if (!isInUse) { userInput.Text = purposeInput.Text = ""; }
    }

    private void updateIdentity() {
        deviceName.Text = baseline.name;
        updatedLabel.Text = "紀錄更新：" + baseline.updatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss zzz");
    }

    private static void addField(StackPanel panel, string name, Control input, string id) {
        var label = Theme.label(name, 13);
        label.Margin = new Thickness(0, 12, 0, 5);
        panel.Children.Add(label);
        AutomationProperties.SetAutomationId(input, id);
        AutomationProperties.SetName(input, name);
        panel.Children.Add(input);
    }

    private DeviceUsageState selectedState => (DeviceUsageState)((ComboBoxItem)stateSelector.SelectedItem).Tag;
}
