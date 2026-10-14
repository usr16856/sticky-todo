using System.Globalization;

namespace StickyTodo.Core;

public readonly record struct ThemeColor(byte r, byte g, byte b, byte a = 255) {
    public string toHex() => $"#{r:X2}{g:X2}{b:X2}";

    public ThemeColor withAlpha(byte alpha) => this with { a = alpha };

    public static bool tryParse(string? text, out ThemeColor color) {
        color = default;
        if (text == null || text.Length != 7 || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) {
            return false;
        }
        color = new ThemeColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    // Accepts user-typed hex: optional '#', and the 3-digit shorthand.
    public static bool tryParseLoose(string? text, out ThemeColor color) {
        var trimmed = (text ?? "").Trim().TrimStart('#');
        if (trimmed.Length == 3) {
            trimmed = string.Concat(trimmed.Select(digit => new string(digit, 2)));
        }
        return tryParse("#" + trimmed, out color);
    }

    public (double hue, double saturation, double value) toHsv() {
        var red = r / 255.0;
        var green = g / 255.0;
        var blue = b / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var delta = max - Math.Min(red, Math.Min(green, blue));
        if (delta == 0) { return (0, 0, max); }
        var hue = max == red ? (green - blue) / delta + (green < blue ? 6 : 0)
            : max == green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
        return (hue * 60, delta / max, max);
    }

    public static ThemeColor fromHsv(double hue, double saturation, double value) {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation;
        var segment = hue / 60;
        var x = chroma * (1 - Math.Abs(segment % 2 - 1));
        var (red, green, blue) = segment switch {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        var m = value - chroma;
        return new ThemeColor((byte)Math.Round((red + m) * 255), (byte)Math.Round((green + m) * 255), (byte)Math.Round((blue + m) * 255));
    }

    public double luminance() {
        static double channel(byte value) {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
    }

    public static double contrast(ThemeColor first, ThemeColor second) {
        var light = Math.Max(first.luminance(), second.luminance());
        var dark = Math.Min(first.luminance(), second.luminance());
        return (light + 0.05) / (dark + 0.05);
    }
}

public sealed record ThemePreset(string name, string color);

public sealed record ThemePalette(
    bool isDark,
    ThemeColor paper,
    ThemeColor inputSurface,
    ThemeColor inputHover,
    ThemeColor hoverSurface,
    ThemeColor pressedSurface,
    ThemeColor border,
    ThemeColor ink,
    ThemeColor muted,
    ThemeColor accent,
    ThemeColor accentHover,
    ThemeColor accentPressed,
    ThemeColor accentForeground,
    ThemeColor accentFocus,
    ThemeColor captionHover,
    ThemeColor captionPressed,
    ThemeColor errorText) {
    public const string DEFAULT_COLOR = "#FFF9DA";

    public static readonly IReadOnlyList<ThemePreset> presets = [
        new("便條黃", DEFAULT_COLOR),
        new("櫻花粉", "#FDE4EC"),
        new("天空藍", "#DFF0FB"),
        new("薄荷綠", "#DDF5E6"),
        new("薰衣草", "#ECE4FA"),
        new("雲朵灰", "#F2F2EF"),
        new("夜間", "#2B2D33")
    ];

    public static readonly IReadOnlyList<string> suggestions = [
        "#FFF9DA", "#FFE9C7", "#FFDCCF", "#FDE4EC", "#F3E1F7", "#ECE4FA", "#DFF0FB", "#DDF5E6",
        "#E6F4D7", "#D9F2F0", "#E4E9F2", "#F2F2EF", "#EFE6DA", "#2B2D33", "#1E3C5A", "#2F3B2F"
    ];

    private static readonly ThemePalette classic = new(
        false,
        new(0xFF, 0xF9, 0xDA), new(0xFF, 0xFC, 0xF0), new(0xFF, 0xF8, 0xE4), new(0xF3, 0xEA, 0xC2), new(0xE8, 0xDB, 0xA8),
        new(0xB7, 0xA6, 0x79), new(0x38, 0x31, 0x22), new(0x74, 0x67, 0x4A), new(0x6E, 0x56, 0x24), new(0x80, 0x66, 0x2D),
        new(0x57, 0x43, 0x1A), new(0xFF, 0xFF, 0xFF), new(0xFF, 0xF4, 0xCA), new(0x38, 0x31, 0x22, 22), new(0x38, 0x31, 0x22, 42),
        new(0xB2, 0x22, 0x22));

    public static string normalize(string? color) =>
        ThemeColor.tryParse(color, out var parsed) ? parsed.toHex() : DEFAULT_COLOR;

    public const int MAX_RECENT_COLORS = 8;

    public static List<string> normalizeRecent(IEnumerable<string?>? colors) => (colors ?? [])
        .Select(color => ThemeColor.tryParse(color, out var parsed) ? parsed.toHex() : null)
        .OfType<string>().Distinct().Take(MAX_RECENT_COLORS).ToList();

    public static List<string> addRecent(IEnumerable<string?>? colors, string color) =>
        normalizeRecent(new[] { normalize(color) }.Concat(colors ?? []));

    public static ThemePalette from(string? color) {
        var hex = normalize(color);
        if (hex == DEFAULT_COLOR) { return classic; }
        ThemeColor.tryParse(hex, out var paper);
        var (hue, saturation, lightness) = toHsl(paper);
        var white = new ThemeColor(255, 255, 255);
        var black = new ThemeColor(0, 0, 0);
        var isDark = ThemeColor.contrast(paper, white) > ThemeColor.contrast(paper, black);
        if (!isDark) {
            var ink = ensureContrast(fromHsl(hue, 0.24 * saturation, Math.Clamp(lightness - 0.75, 0.08, 0.18)), paper, 4.5, black);
            var accentLightness = Math.Clamp(lightness - 0.64, 0.14, 0.29);
            return new ThemePalette(
                false,
                paper,
                fromHsl(hue, saturation, lightness + (1 - lightness) * 0.6),
                fromHsl(hue, saturation, lightness + (1 - lightness) * 0.27),
                fromHsl(hue, 0.74 * saturation, Math.Max(lightness - 0.07, 0)),
                fromHsl(hue, 0.61 * saturation, Math.Max(lightness - 0.145, 0)),
                fromHsl(hue, 0.31 * saturation, Math.Clamp(lightness - 0.33, 0.25, 0.6)),
                ink,
                ensureContrast(mix(ink, paper, 0.3), paper, 3, black),
                ensureContrast(fromHsl(hue, 0.51 * saturation, accentLightness), white, 4.5, black),
                ensureContrast(fromHsl(hue, 0.48 * saturation, accentLightness + 0.05), white, 4.5, black),
                ensureContrast(fromHsl(hue, 0.54 * saturation, Math.Max(accentLightness - 0.07, 0.06)), white, 4.5, black),
                white,
                fromHsl(hue, saturation, lightness + (1 - lightness) * 0.5),
                ink.withAlpha(22),
                ink.withAlpha(42),
                readable(new ThemeColor(0xB2, 0x22, 0x22), ink, paper));
        }
        var darkInk = ensureContrast(fromHsl(hue, 0.2 * saturation, 0.93), paper, 4.5, white);
        var darkAccentLightness = Math.Clamp(lightness + 0.55, 0.68, 0.76);
        // Near-gray dark backgrounds still get a gently tinted accent so focus and selection remain visible.
        var accentSaturation = saturation < 0.02 ? 0 : Math.Max(0.45, 0.6 * saturation);
        var accentForeground = fromHsl(hue, 0.3 * saturation, 0.12);
        return new ThemePalette(
            true,
            paper,
            fromHsl(hue, saturation, Math.Min(lightness + 0.05, 1)),
            fromHsl(hue, saturation, Math.Min(lightness + 0.08, 1)),
            fromHsl(hue, saturation, Math.Min(lightness + 0.1, 1)),
            fromHsl(hue, saturation, Math.Min(lightness + 0.16, 1)),
            fromHsl(hue, 0.6 * saturation, Math.Min(lightness + 0.32, 1)),
            darkInk,
            ensureContrast(mix(darkInk, paper, 0.3), paper, 3, white),
            ensureContrast(fromHsl(hue, accentSaturation, darkAccentLightness), accentForeground, 4.5, white),
            ensureContrast(fromHsl(hue, accentSaturation, darkAccentLightness + 0.06), accentForeground, 4.5, white),
            ensureContrast(fromHsl(hue, accentSaturation, darkAccentLightness - 0.08), accentForeground, 4.5, white),
            accentForeground,
            accentForeground,
            darkInk.withAlpha(26),
            darkInk.withAlpha(48),
            readable(new ThemeColor(0xFF, 0x8A, 0x80), darkInk, paper));
    }

    private static ThemeColor ensureContrast(ThemeColor color, ThemeColor background, double target, ThemeColor extreme) {
        for (var step = 0; step <= 20; step++) {
            var candidate = mix(color, extreme, step / 20.0);
            if (ThemeColor.contrast(candidate, background) >= target) { return candidate; }
        }
        return extreme;
    }

    private static ThemeColor readable(ThemeColor preferred, ThemeColor fallback, ThemeColor background) =>
        ThemeColor.contrast(preferred, background) >= 3 ? preferred : fallback;

    private static ThemeColor mix(ThemeColor from, ThemeColor to, double amount) => new(
        (byte)Math.Round(from.r + (to.r - from.r) * amount),
        (byte)Math.Round(from.g + (to.g - from.g) * amount),
        (byte)Math.Round(from.b + (to.b - from.b) * amount));

    private static (double hue, double saturation, double lightness) toHsl(ThemeColor color) {
        var r = color.r / 255.0;
        var g = color.g / 255.0;
        var b = color.b / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min) { return (0, 0, lightness); }
        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == r ? (g - b) / delta + (g < b ? 6 : 0) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return (hue * 60, saturation, lightness);
    }

    private static ThemeColor fromHsl(double hue, double saturation, double lightness) {
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var segment = hue / 60 % 6;
        var x = chroma * (1 - Math.Abs(segment % 2 - 1));
        var (r, g, b) = segment switch {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        var m = lightness - chroma / 2;
        return new ThemeColor(
            (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
