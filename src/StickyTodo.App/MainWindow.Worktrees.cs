using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.Win32;
using StickyTodo.Core;

namespace StickyTodo;

// Read-only worktree overview. It only reads the agent-rules report and may re-run the read-only report tool;
// it never removes, prunes or registers worktrees.
internal sealed partial class MainWindow {
    private readonly IWorktreeReportRunner worktreeRunner;
    private readonly CancellationTokenSource worktreeCancellation = new();
    private Task worktreeRegenerateTask = Task.CompletedTask;
    private readonly DispatcherTimer worktreeTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Border worktreeSurface = new() { Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(6) };
    private readonly Expander worktreesExpander = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock worktreeHeading = Theme.label("工作樹", 15);
    private readonly TextBlock worktreeCounts = Theme.label("", 12);
    private readonly TextBlock worktreeAlert = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock worktreeInfo = Theme.label("", 11);
    private readonly StackPanel worktreeRows = new();
    private readonly ScrollViewer worktreeScroller = new() { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button regenerateWorktreesButton = new() { Content = "重新產生報告" };
    private readonly Button openWorktreeFolderButton = new() { Content = "開啟報告資料夾" };
    private readonly Button chooseWorktreeProfileButton = new() { Content = "選擇 profile…" };
    private WorktreeToolConfig? worktreeConfig;
    private WorktreeReportLoad worktreeLoad = new(WorktreeReportState.NOT_CONFIGURED, null, null);
    private WorktreeReportStamp? worktreeReportStamp;
    private string? worktreeRunMessage;
    private bool isRegeneratingWorktrees;
    internal Func<DateTimeOffset> worktreeClock = () => DateTimeOffset.Now;

    private sealed record WorktreeReportStamp(string path, DateTime writeTime, long length);

    private void initializeWorktreePanel() {
        worktreeSurface.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        worktreeSurface.Child = worktreesExpander;
        worktreeHeading.FontWeight = FontWeights.SemiBold;
        worktreeCounts.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        worktreeAlert.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        AutomationProperties.SetAutomationId(worktreeAlert, "WorktreeAlert");
        AutomationProperties.SetAutomationId(worktreeCounts, "WorktreeCounts");
        var header = new StackPanel();
        header.Children.Add(worktreeHeading);
        header.Children.Add(worktreeCounts);
        header.Children.Add(worktreeAlert);
        worktreesExpander.Header = header;
        worktreesExpander.IsExpanded = !settings.isWorktreePanelCollapsed;
        AutomationProperties.SetAutomationId(worktreesExpander, "WorktreePanel");
        worktreesExpander.Expanded += (_, _) => { settings.isWorktreePanelCollapsed = false; saveSettings(); };
        worktreesExpander.Collapsed += (_, _) => { settings.isWorktreePanelCollapsed = true; saveSettings(); };
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var actions = new WrapPanel();
        AutomationProperties.SetAutomationId(regenerateWorktreesButton, "RegenerateWorktreeReport");
        ToolTipService.SetShowOnDisabled(regenerateWorktreesButton, true);
        regenerateWorktreesButton.Click += async (_, _) => await regenerateWorktreeReport();
        actions.Children.Add(regenerateWorktreesButton);
        AutomationProperties.SetAutomationId(openWorktreeFolderButton, "OpenWorktreeReportFolder");
        ToolTipService.SetShowOnDisabled(openWorktreeFolderButton, true);
        openWorktreeFolderButton.Click += (_, _) => openWorktreeReportFolder();
        actions.Children.Add(openWorktreeFolderButton);
        AutomationProperties.SetAutomationId(chooseWorktreeProfileButton, "ChooseWorktreeProfile");
        ToolTipService.SetShowOnDisabled(chooseWorktreeProfileButton, true);
        chooseWorktreeProfileButton.Click += (_, _) => chooseWorktreeProfile();
        actions.Children.Add(chooseWorktreeProfileButton);
        content.Children.Add(actions);
        // Match the standard button's text inset like the device panel.
        worktreeInfo.Margin = new Thickness(16, 0, 0, 2);
        worktreeInfo.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        AutomationProperties.SetAutomationId(worktreeInfo, "WorktreeReportInfo");
        content.Children.Add(worktreeInfo);
        worktreeRows.Margin = new Thickness(16, 0, 0, 0);
        worktreeScroller.Content = worktreeRows;
        worktreeScroller.PreviewMouseWheel += forwardWorktreeWheel;
        worktreeScroller.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Application.Current.FindResource("MinimalVerticalScrollBar");
        content.Children.Add(worktreeScroller);
        worktreesExpander.Content = content;
        updateWorktreeHeight();
        worktreeTimer.Tick += (_, _) => loadWorktreeReport(false);
        renderWorktrees();
    }

    // The rows scroll inside the panel so an expanded report does not push every project far down the list;
    // the panel itself sits in the project scroller, so keep it below the list viewport at the default size.
    private void updateWorktreeHeight() => worktreeScroller.MaxHeight = Math.Clamp(Height - 420, 60, 320);

    // A nested ScrollViewer swallows the wheel even at its ends; hand it to the project list instead.
    private void forwardWorktreeWheel(object sender, System.Windows.Input.MouseWheelEventArgs args) {
        var isAtEdge = args.Delta > 0 ? worktreeScroller.VerticalOffset <= 0
            : worktreeScroller.VerticalOffset >= worktreeScroller.ScrollableHeight;
        if (!isAtEdge) { return; }
        args.Handled = true;
        todoScroller.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta) {
            RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }

    internal void loadWorktreeReport(bool force = true) {
        if (isExiting) { return; }
        try {
            var profile = settings.worktreeProfilePath;
            if (string.IsNullOrWhiteSpace(profile)) {
                worktreeConfig = null;
                worktreeReportStamp = null;
                worktreeLoad = new(WorktreeReportState.NOT_CONFIGURED, null, null);
                renderWorktrees();
                return;
            }
            try { worktreeConfig = WorktreeToolConfig.read(profile); }
            catch (Exception exception) {
                worktreeConfig = null;
                worktreeReportStamp = null;
                worktreeLoad = new(WorktreeReportState.CONFIG_ERROR, null, "profile 無法使用：" + exception.Message);
                renderWorktrees();
                return;
            }
            var stamp = readReportStamp(worktreeConfig.reportPath);
            if (!force && stamp == worktreeReportStamp && worktreeLoad.state != WorktreeReportState.NOT_CONFIGURED
                && worktreeLoad.state != WorktreeReportState.CONFIG_ERROR) {
                // Same file: only time-dependent warnings (report age) can change.
                updateWorktreeAlerts();
                updateWorktreeButtons();
                return;
            }
            worktreeLoad = WorktreeReportLoad.read(worktreeConfig.reportPath);
            // Keep retrying unreadable or half-written reports on the next tick even if the file stamp is unchanged.
            worktreeReportStamp = worktreeLoad.state == WorktreeReportState.INVALID ? null : stamp;
        } catch (Exception exception) {
            worktreeReportStamp = null;
            worktreeLoad = new(WorktreeReportState.INVALID, null, "報告無法讀取：" + exception.Message);
        }
        renderWorktrees();
    }

    private static WorktreeReportStamp? readReportStamp(string path) {
        var file = new FileInfo(path);
        return file.Exists ? new(path, file.LastWriteTimeUtc, file.Length) : null;
    }

    private void renderWorktrees() {
        var scrollOffset = worktreeScroller.VerticalOffset;
        worktreeRows.Children.Clear();
        var report = worktreeLoad.report;
        if (report == null) {
            worktreeHeading.Text = worktreeLoad.state switch {
                WorktreeReportState.NOT_CONFIGURED => "工作樹 · 未設定",
                WorktreeReportState.MISSING => "工作樹 · 尚無報告",
                _ => "工作樹 · 無法讀取"
            };
            worktreeCounts.Text = worktreeLoad.state == WorktreeReportState.NOT_CONFIGURED
                ? "請按「選擇 profile…」指定 agent-rules 的 profile.json。" : "";
            worktreeCounts.Visibility = string.IsNullOrEmpty(worktreeCounts.Text) ? Visibility.Collapsed : Visibility.Visible;
            worktreeInfo.Text = worktreeConfig == null ? "" : "報告位置：" + worktreeConfig.reportPath;
            worktreeRows.Children.Add(Theme.label(worktreeLoad.state == WorktreeReportState.NOT_CONFIGURED
                ? "設定後會讀取 agent-rules 產生的工作樹週報。" : "目前沒有可顯示的工作樹資料。", 12));
        } else {
            worktreeHeading.Text = $"工作樹 · {report.worktrees.Count} 個";
            worktreeCounts.Text = string.Join(" · ", report.getStatusCounts()
                .Select(item => $"{WorktreeStatuses.getName(item.status)} {item.count}"));
            worktreeCounts.Visibility = Visibility.Visible;
            worktreeInfo.Text = "報告時間：" + (report.createdAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "未知");
            if (report.worktrees.Count == 0) { worktreeRows.Children.Add(Theme.label("報告中沒有主 checkout 以外的工作樹。", 12)); }
            foreach (var entry in report.worktrees) { worktreeRows.Children.Add(createWorktreeRow(entry)); }
        }
        worktreeInfo.ToolTip = worktreeConfig?.reportPath;
        updateWorktreeAlerts();
        updateWorktreeButtons();
        worktreeScroller.UpdateLayout();
        worktreeScroller.ScrollToVerticalOffset(scrollOffset);
    }

    private static StackPanel createWorktreeRow(WorktreeEntry entry) {
        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        AutomationProperties.SetAutomationId(row, "WorktreeRow:" + entry.path);
        var title = Theme.label("", 13);
        title.Inlines.Add(new Run((string.IsNullOrEmpty(entry.project) ? "（未知專案）" : entry.project) + " · "));
        var state = new Run(entry.statusName) { FontWeight = FontWeights.SemiBold };
        state.SetResourceReference(TextElement.ForegroundProperty, Theme.getWorktreeStatusBrushKey(entry.status));
        title.Inlines.Add(state);
        row.Children.Add(title);
        var owner = string.IsNullOrWhiteSpace(entry.owner) ? "負責人未登記" : "負責人：" + entry.owner;
        row.Children.Add(Theme.label($"{entry.branchText} · {owner}", 12));
        var path = Theme.label(entry.path, 11);
        path.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        path.ToolTip = entry.path;
        row.Children.Add(path);
        row.Children.Add(Theme.label($"{entry.idleText} · {entry.sizeText}", 12));
        if (entry.warnings.Count > 0) { row.Children.Add(Theme.label("警示：" + string.Join(", ", entry.warnings), 12)); }
        return row;
    }

    private void updateWorktreeAlerts() {
        var alerts = new System.Collections.Generic.List<string>();
        if (worktreeLoad.message != null) { alerts.Add(worktreeLoad.message); }
        if (worktreeLoad.report != null) { alerts.AddRange(worktreeLoad.report.getAlerts(worktreeClock())); }
        if (worktreeRunMessage != null) { alerts.Add(worktreeRunMessage); }
        var text = string.Join("\n", alerts.Select(alert => "⚠ " + alert));
        if (worktreeAlert.Text != text) { worktreeAlert.Text = text; }
        worktreeAlert.Visibility = alerts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void updateWorktreeButtons() {
        var reason = worktreeConfig == null ? "請先選擇 profile。" : worktreeConfig.regenerateUnavailableReason;
        regenerateWorktreesButton.IsEnabled = reason == null && !isRegeneratingWorktrees && !isExiting;
        regenerateWorktreesButton.ToolTip = isRegeneratingWorktrees ? "報告產生中，完成後自動重新載入"
            : reason ?? "以背景執行 agent-rules 的唯讀工作樹盤點";
        openWorktreeFolderButton.IsEnabled = worktreeConfig != null;
        chooseWorktreeProfileButton.IsEnabled = !isRegeneratingWorktrees && !isExiting;
        chooseWorktreeProfileButton.ToolTip = isRegeneratingWorktrees ? "報告產生中，完成後才能更換 profile" : "指定本機 agent-rules 的 profile.json";
        openWorktreeFolderButton.ToolTip = worktreeConfig?.reportDirectory ?? "請先選擇 profile。";
    }

    internal Task regenerateWorktreeReport() {
        if (isRegeneratingWorktrees || isExiting || worktreeConfig == null) { return Task.CompletedTask; }
        worktreeRegenerateTask = runWorktreeReport(worktreeConfig);
        return worktreeRegenerateTask;
    }

    private async Task runWorktreeReport(WorktreeToolConfig config) {
        isRegeneratingWorktrees = true;
        regenerateWorktreesButton.Content = "產生中…";
        updateWorktreeButtons();
        try {
            var result = await worktreeRunner.run(config.createRegenerateStartInfo(), worktreeCancellation.Token);
            if (isExiting) { return; }
            worktreeRunMessage = result.failureMessage;
            if (worktreeRunMessage == null) { showStatus("已重新產生工作樹報告 " + DateTime.Now.ToString("HH:mm")); }
            else { showStatus(worktreeRunMessage, true); }
        } catch (Exception exception) {
            if (isExiting) { return; }
            worktreeRunMessage = "報告產生失敗：" + exception.Message;
            showStatus(worktreeRunMessage, true);
        } finally {
            isRegeneratingWorktrees = false;
            regenerateWorktreesButton.Content = "重新產生報告";
            if (!isExiting) { loadWorktreeReport(); }
        }
    }

    private void openWorktreeReportFolder() {
        if (worktreeConfig == null) { return; }
        try { ShellFileLauncher.openFolder(worktreeConfig.reportDirectory); }
        catch (Exception exception) { showStatus("無法開啟報告資料夾：" + exception.Message, true); }
    }

    private void chooseWorktreeProfile() {
        if (isRegeneratingWorktrees || isExiting) { return; }
        var dialog = new OpenFileDialog { Title = "選擇 agent-rules profile", Filter = "JSON 檔案 (*.json)|*.json|所有檔案 (*.*)|*.*",
            CheckFileExists = true };
        try {
            if (!string.IsNullOrWhiteSpace(settings.worktreeProfilePath)) {
                dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.worktreeProfilePath));
                dialog.FileName = Path.GetFileName(settings.worktreeProfilePath);
            }
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { }
        if (dialog.ShowDialog(this) != true) { return; }
        setWorktreeProfile(dialog.FileName);
    }

    internal void setWorktreeProfile(string? profilePath) {
        if (isRegeneratingWorktrees) { return; }
        settings.worktreeProfilePath = string.IsNullOrWhiteSpace(profilePath) ? null : profilePath;
        worktreeRunMessage = null;
        saveSettings();
        loadWorktreeReport();
    }
}
