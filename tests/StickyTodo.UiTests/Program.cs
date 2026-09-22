using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StickyTodo;

internal static class Program {
    [STAThread]
    private static int Main() {
        try {
            var application = new Application();
            Theme.install(application);
            var tooltip = new ToolTip { Content = "不透明度：66%" };
            tooltip.ApplyTemplate();
            var tooltipSurface = (Border)tooltip.Template.FindName("surface", tooltip);
            check(((SolidColorBrush)tooltipSurface.Background).Color == Color.FromRgb(255, 249, 218), "Tooltip background does not match paper");
            check(((SolidColorBrush)tooltipSurface.BorderBrush).Color == Color.FromRgb(110, 86, 36), "Tooltip border does not match accent");
            check(tooltipSurface.CornerRadius == new CornerRadius(6) && tooltipSurface.Padding == new Thickness(8, 5, 8, 5), "Tooltip shape or padding incorrect");
            var scrollBar = new System.Windows.Controls.Primitives.ScrollBar {
                Style = (Style)application.FindResource("MinimalVerticalScrollBar"),
                Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = 40, ViewportSize = 20, Height = 300
            };
            scrollBar.Measure(new Size(10, 300)); scrollBar.Arrange(new Rect(0, 0, 10, 300)); scrollBar.ApplyTemplate();
            var scrollTrack = (System.Windows.Controls.Primitives.Track)scrollBar.Template.FindName("PART_Track", scrollBar);
            scrollTrack.Thumb.ApplyTemplate();
            var scrollThumb = (Border)scrollTrack.Thumb.Template.FindName("thumb", scrollTrack.Thumb);
            check(scrollBar.Width == 10 && scrollTrack.Thumb.Width == 7 && scrollThumb.CornerRadius == new CornerRadius(3.5),
                "Minimal scrollbar dimensions or rounded thumb incorrect");
            check(((SolidColorBrush)scrollThumb.Background).Color == Color.FromRgb(110, 86, 36),
                "Minimal scrollbar does not use the accent color");
            var launchFile = Path.Combine(AppContext.BaseDirectory, "open-with-test.md");
            File.WriteAllText(launchFile, "# test");
            var launchInfo = ShellFileLauncher.createDefaultStartInfo(launchFile);
            check(launchInfo.FileName == launchFile && launchInfo.UseShellExecute,
                "Default data-file launch does not use the Windows file association");
            var projectName = "這是一個很長的專案名稱用來驗證選單換行與窄視窗";
            var saved = false;
            IReadOnlyList<string> savedProjects = [];
            var editor = new EditorWindow(["SpotCam", "其他"], "", false, () => ["SpotCam", projectName, "其他"],
                (projects, _) => { saved = true; savedProjects = projects; }, () => { });
            check(!editor.AllowsTransparency && editor.Opacity == 1.0, "Editor must remain fully opaque");
            var root = (FrameworkElement)editor.Content;
            root.Measure(new Size(350, 520));
            root.Arrange(new Rect(0, 0, 350, 520));
            root.UpdateLayout();
            var controls = descendants(root).ToList();
            var textBox = controls.OfType<TextBox>().Single();
            var combo = controls.OfType<ComboBox>().Single();
            var multiSelector = controls.OfType<ProjectMultiSelector>().Single();
            check(textBox.Template.FindName("PART_ContentHost", textBox) is ScrollViewer, "TextBox content host missing");
            combo.ApplyTemplate();
            check(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup, "ComboBox popup missing");
            check(combo.Items.Count == 3 && multiSelector.selectedProjects.SequenceEqual(new[] { "SpotCam", "其他" }), "Multi-project selection lost");
            multiSelector.setSelectedProjects([projectName, "其他"]);
            check(multiSelector.selectedProjects.SequenceEqual(new[] { projectName, "其他" }), "Multi-project selection did not update");
            multiSelector.setSelectedProjects([]);
            typeof(EditorWindow).GetMethod("save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            check(!saved && descendants(root).OfType<TextBlock>().Any(block => block.Text.Contains("請至少選擇一個專案")),
                "Empty project selection was not rejected clearly");
            multiSelector.setProjects(["SpotCam", projectName, "其他"], ["遺失的專案"]);
            check(multiSelector.selectedProjects.SequenceEqual(new[] { "其他" }), "Missing selections did not fall back to Other");
            multiSelector.setSelectedProjects(["SpotCam", projectName, "其他"]);
            var selectorSummary = (TextBlock)multiSelector.Children.OfType<Button>().Single().Content;
            check(selectorSummary.Text == "已選 3 個專案", "Three-project summary is unclear");
            multiSelector.setSelectedProjects([projectName, "其他"]);
            textBox.Text = "中文\n多行 **符號**";
            root.UpdateLayout();
            check(textBox.Text.Contains("\n") && !saved, "Editing triggered save or lost multiline content");
            typeof(EditorWindow).GetMethod("save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            check(saved && savedProjects.SequenceEqual(new[] { projectName, "其他" }), "Save did not receive every selected project");
            check(controls.OfType<Button>().Count(button => button.ToolTip as string == "關閉") == 1, "Editor close button missing");
            check(!controls.OfType<Button>().Any(button => button.ToolTip as string == "最小化"), "Editor unexpectedly has minimize button");
            editor.Title = "新增待辦（保留的草稿）";
            root.UpdateLayout();
            check(descendants(root).OfType<TextBlock>().Any(block => block.Text == editor.Title), "Caption does not track title");
            editor.notifyExternalChange();
            root.Measure(new Size(350, 520));
            root.Arrange(new Rect(0, 0, 350, 520));
            root.UpdateLayout();
            var saveButton = descendants(root).OfType<Button>().Single(button => button.Content as string == "儲存");
            var saveBounds = saveButton.TransformToAncestor(root).TransformBounds(new Rect(saveButton.RenderSize));
            check(saveBounds.Bottom <= 520 && saveBounds.Right <= 350, "Save action clipped at minimum size");
            check(!saveBounds.IntersectsWith(textBox.TransformToAncestor(root).TransformBounds(new Rect(textBox.RenderSize))), "Content overlaps save action");
            var output = Path.Combine(AppContext.BaseDirectory, "editor-minimum.png");
            var bitmap = new RenderTargetBitmap(350, 520, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(output)) { encoder.Save(stream); }
            var editEditor = new EditorWindow(["SpotCam"], "既有內容", true, () => ["SpotCam", "其他"], (_, _) => { }, () => { });
            var editRoot = (FrameworkElement)editEditor.Content;
            editRoot.Measure(new Size(350, 520)); editRoot.Arrange(new Rect(0, 0, 350, 520)); editRoot.UpdateLayout();
            check(descendants(editRoot).OfType<ComboBox>().Single().Visibility == Visibility.Visible
                && descendants(editRoot).OfType<ProjectMultiSelector>().Single().Visibility == Visibility.Collapsed,
                "Edit mode must remain single-project");
            var discardWindow = new DiscardDraftConfirmationWindow(editor);
            var discardRoot = (FrameworkElement)discardWindow.Content;
            discardRoot.Measure(new Size(340, 250)); discardRoot.Arrange(new Rect(0, 0, 340, 250)); discardRoot.UpdateLayout();
            var discardButtons = descendants(discardRoot).OfType<Button>().ToList();
            check(discardButtons.Any(button => button.Content as string == "繼續編輯" && button.IsCancel)
                && discardButtons.Any(button => button.Content as string == "放棄草稿"),
                "Styled draft confirmation actions are missing");
            check(descendants(discardRoot).OfType<TextBlock>().Any(block => block.Text == "放棄尚未儲存的內容？"),
                "Styled draft confirmation message is missing");
            var discardOutput = Path.Combine(AppContext.BaseDirectory, "discard-confirmation.png");
            var discardBitmap = new RenderTargetBitmap(340, 250, 96, 96, PixelFormats.Pbgra32);
            discardBitmap.Render(discardRoot);
            var discardEncoder = new PngBitmapEncoder(); discardEncoder.Frames.Add(BitmapFrame.Create(discardBitmap));
            using (var stream = File.Create(discardOutput)) { discardEncoder.Save(stream); }
            Console.WriteLine("PASS Styled draft confirmation, Windows file association, themed scrollbar, editor template loading, multi-project selection/save, single-project edit, multiline content, title binding, minimum-size conflict layout");
            Console.WriteLine(output);
            Console.WriteLine(discardOutput);
            return 0;
        } catch (Exception exception) {
            Console.WriteLine(exception);
            return 1;
        }
    }

    private static IEnumerable<DependencyObject> descendants(DependencyObject parent) {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in descendants(child)) { yield return descendant; }
        }
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
