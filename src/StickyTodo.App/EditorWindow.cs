using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StickyTodo.Core;

namespace StickyTodo;

internal sealed class EditorWindow : Window {
    private readonly ComboBox projectSelector = new() { MinHeight = 32, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox contentInput = new() {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(10), MinHeight = 100
    };
    private readonly TextBlock errorLabel = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    private readonly Button recoverButton = new() { Content = "保留草稿，重新載入並改為新增", Visibility = Visibility.Collapsed };
    private readonly Func<List<string>> getProjects;
    private readonly Action<string, string> saveItem;
    private readonly Action recoverAsNew;
    private readonly string? originalProject;
    private readonly string originalContent;
    private bool isSaved;
    private readonly Button deleteButton = new() { Content = "刪除待辦", Foreground = Brushes.Firebrick };

    internal EditorWindow(string project, string content, bool isEdit, Func<List<string>> getProjects,
        Action<string, string> saveItem, Action recoverAsNew, Action? deleteItem = null) {
        this.getProjects = getProjects;
        this.saveItem = saveItem;
        this.recoverAsNew = recoverAsNew;
        Background = Theme.paper;
        Foreground = Theme.ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        FontSize = 14;
        originalProject = project;
        originalContent = content;
        Title = isEdit ? "編輯待辦" : "新增待辦";
        Width = 410;
        Height = 540;
        MinWidth = 350;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var frame = new DockPanel { Background = Theme.paper };
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var root = new DockPanel { Margin = new Thickness(18, 0, 18, 18) };
        var header = new StackPanel();
        var projectRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var refreshButton = new Button { Content = "重新整理", ToolTip = "重新讀取專案資料夾" };
        refreshButton.Click += (_, _) => refreshProjects(projectSelector.SelectedItem as string);
        DockPanel.SetDock(refreshButton, Dock.Right);
        projectRow.Children.Add(refreshButton);
        projectRow.Children.Add(Theme.label("專案"));
        header.Children.Add(projectRow);
        header.Children.Add(projectSelector);
        var contentLabel = Theme.label("待辦內容");
        contentLabel.Margin = new Thickness(0, 16, 0, 8);
        header.Children.Add(contentLabel);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new ScrollViewer { Content = errorLabel, MaxHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        footer.Children.Add(recoverButton);
        recoverButton.Click += (_, _) => {
            try {
                recoverAsNew();
                deleteButton.Visibility = Visibility.Collapsed;
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
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        contentInput.Text = content;
        root.Children.Add(contentInput);
        frame.Children.Add(root);
        Content = frame;
        refreshProjects(project);
        Loaded += (_, _) => { contentInput.Focus(); contentInput.CaretIndex = contentInput.Text.Length; };
        Closing += (_, eventArgs) => {
            if (!isSaved && (contentInput.Text != originalContent || projectSelector.SelectedItem as string != originalProject)) {
                eventArgs.Cancel = MessageBox.Show(this, "尚未儲存的內容將放棄。確定關閉？", "保留草稿",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes;
            }
        };
    }

    internal void notifyExternalChange() {
        deleteButton.IsEnabled = false;
        errorLabel.Text = "資料檔已在外部變更；草稿仍保留。請重新載入並改為新增，或取消後重新編輯。";
        recoverButton.Visibility = Visibility.Visible;
    }

    private void refreshProjects(string? selected) {
        try {
            var projects = getProjects();
            if (originalProject != null && !projects.Contains(originalProject)) {
                projects.Insert(0, originalProject);
            }
            projectSelector.ItemsSource = projects;
            projectSelector.SelectedItem = selected != null && projects.Contains(selected) ? selected : "其他";
        } catch (Exception exception) {
            errorLabel.Text = "無法讀取專案清單：" + exception.Message;
            projectSelector.ItemsSource = new[] { originalProject ?? "其他", "其他" }.Distinct().ToList();
            projectSelector.SelectedIndex = 0;
        }
    }

    private void save() {
        try {
            saveItem(projectSelector.SelectedItem as string ?? "其他", contentInput.Text);
            isSaved = true;
            DialogResult = true;
        } catch (DataConflictException) {
            notifyExternalChange();
        } catch (Exception exception) {
            errorLabel.Text = "未儲存，內容仍保留：" + exception.Message;
        }
    }
}
