using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyTodo;

internal sealed class ProjectMultiSelector : Grid {
    private readonly Button toggleButton = new();
    private readonly TextBlock summary = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Popup popup = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
    private readonly StackPanel choicesPanel = new();
    private readonly List<CheckBox> choices = [];

    internal ProjectMultiSelector() {
        MinHeight = 36;
        Margin = new Thickness(0, 6, 0, 0);
        toggleButton.Content = summary;
        toggleButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        toggleButton.SetResourceReference(StyleProperty, "ProjectMultiSelectButton");
        AutomationProperties.SetName(toggleButton, "專案（可多選）");
        AutomationProperties.SetHelpText(toggleButton, "開啟後可使用 Tab 移動，按空白鍵選取多個專案");
        toggleButton.Click += (_, _) => setOpen(!popup.IsOpen);
        toggleButton.PreviewKeyDown += onToggleKeyDown;
        Children.Add(toggleButton);

        var scroll = new ScrollViewer {
            Content = choicesPanel, MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var surface = new Border {
            Child = scroll, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Padding = new Thickness(4), Margin = new Thickness(0, 3, 0, 0)
        };
        surface.SetResourceReference(BackgroundProperty, "InputSurface");
        surface.SetResourceReference(Border.BorderBrushProperty, "PressedSurface");
        surface.SetBinding(MinWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = this });
        surface.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = this });
        surface.PreviewKeyDown += onPopupKeyDown;
        popup.PlacementTarget = this;
        popup.Child = surface;
        popup.Closed += (_, _) => toggleButton.Focus();
    }

    internal IReadOnlyList<string> selectedProjects => choices
        .Where(choice => choice.IsChecked == true)
        .Select(choice => (string)choice.Tag).ToList();

    internal void setProjects(IEnumerable<string> projects, IEnumerable<string> selectedProjects) {
        var selected = selectedProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        choices.Clear();
        choicesPanel.Children.Clear();
        foreach (var project in projects.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var choice = new CheckBox {
                Content = new TextBlock { Text = project, TextWrapping = TextWrapping.Wrap },
                Tag = project, IsChecked = selected.Contains(project), Padding = new Thickness(6, 5, 6, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(choice, project);
            choice.Checked += (_, _) => updateSummary();
            choice.Unchecked += (_, _) => updateSummary();
            choices.Add(choice);
            choicesPanel.Children.Add(choice);
        }
        if (!choices.Any(choice => choice.IsChecked == true) && choices.Count > 0) {
            var fallback = choices.FirstOrDefault(choice => string.Equals((string)choice.Tag, "其他", StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            fallback.IsChecked = true;
        }
        updateSummary();
    }

    internal void setSelectedProjects(IEnumerable<string> selectedProjects) {
        var selected = selectedProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in choices) {
            choice.IsChecked = selected.Contains((string)choice.Tag);
        }
        updateSummary();
    }

    private void updateSummary() {
        var selected = selectedProjects;
        summary.Text = selected.Count switch {
            0 => "請選擇專案",
            1 => selected[0],
            2 => string.Join("、", selected),
            _ => $"已選 {selected.Count} 個專案"
        };
        AutomationProperties.SetItemStatus(toggleButton, summary.Text);
    }

    private void setOpen(bool isOpen) {
        popup.IsOpen = isOpen;
        if (isOpen) {
            Dispatcher.BeginInvoke(() => (choices.FirstOrDefault(choice => choice.IsChecked == true) ?? choices.FirstOrDefault())?.Focus());
        }
    }

    private void onToggleKeyDown(object sender, KeyEventArgs eventArgs) {
        if (eventArgs.Key is Key.Down or Key.Enter or Key.Space) {
            setOpen(true);
            eventArgs.Handled = true;
        }
    }

    private void onPopupKeyDown(object sender, KeyEventArgs eventArgs) {
        if (eventArgs.Key == Key.Escape) {
            setOpen(false);
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) { return; }
        var current = choices.FindIndex(choice => choice.IsKeyboardFocusWithin);
        var target = eventArgs.Key switch {
            Key.Home => 0,
            Key.End => choices.Count - 1,
            Key.Up => Math.Max(0, current - 1),
            _ => Math.Min(choices.Count - 1, current + 1)
        };
        if (target >= 0) { choices[target].Focus(); }
        eventArgs.Handled = true;
    }
}
