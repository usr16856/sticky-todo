using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace StickyTodo;

// Shared caption for the main note and owned editor windows.
internal sealed class WindowHeader : Grid {
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private readonly Window window;
    private readonly Button? maximizeButton;
    private readonly Path? maximizeIcon;

    internal WindowHeader(Window window, string? caption = "我的待辦", bool showWindowControls = true) {
        this.window = window;
        Height = 48;
        Background = Theme.paper;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            // The top resize strip is included in the 48-DIP header.
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Theme.label(caption ?? "", 22);
        if (caption == null) {
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        }
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(16, 0, 8, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        Children.Add(title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(controls, 1);
        Children.Add(controls);
        if (showWindowControls) {
            var minimizeButton = createButton("最小化", "M 0,5 L 10,5", false, out _);
            minimizeButton.Click += (_, _) => SystemCommands.MinimizeWindow(window);
            controls.Children.Add(minimizeButton);
            maximizeButton = createButton("最大化", "M 0,0 L 10,0 10,10 0,10 Z", false, out maximizeIcon);
            maximizeButton.Click += (_, _) => {
                if (window.WindowState == WindowState.Maximized) { SystemCommands.RestoreWindow(window); }
                else { SystemCommands.MaximizeWindow(window); }
            };
            controls.Children.Add(maximizeButton);
        }
        var closeButton = createButton(showWindowControls ? "關閉（縮至系統匣）" : "關閉", "M 0,0 L 10,10 M 0,10 L 10,0", true, out _);
        closeButton.Click += (_, _) => SystemCommands.CloseWindow(window);
        controls.Children.Add(closeButton);
        window.StateChanged += (_, _) => updateMaximizeButton();
        window.SourceInitialized += (_, _) => {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(handleWindowMessage);
        };
    }

    private static Button createButton(string name, string geometry, bool isClose, out Path icon) {
        var button = new Button { Width = 40, Height = 48, ToolTip = name, Style = createButtonStyle(isClose) };
        AutomationProperties.SetName(button, name);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        icon = new Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
        var iconCanvas = new Grid { Width = 12, Height = 12, IsHitTestVisible = false };
        iconCanvas.Children.Add(icon);
        button.Content = iconCanvas;
        return button;
    }

    private static Style createButtonStyle(bool isClose) {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(isClose ? Color.FromRgb(196, 43, 28) : Color.FromArgb(22, 56, 49, 34))));
        if (isClose) { hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); }
        style.Triggers.Add(hover);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(isClose ? Color.FromRgb(155, 30, 20) : Color.FromArgb(42, 56, 49, 34))));
        style.Triggers.Add(pressed);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.accent));
        style.Triggers.Add(focused);
        return style;
    }

    private void updateMaximizeButton() {
        if (maximizeButton == null || maximizeIcon == null) { return; }
        var isMaximized = window.WindowState == WindowState.Maximized;
        maximizeIcon.Data = Geometry.Parse(isMaximized
            ? "M 2,0 L 10,0 10,8 M 0,2 L 8,2 8,10 0,10 Z"
            : "M 0,0 L 10,0 10,10 0,10 Z");
        var name = isMaximized ? "還原" : "最大化";
        maximizeButton.ToolTip = name;
        AutomationProperties.SetName(maximizeButton, name);
    }

    private IntPtr handleWindowMessage(IntPtr handle, int message, IntPtr parameter, IntPtr data, ref bool handled) {
        if (message != WM_GETMINMAXINFO) { return IntPtr.Zero; }
        var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) { return IntPtr.Zero; }
        var limits = Marshal.PtrToStructure<MinMaxInfo>(data);
        // Native coordinates are physical pixels, including monitors left of the primary.
        limits.maxPosition.x = info.work.left - info.monitor.left;
        limits.maxPosition.y = info.work.top - info.monitor.top;
        limits.maxSize.x = info.work.right - info.work.left;
        limits.maxSize.y = info.work.bottom - info.work.top;
        var dpiScale = GetDpiForWindow(handle) / 96.0;
        limits.minTrackSize.x = Math.Max(limits.minTrackSize.x, (int)Math.Ceiling(window.MinWidth * dpiScale));
        limits.minTrackSize.y = Math.Max(limits.minTrackSize.y, (int)Math.Ceiling(window.MinHeight * dpiScale));
        Marshal.StructureToPtr(limits, data, false);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int left; public int top; public int right; public int bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo {
        public NativePoint reserved;
        public NativePoint maxSize;
        public NativePoint maxPosition;
        public NativePoint minTrackSize;
        public NativePoint maxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo {
        public int size;
        public NativeRect monitor;
        public NativeRect work;
        public uint flags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
