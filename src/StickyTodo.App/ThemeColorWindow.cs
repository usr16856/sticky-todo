using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StickyTodo.Core;

namespace StickyTodo;

// Custom theme color picker; previews live and restores the original color unless applied.
internal sealed class ThemeColorWindow : Window {
    private readonly string originalColor;
    private readonly ColorField field = new() { Height = 170 };
    private readonly Slider hueSlider = new() { Minimum = 0, Maximum = 360, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBox hexInput = new() { MaxLength = 7, CharacterCasing = CharacterCasing.Upper, Padding = new Thickness(10, 8, 10, 8),
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI"), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock hexError = new() { Text = "請輸入 #RRGGBB 格式的色碼", FontSize = 12, Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 6, 0, 0) };
    private readonly Border newPreview = new() { Width = 44, CornerRadius = new CornerRadius(0, 8, 8, 0), BorderThickness = new Thickness(1) };
    private readonly Button applyButton = new() { Content = "套用", IsDefault = true, MinWidth = 88 };
    private readonly List<(RadioButton swatch, string color)> swatches = [];
    private readonly StackPanel recentSection = new();
    private ThemeColor color;
    private double hue;
    private bool isSyncing;
    private bool isApplied;
    private string? pendingPreview;
    private DispatcherOperation? previewOperation;

    internal string? selectedColor { get; private set; }
    internal string currentColor => color.toHex();
    internal bool isRecentVisible => recentSection.Visibility == Visibility.Visible;

    internal ThemeColorWindow(Window owner, string currentColor, IReadOnlyList<string> recentColors) {
        originalColor = ThemePalette.normalize(currentColor);
        Title = "自訂主題色";
        Width = 400;
        MinWidth = 360;
        SizeToContent = SizeToContent.Height;
        if (owner.IsLoaded) {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        } else {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        ShowInTaskbar = false;
        Theme.usePaper(this);
        FontFamily = owner.FontFamily;
        FontSize = 14;

        var frame = new DockPanel();
        frame.SetResourceReference(Panel.BackgroundProperty, Theme.PAPER);
        var caption = new WindowHeader(this, caption: null, showWindowControls: false);
        DockPanel.SetDock(caption, Dock.Top);
        frame.Children.Add(caption);
        var body = new StackPanel { Margin = new Thickness(18, 4, 18, 18) };

        var hint = Theme.label("拖曳選擇便條紙底色，文字顏色會自動搭配。", 12);
        hint.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        hint.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(hint);

        field.SetResourceReference(ColorField.StrokeProperty, "ControlBorder");
        field.SetResourceReference(ColorField.FocusStrokeProperty, Theme.ACCENT);
        AutomationProperties.SetName(field, "飽和度與明度");
        AutomationProperties.SetHelpText(field, "左右鍵調整飽和度，上下鍵調整明度，按住 Shift 加大步距");
        field.changed += (saturation, value) => { if (!isSyncing) { setFromHsv(hue, saturation, value); } };
        body.Children.Add(field);

        hueSlider.SetResourceReference(StyleProperty, "HueSlider");
        AutomationProperties.SetName(hueSlider, "色相");
        hueSlider.ValueChanged += (_, _) => {
            if (!isSyncing) { setFromHsv(hueSlider.Value, field.saturation, field.value); }
        };
        body.Children.Add(hueSlider);

        var hexRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var compare = new StackPanel { Orientation = Orientation.Horizontal, Height = 38, Margin = new Thickness(0, 0, 12, 0) };
        ThemeColor.tryParse(originalColor, out var original);
        var originalButton = new Button { Width = 44, Background = new SolidColorBrush(Theme.toColor(original)),
            ToolTip = $"原本 {originalColor}（點一下還原）" };
        originalButton.SetResourceReference(StyleProperty, "OriginalColorButton");
        AutomationProperties.SetName(originalButton, "還原為原本的顏色 " + originalColor);
        originalButton.Click += (_, _) => setFromColor(original, null);
        compare.Children.Add(originalButton);
        newPreview.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
        newPreview.ToolTip = "新的顏色";
        compare.Children.Add(newPreview);
        hexRow.Children.Add(compare);
        AutomationProperties.SetName(hexInput, "色碼");
        hexInput.ToolTip = "輸入 #RRGGBB、RRGGBB 或 #RGB";
        hexInput.TextChanged += (_, _) => onHexChanged();
        hexInput.LostKeyboardFocus += (_, _) => { if (applyButton.IsEnabled) { setHexText(color.toHex()); } };
        Grid.SetColumn(hexInput, 1);
        hexRow.Children.Add(hexInput);
        body.Children.Add(hexRow);
        hexError.SetResourceReference(TextBlock.ForegroundProperty, Theme.ERROR);
        body.Children.Add(hexError);

        body.Children.Add(createSection("建議色", ThemePalette.suggestions));
        var recent = ThemePalette.normalizeRecent(recentColors);
        recentSection.Children.Add(createSection("最近使用", recent));
        recentSection.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        body.Children.Add(recentSection);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 76 };
        cancel.Click += (_, _) => DialogResult = false;
        applyButton.SetResourceReference(StyleProperty, "PrimaryButton");
        applyButton.Click += (_, _) => apply();
        actions.Children.Add(cancel);
        actions.Children.Add(applyButton);
        body.Children.Add(actions);

        frame.Children.Add(body);
        Content = frame;
        setFromColor(original, null);
        Closed += (_, _) => {
            previewOperation?.Abort();
            if (!isApplied) { Theme.apply(originalColor); }
        };
        Loaded += (_, _) => field.Focus();
        if (Owner != null && tryPlaceBesideOwner(owner, frame)) { WindowStartupLocation = WindowStartupLocation.Manual; }
    }

    // Keep the note visible during live preview: open beside it when the owner's screen has room.
    private bool tryPlaceBesideOwner(Window owner, FrameworkElement content) {
        if (owner.WindowState != WindowState.Normal) { return false; }
        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget == null) { return false; }
        content.Measure(new Size(Width, double.PositiveInfinity));
        var height = content.DesiredSize.Height;
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle).WorkingArea;
        var toDip = source.CompositionTarget.TransformFromDevice;
        var topLeft = toDip.Transform(new Point(screen.Left, screen.Top));
        var bottomRight = toDip.Transform(new Point(screen.Right, screen.Bottom));
        const double GAP = 12;
        var right = owner.Left + owner.ActualWidth + GAP;
        var left = owner.Left - GAP - Width;
        if (right + Width <= bottomRight.X) { Left = right; }
        else if (left >= topLeft.X) { Left = left; }
        else { return false; }
        Top = Math.Max(topLeft.Y, Math.Min(owner.Top, bottomRight.Y - height));
        return true;
    }

