using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StickyTodo.Core;

namespace StickyTodo;

internal sealed class EditorWindow : Window {
    private readonly ComboBox projectSelector = new() { MinHeight = 32, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ProjectMultiSelector multiProjectSelector = new();
    private readonly Grid projectSelectorHost = new();
    private readonly ComboBox prioritySelector = new() { MinHeight = 32, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox syncMatchesCheckBox = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel syncPanel = new();
    private readonly TextBlock priorityScopeHint = Theme.label("僅套用目前待辦", 12);
    private readonly TextBox contentInput = new() {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(10), MinHeight = 100
    };
    private readonly TextBlock errorLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button recoverButton = new() { Content = "保留草稿，重新載入並改為新增", Visibility = Visibility.Collapsed };
    private readonly Func<List<string>> getProjects;
    private readonly Action<IReadOnlyList<string>, string, TodoPriority, bool> saveItem;
    private readonly Action recoverAsNew;
    private readonly string? originalProject;
    private readonly string originalContent;
    private readonly TodoPriority originalPriority;
    private readonly List<TodoMatch> matchingItems;
    private bool isSaved;
    private bool isEditMode;
    private readonly List<string> originalProjects;
    private readonly Button deleteButton = new() { Content = "刪除待辦" };

    internal EditorWindow(IReadOnlyList<string> projects, string content, bool isEdit, Func<List<string>> getProjects,
        Action<IReadOnlyList<string>, string, TodoPriority, bool> saveItem, Action recoverAsNew, Action? deleteItem = null,
        TodoPriority priority = TodoPriority.NORMAL, IReadOnlyList<TodoMatch>? matchingItems = null) {
        this.getProjects = getProjects;
        this.saveItem = saveItem;
        this.recoverAsNew = recoverAsNew;
        Theme.usePaper(this);
        errorLabel.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        deleteButton.SetResourceReference(ForegroundProperty, Theme.ERROR);
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        FontSize = 14;
        originalProjects = projects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        originalProject = originalProjects.FirstOrDefault();
        originalContent = content;
        originalPriority = priority;
        this.matchingItems = matchingItems?.ToList() ?? [];
        isEditMode = isEdit;
        AutomationProperties.SetAutomationId(projectSelector, "ProjectSelector");
        AutomationProperties.SetName(projectSelector, "專案");
        AutomationProperties.SetAutomationId(prioritySelector, "PrioritySelector");
        AutomationProperties.SetName(prioritySelector, "優先等級");
        AutomationProperties.SetAutomationId(contentInput, "TodoContent");
        AutomationProperties.SetName(contentInput, "待辦內容");
        AutomationProperties.SetAutomationId(syncMatchesCheckBox, "SyncMatchingContent");
        Title = isEdit ? "編輯待辦" : "新增待辦";
        Width = 410;
        Height = 540;
        MinWidth = 350;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var frame = new DockPanel();
        frame.SetResourceReference(Panel.BackgroundProperty, Theme.PAPER);
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var root = new Grid { Margin = new Thickness(18, 0, 18, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        var projectRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var refreshButton = new Button { Content = "重新整理", ToolTip = "重新讀取專案資料夾" };
        refreshButton.Click += (_, _) => refreshProjects(getSelectedProjects());
        DockPanel.SetDock(refreshButton, Dock.Right);
        projectRow.Children.Add(refreshButton);
        projectRow.Children.Add(Theme.label("專案"));
        header.Children.Add(projectRow);
        projectSelectorHost.Children.Add(projectSelector);
        projectSelectorHost.Children.Add(multiProjectSelector);
        header.Children.Add(projectSelectorHost);
        var priorityRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        priorityScopeHint.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        DockPanel.SetDock(priorityScopeHint, Dock.Right);
        priorityRow.Children.Add(priorityScopeHint);
        priorityRow.Children.Add(Theme.label("優先等級"));
        header.Children.Add(priorityRow);
        foreach (var level in new[] { TodoPriority.HIGH, TodoPriority.NORMAL, TodoPriority.LOW }) {
            var option = new ComboBoxItem { Content = Theme.getPriorityName(level), Tag = level };
            AutomationProperties.SetName(option, Theme.getPriorityName(level));
            option.SetResourceReference(ForegroundProperty, Theme.getPriorityBrushKey(level));
            prioritySelector.Items.Add(option);
            if (level == priority) { prioritySelector.SelectedItem = option; }
        }
        prioritySelector.SetResourceReference(ForegroundProperty, Theme.getPriorityBrushKey(priority));
        prioritySelector.SelectionChanged += (_, _) => prioritySelector.SetResourceReference(ForegroundProperty, Theme.getPriorityBrushKey(selectedPriority));
        header.Children.Add(prioritySelector);
        syncMatchesCheckBox.Content = Theme.label($"同步其他專案的 {this.matchingItems.Count} 筆同文待辦", 13);
        AutomationProperties.SetName(syncMatchesCheckBox, "同步其他專案的同文待辦");
        syncPanel.Children.Add(syncMatchesCheckBox);
        var syncSummary = Theme.label(string.Join("\n", this.matchingItems
            .GroupBy(item => item.project).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}：{group.Count()} 筆（已完成 {group.Count(item => item.isCompleted)}）")), 12);
        syncSummary.Margin = new Thickness(22, 4, 0, 0);
        syncSummary.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        AutomationProperties.SetAutomationId(syncSummary, "SyncMatchingSummary");
        var summaryScroll = new ScrollViewer { Content = syncSummary, MaxHeight = 54,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        summaryScroll.Resources[typeof(ScrollBar)] = FindResource("MinimalVerticalScrollBar");
        syncPanel.Children.Add(summaryScroll);
        header.Children.Add(syncPanel);
        syncMatchesCheckBox.Checked += (_, _) => updateSyncMode();
        syncMatchesCheckBox.Unchecked += (_, _) => updateSyncMode();
        syncMatchesCheckBox.IsChecked = isEdit && this.matchingItems.Count > 0;
        var contentLabel = Theme.label("待辦內容");
        contentLabel.Margin = new Thickness(0, 12, 0, 8);
        header.Children.Add(contentLabel);
        var headerScroll = new ScrollViewer { Content = header, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        headerScroll.Resources[typeof(ScrollBar)] = FindResource("MinimalVerticalScrollBar");
        root.Children.Add(headerScroll);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new ScrollViewer { Content = errorLabel, MaxHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        footer.Children.Add(recoverButton);
        recoverButton.Click += (_, _) => {
            try {
                var recoveredProjects = getSelectedProjects();
                recoverAsNew();
                deleteButton.Visibility = Visibility.Collapsed;
                isEditMode = false;
                this.matchingItems.Clear();
                syncMatchesCheckBox.IsChecked = false;
                multiProjectSelector.setSelectedProjects(recoveredProjects);
                updateSelectorMode();
                Title = "新增待辦（保留的草稿）";
                errorLabel.Text = "已載入最新資料；按儲存將草稿新增為一筆待辦，原項目不變。";
                recoverButton.Visibility = Visibility.Collapsed;
            } catch (Exception exception) {
                errorLabel.Text = exception.Message;
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        deleteButton.Visibility = isEdit && deleteItem != null ? Visibility.Visible : Visibility.Collapsed;
        deleteButton.Click += (_, _) => {
            var preview = originalContent.Length > 160 ? originalContent[..160] + "…" : originalContent;
            if (new DeleteConfirmationWindow(this, originalProject ?? "其他", preview).ShowDialog() != true) { return; }
            try {
                deleteItem!();
                isSaved = true;
                DialogResult = true;
            } catch (DataConflictException) {
                notifyExternalChange();
            } catch (Exception exception) {
                errorLabel.Text = "未刪除，內容仍保留：" + exception.Message;
            }
        };
        buttons.Children.Add(deleteButton);
        var cancelButton = new Button { Content = "取消" };
        cancelButton.Click += (_, _) => Close();
        var saveButton = new Button { Content = "儲存", MinWidth = 82 };
        saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        saveButton.Click += (_, _) => save();
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        // Keep content and actions visible at the minimum size; long summaries/errors can scroll.
        void updateHeaderLimit() {
            if (frame.ActualHeight > 0) {
                var availableHeight = frame.ActualHeight - caption.ActualHeight - root.Margin.Top - root.Margin.Bottom;
                headerScroll.MaxHeight = Math.Max(60, availableHeight - footer.ActualHeight
                    - footer.Margin.Top - footer.Margin.Bottom - contentInput.MinHeight);
            }
        }
        frame.SizeChanged += (_, _) => updateHeaderLimit();
        footer.SizeChanged += (_, _) => updateHeaderLimit();
        contentInput.Text = content;
        Grid.SetRow(contentInput, 1);
        root.Children.Add(contentInput);
        frame.Children.Add(root);
        Content = frame;
        refreshProjects(originalProjects);
        Loaded += (_, _) => { contentInput.Focus(); contentInput.CaretIndex = contentInput.Text.Length; };
        Closing += (_, eventArgs) => {
            if (!isSaved && hasDraftChanges) {
                eventArgs.Cancel = new DiscardDraftConfirmationWindow(this).ShowDialog() != true;
            }
        };
    }

    internal void notifyExternalChange() {
        deleteButton.IsEnabled = false;
        syncMatchesCheckBox.IsEnabled = false;
        errorLabel.Text = "資料檔已在外部變更；草稿仍保留。請重新載入並改為新增，或取消後重新編輯。";
        recoverButton.Visibility = Visibility.Visible;
    }

    private void refreshProjects(IEnumerable<string> selectedProjects) {
        try {
            var projects = getProjects();
            if (originalProject != null && !projects.Contains(originalProject, StringComparer.OrdinalIgnoreCase)) {
                projects.Insert(0, originalProject);
            }
            projectSelector.ItemsSource = projects;
            var selected = selectedProjects.Where(project => projects.Contains(project, StringComparer.OrdinalIgnoreCase)).ToList();
            if (selected.Count == 0) { selected.Add("其他"); }
            projectSelector.SelectedItem = projects.FirstOrDefault(project => project.Equals(selected[0], StringComparison.OrdinalIgnoreCase)) ?? "其他";
            multiProjectSelector.setProjects(projects, selected);
            updateSelectorMode();
        } catch (Exception exception) {
            errorLabel.Text = "無法讀取專案清單：" + exception.Message;
            var projects = new[] { originalProject ?? "其他", "其他" }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            projectSelector.ItemsSource = projects;
            projectSelector.SelectedIndex = 0;
            multiProjectSelector.setProjects(projects, selectedProjects);
            updateSelectorMode();
        }
    }

    private void save() {
        try {
            var projects = getSelectedProjects();
            if (projects.Count == 0) { throw new ArgumentException("請至少選擇一個專案。"); }
            saveItem(projects, contentInput.Text, selectedPriority, shouldSyncMatchingContent);
            isSaved = true;
            DialogResult = true;
        } catch (DataConflictException) {
            notifyExternalChange();
        } catch (Exception exception) {
            errorLabel.Text = "未儲存，內容仍保留：" + exception.Message;
        }
    }

    private IReadOnlyList<string> getSelectedProjects() {
        if (!isEditMode) { return multiProjectSelector.selectedProjects; }
        if (shouldSyncMatchingContent && originalProject != null) { return [originalProject]; }
        return projectSelector.SelectedItem is string project ? [project] : [];
    }

    private void updateSelectorMode() {
        projectSelector.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
        multiProjectSelector.Visibility = isEditMode ? Visibility.Collapsed : Visibility.Visible;
        updateSyncMode();
    }

    private TodoPriority selectedPriority => prioritySelector.SelectedItem is ComboBoxItem { Tag: TodoPriority priority }
        ? priority : TodoPriority.NORMAL;

    private bool shouldSyncMatchingContent => isEditMode && matchingItems.Count > 0 && syncMatchesCheckBox.IsChecked == true;

    internal bool hasDraftChanges => TodoDocument.normalizeLines(contentInput.Text) != TodoDocument.normalizeLines(originalContent)
        || selectedPriority != originalPriority || !sameProjects(getSelectedProjects(), originalProjects);

    private void updateSyncMode() {
        syncPanel.Visibility = isEditMode && matchingItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        priorityScopeHint.Visibility = shouldSyncMatchingContent ? Visibility.Visible : Visibility.Collapsed;
        projectSelector.IsEnabled = !shouldSyncMatchingContent;
        if (shouldSyncMatchingContent) {
            projectSelector.SelectedItem = projectSelector.Items.Cast<string>()
                .FirstOrDefault(project => project.Equals(originalProject, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool sameProjects(IReadOnlyList<string> first, IReadOnlyList<string> second) {
        return first.Count == second.Count && first.SequenceEqual(second, StringComparer.OrdinalIgnoreCase);
    }
}
