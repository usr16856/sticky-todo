using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StickyTodo.Core;
using Forms = System.Windows.Forms;

namespace StickyTodo;

internal sealed class MainWindow : Window {
    private const string STARTUP_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string projectRoot;
    private readonly TodoStore store;
    private readonly SettingsStore settingsStore;
    private readonly AppSettings settings;
    private readonly EventWaitHandle activationEvent;
    private readonly bool isTestInstance;
    private readonly StackPanel groupPanel = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock summary = Theme.label("隨手記下，慢慢完成。", 12);
    private readonly Button addButton = new() { Content = "＋ 新增待辦", MinHeight = 36 };
    private readonly Button reloadButton = new() { Content = "重新載入", Visibility = Visibility.Collapsed };
    private readonly CheckBox pinBox = new() { Content = "置頂", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
    private readonly CheckBox hideBox = new() { Content = "隱藏已完成", Margin = new Thickness(0, 10, 0, 2) };
    private readonly DispatcherTimer pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Slider opacitySlider = new() { Minimum = 40, Maximum = 100, SmallChange = 1, LargeChange = 10,
        TickFrequency = 1, IsSnapToTickEnabled = true, Width = 100, Height = 24, VerticalAlignment = VerticalAlignment.Top };
    private double appliedOpacity = 1.0;
    private bool isApplyingOpacity;
    private readonly System.Drawing.Icon trayImage;
    private TodoSnapshot? snapshot;
    private EditorWindow? editor;
    private bool isExiting;
    private bool isReady;
    private bool hasDataError;

    internal MainWindow(string projectRoot, string dataPath, string settingsDirectory, bool isTestInstance, EventWaitHandle activationEvent) {
        this.projectRoot = projectRoot;
        this.isTestInstance = isTestInstance;
        this.activationEvent = activationEvent;
        Background = Theme.paper;
        Foreground = Theme.ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        FontSize = 14;
        addButton.SetResourceReference(StyleProperty, "PrimaryButton");
        store = new TodoStore(dataPath);
        settingsStore = new SettingsStore(Path.Combine(settingsDirectory, "settings.json"));
        string? settingsWarning = null;
        try {
            settings = settingsStore.read();
        } catch (Exception exception) {
            settings = new AppSettings();
            settingsWarning = "設定無法讀取，已使用預設值：" + exception.Message;
        }
        Title = isTestInstance ? "StickyTodo · 測試便條紙" : "StickyTodo · 我的待辦";
        var iconUri = new Uri("pack://application:,,,/StickyTodo;component/Assets/StickyTodo.ico");
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        Width = Math.Clamp(settings.width, 340, 1600);
        Height = Math.Clamp(settings.height, 360, 1400);
        MinWidth = 340;
        MinHeight = 360;
        Topmost = settings.isTopmost;
        if (settings.left.HasValue && settings.top.HasValue) {
            Left = settings.left.Value;
            Top = settings.top.Value;
        } else {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        var frame = new DockPanel { Background = Theme.paper };
        var caption = new WindowHeader(this);
        AllowsTransparency = true;
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var root = new DockPanel { Margin = new Thickness(16, 0, 16, 12) };
        var header = new StackPanel();
        summary.Foreground = Theme.muted;
        summary.Margin = new Thickness(0, 4, 0, 10);
        header.Children.Add(summary);
        var toolbar = new DockPanel();
        var settingsButton = new Button { Content = "設定", ToolTip = "開啟資料檔、開機啟動及結束 App" };
        settingsButton.Click += (_, _) => showSettings(settingsButton);
        DockPanel.SetDock(settingsButton, Dock.Right);
        toolbar.Children.Add(settingsButton);
        pinBox.IsChecked = settings.isTopmost;
        pinBox.Click += (_, _) => { Topmost = pinBox.IsChecked == true; settings.isTopmost = Topmost; saveSettings(); };
        DockPanel.SetDock(pinBox, Dock.Right);
        toolbar.Children.Add(pinBox);
        addButton.Click += (_, _) => openEditor(null, -1);
        toolbar.Children.Add(addButton);
        header.Children.Add(toolbar);
        hideBox.IsChecked = settings.hideCompleted;
        hideBox.Click += (_, _) => { settings.hideCompleted = hideBox.IsChecked == true; render(); saveSettings(); };
        header.Children.Add(hideBox);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footer = new StackPanel();
        var statusRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition());
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Margin = new Thickness(0, 4, 12, 0);
        statusRow.Children.Add(status);
        opacitySlider.SetResourceReference(StyleProperty, "OpacitySlider");
        System.Windows.Automation.AutomationProperties.SetName(opacitySlider, "便條紙不透明度百分比");
        opacitySlider.Value = Math.Round(settings.windowOpacity * 100);
        opacitySlider.ToolTip = $"不透明度：{opacitySlider.Value:0}%";
        opacitySlider.ValueChanged += (_, _) => applyOpacity();
        opacitySlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler((_, _) => {
                opacityHint.PlacementTarget = opacitySlider;
                opacityHint.IsOpen = true;
            }));
        opacitySlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => opacityHint.IsOpen = false));
        Grid.SetColumn(opacitySlider, 1);
        statusRow.Children.Add(opacitySlider);
        footer.Children.Add(statusRow);
        reloadButton.Click += (_, _) => loadData();
        footer.Children.Add(reloadButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var todoScroller = new ScrollViewer { Content = groupPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 10, 0, 0) };
        todoScroller.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] =
            Application.Current.FindResource("MinimalVerticalScrollBar");
        root.Children.Add(todoScroller);
        frame.Children.Add(root);
        Content = frame;

        using (var iconStream = Application.GetResourceStream(iconUri).Stream)
        using (var resourceIcon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize)) {
            trayImage = (System.Drawing.Icon)resourceIcon.Clone();
        }
        trayIcon = new Forms.NotifyIcon { Icon = trayImage, Text = "StickyTodo · 我的待辦", Visible = true };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("顯示便條紙", null, (_, _) => Dispatcher.Invoke(showWindow));
        trayMenu.Items.Add("結束 StickyTodo", null, (_, _) => Dispatcher.Invoke(exit));
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(showWindow);
        Closing += onClosing;
        LocationChanged += (_, _) => scheduleSettings();
        SizeChanged += (_, _) => scheduleSettings();
        settingsTimer.Tick += (_, _) => { settingsTimer.Stop(); saveSettings(); };
        pollTimer.Tick += (_, _) => poll();
        Loaded += (_, _) => {
            keepWindowVisible();
            isReady = true;
            try { store.initialize(); loadData(); } catch (Exception exception) { showDataError(exception); }
            if (settingsWarning != null) { showStatus(settingsWarning, true); }
            pollTimer.Start();
        };
        SystemEvents.DisplaySettingsChanged += onDisplaySettingsChanged;
        SourceInitialized += (_, _) => applyOpacity();
    }

    private readonly ToolTip opacityHint = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

    private void applyOpacity() {
        if (isApplyingOpacity) { return; }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) { return; }
        isApplyingOpacity = true;
        try {
            Opacity = opacitySlider.Value / 100.0;
            appliedOpacity = opacitySlider.Value / 100.0;
            settings.windowOpacity = appliedOpacity;
            scheduleSettings();
        } catch (Exception exception) {
            opacitySlider.Value = Math.Round(appliedOpacity * 100);
            settings.windowOpacity = appliedOpacity;
            showStatus("透明度未調整：" + exception.Message, true);
        } finally {
            opacitySlider.ToolTip = $"不透明度：{opacitySlider.Value:0}%";
            opacityHint.Content = opacitySlider.ToolTip;
            isApplyingOpacity = false;
        }
    }

    private void loadData() {
        try {
            snapshot = store.read();
            hasDataError = false;
            addButton.IsEnabled = true;
            reloadButton.Visibility = Visibility.Collapsed;
            render();
            showStatus("已載入 · 變更會自動儲存");
        } catch (Exception exception) { showDataError(exception); }
    }

    private void showDataError(Exception exception) {
        hasDataError = true;
        addButton.IsEnabled = false;
        reloadButton.Visibility = Visibility.Visible;
        showStatus("資料未變更：" + exception.Message, true);
        render();
    }

    private void render() {
        groupPanel.Children.Clear();
        if (snapshot == null) { return; }
        var count = snapshot.document.groups.Sum(group => group.items.Count);
        var completed = snapshot.document.groups.Sum(group => group.items.Count(item => item.isCompleted));
        summary.Text = count == 0 ? "隨手記下，慢慢完成。" : $"{count - completed} 項待辦 · {completed} 項已完成";
        if (count == 0) {
            var empty = new StackPanel { Margin = new Thickness(12, 52, 12, 0) };
            var emptyTitle = Theme.label("把想到的事，先放這裡。", 19);
            emptyTitle.TextAlignment = TextAlignment.Center;
            empty.Children.Add(emptyTitle);
            var hint = Theme.label("按「新增待辦」選擇專案，\n記下下一個想做的功能。", 13);
            hint.TextAlignment = TextAlignment.Center;
            hint.Foreground = Theme.muted;
            hint.Margin = new Thickness(0, 12, 0, 0);
            empty.Children.Add(hint);
            groupPanel.Children.Add(empty);
        }
        foreach (var group in snapshot.document.groups.Where(group => group.items.Count > 0)
                     .OrderBy(group => group.name == "其他" ? 1 : 0).ThenBy(group => group.name, StringComparer.OrdinalIgnoreCase)) {
            var heading = Theme.label(group.name, 16);
            heading.FontWeight = FontWeights.SemiBold;
            var expander = new Expander { Header = heading, IsExpanded = !settings.collapsedProjects.Contains(group.name),
                Margin = new Thickness(0, 1, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            expander.Expanded += (_, _) => { settings.collapsedProjects.Remove(group.name); saveSettings(); };
            expander.Collapsed += (_, _) => {
                if (!settings.collapsedProjects.Contains(group.name)) { settings.collapsedProjects.Add(group.name); }
                saveSettings();
            };
            var rows = new StackPanel { Margin = new Thickness(10, 3, 0, 0) };
            for (var index = 0; index < group.items.Count; index++) {
                var itemIndex = index;
                var item = group.items[index];
                if (settings.hideCompleted && item.isCompleted) { continue; }
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7), IsEnabled = !hasDataError };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var check = new CheckBox { IsChecked = item.isCompleted, VerticalAlignment = VerticalAlignment.Top,
                    // Match the visible CJK glyph center, which sits below the line-box center.
                    Height = 22, Margin = new Thickness(0, 8, 7, 0), ToolTip = item.isCompleted ? "取消完成" : "標記完成" };
                System.Windows.Automation.AutomationProperties.SetName(check, $"{group.name} 第 {index + 1} 項完成狀態");
                check.Click += (_, _) => toggleItem(group.name, itemIndex, check.IsChecked == true);
                row.Children.Add(check);
                var text = Theme.label($"{index + 1}. {item.content}");
                text.LineHeight = 22;
                text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                text.Margin = new Thickness(0, 6, 4, 6);
                if (item.isCompleted) { text.TextDecorations = TextDecorations.Strikethrough; text.Foreground = Theme.muted; }
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                var editLabel = Theme.label("編輯", text.FontSize);
                editLabel.LineHeight = text.LineHeight;
                editLabel.LineStackingStrategy = text.LineStackingStrategy;
                editLabel.TextWrapping = TextWrapping.NoWrap;
                // The one-DIP button border puts its text at the same six-DIP inset as the task text.
                var edit = new Button { Content = editLabel, Padding = new Thickness(6, 0, 6, 0),
                    Height = 24, Margin = new Thickness(3, 5, 3, 0),
                    VerticalAlignment = VerticalAlignment.Top, ToolTip = $"編輯 {group.name} 第 {index + 1} 項" };
                edit.Click += (_, _) => openEditor(group.name, itemIndex);
                Grid.SetColumn(edit, 2);
                row.Children.Add(edit);
                rows.Children.Add(row);
            }
            if (rows.Children.Count == 0) { rows.Children.Add(Theme.label("此專案已全部完成", 12)); }
            expander.Content = rows;
            groupPanel.Children.Add(expander);
        }
    }

    private void toggleItem(string project, int index, bool completed) {
        if (snapshot == null) { return; }
        try {
            var document = snapshot.document.clone();
            var group = document.groups.Single(group => group.name == project);
            group.items[index] = group.items[index] with { isCompleted = completed };
            snapshot = store.save(document, snapshot.version);
            render();
            showStatus("已儲存 " + DateTime.Now.ToString("HH:mm"));
        } catch (Exception exception) {
            render();
            showStatus("未儲存：" + exception.Message, true);
            reloadButton.Visibility = Visibility.Visible;
        }
    }

    private void openEditor(string? project, int index) {
        if (snapshot == null || hasDataError || editor != null) { return; }
        var baseline = snapshot;
        var originalProject = project;
        var originalIndex = index;
        var content = project == null ? "" : baseline.document.groups.Single(group => group.name == project).items[index].content;
        IReadOnlyList<string> selectedProjects = project == null
            ? (settings.lastProjects.Count > 0 ? settings.lastProjects : [settings.lastProject])
            : [project];
        if (project == null) {
            try {
                var available = ProjectCatalog.discover(projectRoot);
                selectedProjects = selectedProjects
                    .Where(selected => available.Contains(selected, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (selectedProjects.Count == 0) { selectedProjects = ["其他"]; }
            } catch { selectedProjects = ["其他"]; }
        }
        editor = new EditorWindow(selectedProjects, content, project != null, () => ProjectCatalog.discover(projectRoot),
            (targetProjects, text) => {
                var document = baseline.document.clone();
                var isAdding = originalProject == null;
                if (isAdding) { document.addToProjects(targetProjects, text); }
                else { document.edit(originalProject!, originalIndex, targetProjects.Single(), text); }
                snapshot = store.save(document, baseline.version);
                if (isAdding) {
                    settings.lastProjects = targetProjects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    settings.lastProject = settings.lastProjects[0];
                }
                hasDataError = false;
                saveSettings();
                showStatus("已儲存 " + DateTime.Now.ToString("HH:mm"));
            }, () => {
                baseline = store.read();
                snapshot = baseline;
                originalProject = null;
                originalIndex = -1;
                hasDataError = false;
            }, () => {
                if (originalProject == null) { throw new InvalidOperationException("新增草稿不能刪除原待辦。"); }
                var document = baseline.document.clone();
                document.remove(originalProject, originalIndex);
                snapshot = store.save(document, baseline.version);
                hasDataError = false;
                showStatus("已刪除 " + DateTime.Now.ToString("HH:mm"));
            }) { Owner = this };
        try { editor.ShowDialog(); } finally { editor = null; render(); }
    }

    private void poll() {
        if (activationEvent.WaitOne(0)) { showWindow(); }
        if (snapshot == null) { return; }
        try {
            if (store.readVersion() == snapshot.version && !hasDataError) { return; }
            if (editor != null) { editor.notifyExternalChange(); return; }
            loadData();
        } catch (Exception exception) {
            if (editor == null) { showDataError(exception); }
        }
    }

    private void showSettings(Button anchor) {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            HorizontalOffset = -150 };
        var openFile = new MenuItem { Header = "開啟待辦資料檔" };
        openFile.Click += (_, _) => {
            try { ShellFileLauncher.openDefault(store.filePath); }
            catch (Exception exception) { showStatus("無法開啟資料檔：" + exception.Message, true); }
        };
        menu.Items.Add(openFile);
        var chooseApplication = new MenuItem { Header = "選擇 App 開啟…" };
        chooseApplication.Click += (_, _) => {
            try { ShellFileLauncher.chooseApplication(this, store.filePath); }
            catch (Exception exception) { showStatus("無法選擇 App：" + exception.Message, true); }
        };
        menu.Items.Add(chooseApplication);
        var refresh = new MenuItem { Header = "重新載入資料" };
        refresh.Click += (_, _) => loadData();
        menu.Items.Add(refresh);
        var startup = new MenuItem { Header = "登入 Windows 時啟動", IsCheckable = true, IsEnabled = !isTestInstance };
        try {
            using var registry = Registry.CurrentUser.OpenSubKey(STARTUP_KEY);
            startup.IsChecked = registry?.GetValue("StickyTodo") is string;
        } catch (Exception exception) { startup.IsEnabled = false; startup.ToolTip = exception.Message; }
        startup.Click += (_, _) => {
            try {
                using var registry = Registry.CurrentUser.CreateSubKey(STARTUP_KEY);
                if (startup.IsChecked) { registry.SetValue("StickyTodo", "\"" + Environment.ProcessPath + "\""); }
                else { registry.DeleteValue("StickyTodo", false); }
            } catch (Exception exception) { showStatus("開機啟動設定失敗：" + exception.Message, true); }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "結束 StickyTodo" };
        quit.Click += (_, _) => exit();
        menu.Items.Add(quit);
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void showStatus(string message, bool isError = false) {
        status.Text = message;
        status.Foreground = isError ? Brushes.Firebrick : Theme.muted;
    }

    private void scheduleSettings() {
        if (!isReady) { return; }
        settingsTimer.Stop();
        settingsTimer.Start();
    }

    private void saveSettings() {
        if (!isReady) { return; }
        if (WindowState == WindowState.Normal) {
            settings.width = Width;
            settings.height = Height;
            settings.left = Left;
            settings.top = Top;
        }
        try { settingsStore.save(settings); }
        catch (Exception exception) { showStatus("設定尚未儲存：" + exception.Message, true); }
    }

    private void onDisplaySettingsChanged(object? sender, EventArgs args) => Dispatcher.BeginInvoke(keepWindowVisible);

    private void keepWindowVisible() {
        if (WindowState != WindowState.Normal) { return; }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) { return; }
        var point = PointToScreen(new Point(0, 0));
        var titleRectangle = new System.Drawing.Rectangle((int)point.X, (int)point.Y, Math.Max(100, (int)ActualWidth), 40);
        if (Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(titleRectangle))) { return; }
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = transform.Transform(new Point(area.Left, area.Top));
        var size = transform.Transform(new Vector(area.Width, area.Height));
        Width = Math.Max(MinWidth, Math.Min(Width, size.X));
        Height = Math.Max(MinHeight, Math.Min(Height, size.Y));
        Left = origin.X + Math.Max(0, (size.X - Width) / 2);
        Top = origin.Y + Math.Max(0, (size.Y - Height) / 2);
    }

    private void showWindow() {
        Show();
        if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; }
        keepWindowVisible();
        Activate();
        if (editor != null) { editor.Activate(); }
    }

    private void onClosing(object? sender, CancelEventArgs args) {
        if (isExiting) { return; }
        args.Cancel = true;
        saveSettings();
        Hide();
    }

    private void exit() {
        if (editor != null) {
            editor.Close();
            if (editor?.IsVisible == true) { return; }
        }
        saveSettings();
        isExiting = true;
        pollTimer.Stop();
        settingsTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= onDisplaySettingsChanged;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayImage.Dispose();
        Application.Current.Shutdown();
    }
}
