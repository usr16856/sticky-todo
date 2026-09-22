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
            var projectName = "這是一個很長的專案名稱用來驗證選單換行與窄視窗";
            var saved = false;
            var editor = new EditorWindow("其他", "", false, () => ["SpotCam", projectName, "其他"],
                (_, _) => saved = true, () => { });
            var root = (FrameworkElement)editor.Content;
            root.Measure(new Size(350, 520));
            root.Arrange(new Rect(0, 0, 350, 520));
            root.UpdateLayout();
            var controls = descendants(root).ToList();
            var textBox = controls.OfType<TextBox>().Single();
            var combo = controls.OfType<ComboBox>().Single();
            check(textBox.Template.FindName("PART_ContentHost", textBox) is ScrollViewer, "TextBox content host missing");
            check(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup, "ComboBox popup missing");
            check(combo.Items.Count == 3 && (string)combo.SelectedItem == "其他", "Project selection lost");
            combo.SelectedItem = projectName;
            textBox.Text = "中文\n多行 **符號**";
            root.UpdateLayout();
            check(textBox.Text.Contains("\n") && !saved, "Editing triggered save or lost multiline content");
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
            Console.WriteLine("PASS Editor template loading, input hosts, project selection, multiline content, title binding, minimum-size conflict layout");
            Console.WriteLine(output);
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