    internal void setHex(string text) => hexInput.Text = text;

    internal void selectColor(string hex) {
        if (ThemeColor.tryParseLoose(hex, out var parsed)) { setFromColor(parsed, null); }
    }

    internal void apply() {
        if (!applyButton.IsEnabled) { return; }
        isApplied = true;
        previewOperation?.Abort();
        selectedColor = color.toHex();
        DialogResult = true;
    }

    private StackPanel createSection(string title, IReadOnlyList<string> colors) {
        var section = new StackPanel();
        var heading = Theme.label(title, 12);
        heading.FontWeight = FontWeights.SemiBold;
        heading.SetResourceReference(TextBlock.ForegroundProperty, Theme.MUTED);
        heading.Margin = new Thickness(0, 16, 0, 8);
        section.Children.Add(heading);
        var grid = new WrapPanel();
        foreach (var hex in colors) {
            ThemeColor.tryParse(hex, out var parsed);
            var swatch = new RadioButton { Background = new SolidColorBrush(Theme.toColor(parsed)), ToolTip = hex, GroupName = "theme-" + title };
            swatch.SetResourceReference(StyleProperty, "SwatchButton");
            AutomationProperties.SetName(swatch, $"{title} {hex}");
            swatch.Click += (_, _) => setFromColor(parsed, null);
            swatches.Add((swatch, hex));
            grid.Children.Add(swatch);
        }
        section.Children.Add(grid);
        return section;
    }

    private void onHexChanged() {
        if (isSyncing) { return; }
        if (ThemeColor.tryParseLoose(hexInput.Text, out var parsed)) {
            setFromColor(parsed, hexInput);
        } else {
            hexInput.SetResourceReference(BorderBrushProperty, Theme.ERROR);
            hexError.Visibility = Visibility.Visible;
            applyButton.IsEnabled = false;
        }
    }

    private void setFromHsv(double newHue, double saturation, double value) {
        hue = newHue;
        color = ThemeColor.fromHsv(newHue, saturation, value);
        sync(saturation, value, null);
    }

    private void setFromColor(ThemeColor newColor, object? source) {
        color = newColor;
        var (newHue, saturation, value) = newColor.toHsv();
        // Grays have no hue; keep the current one so the field does not jump to red.
        if (saturation > 0 && value > 0) { hue = newHue; }
        sync(saturation, value, source);
    }

    private void sync(double saturation, double value, object? source) {
        isSyncing = true;
        try {
            field.setColor(hue, saturation, value);
            hueSlider.Value = hue;
            if (source != hexInput) { setHexText(color.toHex()); }
            hexInput.ClearValue(BorderBrushProperty);
            hexError.Visibility = Visibility.Collapsed;
            applyButton.IsEnabled = true;
            newPreview.Background = new SolidColorBrush(Theme.toColor(color));
            var hex = color.toHex();
            foreach (var (swatch, swatchColor) in swatches) { swatch.IsChecked = swatchColor == hex; }
        } finally {
            isSyncing = false;
        }
        schedulePreview(color.toHex());
    }

