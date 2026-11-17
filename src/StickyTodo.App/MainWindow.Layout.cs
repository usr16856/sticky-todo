using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StickyTodo.Core;

namespace StickyTodo;

// Responsive main layout. A wide window shows the to-do list beside a side column with the device and worktree
// panels; a narrow window shows one section at a time behind tabs. Both layouts reuse the same section controls,
// so switching only changes visibility and grid sizes and never rebuilds rows, drafts or scroll positions.
internal sealed partial class MainWindow {
    // Window size at which the side column still has room for device and worktree rows next to the list;
    // below the height both stacked panels would squeeze their lists, so a short window keeps the tabs.
    internal const double WIDE_LAYOUT_MIN_WIDTH = 760;
    internal const double WIDE_LAYOUT_MIN_HEIGHT = 520;
    private const double COLUMN_GAP = 14;
    private const double SECTION_GAP = 10;
    private const double SIDE_COLUMN_MIN_WIDTH = 280;
    // Left inset that lines panel text up with the text of CompactButton actions: margin 2 + border 1 + padding 12.
    private const double SECTION_TEXT_INSET = 15;
    private readonly Grid contentGrid = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly DockPanel todoSection = new();
    private readonly Grid sideColumn = new();
    private readonly Grid tabBar = new() { Margin = new Thickness(0, 0, 0, 2) };
    private readonly RadioButton todoTab = new();
    private readonly RadioButton deviceTab = new();
    private readonly RadioButton worktreeTab = new();
    private readonly Border deviceTabBadge = new() { CornerRadius = new CornerRadius(8), MinWidth = 18, Padding = new Thickness(5, 0, 5, 1),
        Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock deviceTabBadgeText = new() { FontSize = 11, FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock deviceTabAlert = new() { Text = "⚠", FontSize = 12, Margin = new Thickness(5, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock worktreeTabBadge = new() { Text = "⚠", FontSize = 12, Margin = new Thickness(5, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private string deviceTabDescription = "測試機使用狀態";
    private bool isWideLayout;
    private bool isApplyingLayout;

    private void initializeTabBar() {
        for (var index = 0; index < 3; index++) { tabBar.ColumnDefinitions.Add(new ColumnDefinition()); }
        var baseline = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        baseline.SetResourceReference(Border.BackgroundProperty, "ControlBorder");
        Grid.SetColumnSpan(baseline, 3);
        tabBar.Children.Add(baseline);
        deviceTabBadge.SetResourceReference(Border.BackgroundProperty, Theme.ACCENT);
        deviceTabBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentForeground");
        deviceTabBadge.Child = deviceTabBadgeText;
        AutomationProperties.SetAutomationId(deviceTabBadge, "DeviceTabBadge");
        deviceTabAlert.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        AutomationProperties.SetAutomationId(deviceTabAlert, "DeviceTabAlert");
        worktreeTabBadge.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        AutomationProperties.SetAutomationId(worktreeTabBadge, "WorktreeTabBadge");
        addTab(todoTab, 0, "待辦", MainSections.TODOS);
        addTab(deviceTab, 1, "測試機", MainSections.DEVICES, deviceTabBadge, deviceTabAlert);
        addTab(worktreeTab, 2, "工作樹", MainSections.WORKTREES, worktreeTabBadge);
    }

    private void addTab(RadioButton tab, int column, string label, string section, params FrameworkElement[] badges) {
        tab.SetResourceReference(StyleProperty, "SectionTab");
        tab.GroupName = "MainSection";
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        // No explicit foreground: the label follows the tab style's selected/unselected colors.
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        foreach (var badge in badges) { content.Children.Add(badge); }
        tab.Content = content;
        AutomationProperties.SetName(tab, label);
        AutomationProperties.SetAutomationId(tab, "MainSection:" + section);
        tab.Checked += (_, _) => { if (!isApplyingLayout) { selectMainSection(section); } };
        Grid.SetColumn(tab, column);
        tabBar.Children.Add(tab);
    }

    private void initializeContentGrid() {
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(todoSection, 0);
        contentGrid.Children.Add(todoSection);
        sideColumn.RowDefinitions.Add(new RowDefinition());
        sideColumn.RowDefinitions.Add(new RowDefinition());
        sideColumn.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(deviceSurface, 0);
        sideColumn.Children.Add(deviceSurface);
        Grid.SetRow(worktreeSurface, 2);
        sideColumn.Children.Add(worktreeSurface);
        Grid.SetColumn(sideColumn, 2);
        contentGrid.Children.Add(sideColumn);
        isWideLayout = isWideSize(Width, Height);
        applyLayout();
    }

    private void updateLayoutMode() {
        var isWide = isWideSize(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        if (isWide == isWideLayout) { return; }
        isWideLayout = isWide;
        applyLayout();
    }

    private static bool isWideSize(double width, double height) => width >= WIDE_LAYOUT_MIN_WIDTH && height >= WIDE_LAYOUT_MIN_HEIGHT;

    internal void selectMainSection(string section) {
        settings.mainSection = MainSections.normalize(section);
        saveSettings();
        applyLayout();
    }

    private void setDevicePanelCollapsed(bool isCollapsed) {
        settings.isDevicePanelCollapsed = isCollapsed;
        saveSettings();
        applyLayout();
    }

    private void setWorktreePanelCollapsed(bool isCollapsed) {
        settings.isWorktreePanelCollapsed = isCollapsed;
        saveSettings();
        applyLayout();
    }

    private void applyLayout() {
        isApplyingLayout = true;
        try {
            var section = settings.mainSection;
            var showTodos = isWideLayout || section == MainSections.TODOS;
            var showDevices = isWideLayout || section == MainSections.DEVICES;
            var showWorktrees = isWideLayout || section == MainSections.WORKTREES;
            var showSide = showDevices || showWorktrees;
            tabBar.Visibility = toVisibility(!isWideLayout);
            todoTab.IsChecked = section == MainSections.TODOS;
            deviceTab.IsChecked = section == MainSections.DEVICES;
            worktreeTab.IsChecked = section == MainSections.WORKTREES;
            todoSection.Visibility = toVisibility(showTodos);
            sideColumn.Visibility = toVisibility(showSide);
            var columns = contentGrid.ColumnDefinitions;
            columns[0].Width = showTodos ? new GridLength(isWideLayout ? 3 : 1, GridUnitType.Star) : new GridLength(0);
            columns[1].Width = new GridLength(isWideLayout ? COLUMN_GAP : 0);
            columns[2].Width = showSide ? new GridLength(isWideLayout ? 2 : 1, GridUnitType.Star) : new GridLength(0);
            columns[2].MinWidth = isWideLayout ? SIDE_COLUMN_MIN_WIDTH : 0;
            // Collapsing only applies beside the list; a tab always shows its whole section.
            configureSection(deviceSurface, deviceToggle, deviceBody, sideColumn.RowDefinitions[0], showDevices,
                !isWideLayout || !settings.isDevicePanelCollapsed);
            configureSection(worktreeSurface, worktreeToggle, worktreeBody, sideColumn.RowDefinitions[2], showWorktrees,
                !isWideLayout || !settings.isWorktreePanelCollapsed);
            sideColumn.RowDefinitions[1].Height = new GridLength(showDevices && showWorktrees ? SECTION_GAP : 0);
        } finally { isApplyingLayout = false; }
    }

    private void configureSection(Border surface, ToggleButton toggle, FrameworkElement body, RowDefinition row, bool isShown, bool isOpen) {
        surface.Visibility = toVisibility(isShown);
        // Disabled as well so UI Automation cannot flip the wide-layout collapse from a tab.
        toggle.IsHitTestVisible = isWideLayout;
        toggle.Focusable = isWideLayout;
        toggle.IsEnabled = isWideLayout;
        body.Visibility = toVisibility(isOpen);
        row.Height = !isShown ? new GridLength(0) : isOpen ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
    }

    private void updateDeviceTabBadge(int inUseCount, string description) {
        deviceTabBadgeText.Text = inUseCount.ToString();
        deviceTabBadge.Visibility = toVisibility(inUseCount > 0);
        deviceTabDescription = description;
        updateDeviceTabAlert();
    }

    private void updateDeviceTabAlert() {
        var hasWarning = deviceWarning.Visibility == Visibility.Visible;
        var text = hasWarning ? deviceTabDescription + " · " + deviceWarning.Text : deviceTabDescription;
        deviceTabAlert.Visibility = toVisibility(hasWarning);
        deviceTab.ToolTip = text;
        AutomationProperties.SetHelpText(deviceTab, text);
    }

    private void updateWorktreeTabBadge(bool hasAlerts, string description) {
        var text = hasAlerts ? description + " · 有警示" : description;
        worktreeTabBadge.Visibility = toVisibility(hasAlerts);
        worktreeTab.ToolTip = text;
        AutomationProperties.SetHelpText(worktreeTab, text);
    }

    private static Visibility toVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
}