    private void setHexText(string text) {
        var wasSyncing = isSyncing;
        isSyncing = true;
        hexInput.Text = text;
        isSyncing = wasSyncing;
    }

    // Coalesce rapid drag updates into one theme refresh after pending input is processed.
    private void schedulePreview(string hex) {
        pendingPreview = hex;
        if (previewOperation is { Status: DispatcherOperationStatus.Pending }) { return; }
        previewOperation = Dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
            if (!isApplied && pendingPreview != null && IsLoaded) { Theme.apply(pendingPreview); }
        });
    }
}

// Saturation (x) / value (y) plane for the current hue.
internal sealed class ColorField : FrameworkElement {
    internal static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(ColorField),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty FocusStrokeProperty = DependencyProperty.Register("FocusStroke", typeof(Brush), typeof(ColorField),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    private const double RADIUS = 8;
    private static readonly Brush whiteFade = frozen(new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0));
    private static readonly Brush blackFade = frozen(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90));
    private static readonly Pen thumbOuter = frozen(new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 1));
    private static readonly Pen thumbInner = frozen(new Pen(Brushes.White, 3));
    private double hue;

    internal double saturation { get; private set; }
    internal double value { get; private set; } = 1;
    internal event Action<double, double>? changed;

    internal ColorField() {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
    }

    internal void setColor(double newHue, double newSaturation, double newValue) {
        hue = newHue;
        saturation = Math.Clamp(newSaturation, 0, 1);
        value = Math.Clamp(newValue, 0, 1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context) {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) { return; }
        var shape = new RectangleGeometry(bounds, RADIUS, RADIUS);
        context.PushClip(shape);
        context.DrawRectangle(new SolidColorBrush(Theme.toColor(ThemeColor.fromHsv(hue, 1, 1))), null, bounds);
        context.DrawRectangle(whiteFade, null, bounds);
        context.DrawRectangle(blackFade, null, bounds);
        context.Pop();
        var isFocused = IsKeyboardFocused;
        var border = new Pen(isFocused ? (Brush)GetValue(FocusStrokeProperty) : (Brush)GetValue(StrokeProperty), isFocused ? 2 : 1);
        context.DrawRoundedRectangle(null, border, new Rect(bounds.X + border.Thickness / 2, bounds.Y + border.Thickness / 2,
            bounds.Width - border.Thickness, bounds.Height - border.Thickness), RADIUS, RADIUS);
        var center = new Point(saturation * bounds.Width, (1 - value) * bounds.Height);
        context.DrawEllipse(new SolidColorBrush(Theme.toColor(ThemeColor.fromHsv(hue, saturation, value))), thumbInner, center, 8, 8);
        context.DrawEllipse(null, thumbOuter, center, 10, 10);
    }

    protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs args) {
        base.OnIsKeyboardFocusedChanged(args);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args) {
        base.OnMouseLeftButtonDown(args);
        Focus();
        CaptureMouse();
        pick(args.GetPosition(this));
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args) {
        base.OnMouseMove(args);
        if (IsMouseCaptured) { pick(args.GetPosition(this)); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args) {
        base.OnMouseLeftButtonUp(args);
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs args) {
        base.OnKeyDown(args);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        var (deltaSaturation, deltaValue) = args.Key switch {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, step),
            Key.Down => (0.0, -step),
            _ => (0.0, 0.0)
        };
        if (deltaSaturation == 0 && deltaValue == 0) { return; }
        update(saturation + deltaSaturation, value + deltaValue);
        args.Handled = true;
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ColorFieldAutomationPeer(this);

    internal string hex => ThemeColor.fromHsv(hue, saturation, value).toHex();

    private void pick(Point point) {
        if (ActualWidth <= 0 || ActualHeight <= 0) { return; }
        update(point.X / ActualWidth, 1 - point.Y / ActualHeight);
    }

    private void update(double newSaturation, double newValue) {
        setColor(hue, newSaturation, newValue);
        changed?.Invoke(saturation, value);
    }

    private static T frozen<T>(T freezable) where T : Freezable {
        freezable.Freeze();
        return freezable;
    }
}

// Exposes the field to screen readers and UI Automation with its current color as a read-only value.
internal sealed class ColorFieldAutomationPeer(ColorField owner)
    : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner), System.Windows.Automation.Provider.IValueProvider {
    protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
        System.Windows.Automation.Peers.AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(ColorField);

    public override object GetPattern(System.Windows.Automation.Peers.PatternInterface pattern) =>
        pattern == System.Windows.Automation.Peers.PatternInterface.Value ? this : base.GetPattern(pattern);

    public string Value => owner.hex;

    public bool IsReadOnly => true;

    public void SetValue(string value) => throw new InvalidOperationException("請使用方向鍵或色碼欄位調整顏色。");
}
