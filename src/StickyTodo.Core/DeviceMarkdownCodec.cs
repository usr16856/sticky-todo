using System.Globalization;
using System.Text;

namespace StickyTodo.Core;

public static class DeviceMarkdownCodec {
    public const string START = "<!-- stickytodo:devices:v1:start -->";
    public const string END = "<!-- stickytodo:devices:v1:end -->";
    private const string HEADING = "## 測試機使用狀態";
    private const string HEADER = "| 裝置序號 | 裝置名稱 | 狀態 | 使用者 | 用途 | 更新時間 |";
    private const string SEPARATOR = "| --- | --- | --- | --- | --- | --- |";

    public static string serialize(IEnumerable<DeviceUsage> devices) {
        var text = new StringBuilder().AppendLine(START).AppendLine(HEADING).AppendLine().AppendLine(HEADER).AppendLine(SEPARATOR);
        var serials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in devices.OrderBy(device => device.serial, StringComparer.Ordinal)) {
            device.validate();
            if (!serials.Add(device.serial)) { throw new FormatException("測試機序號重複。"); }
            var cells = new[] { device.serial, device.name, device.stateName, device.user, device.purpose,
                device.updatedAt.ToString("o", CultureInfo.InvariantCulture) };
            text.Append("| ").Append(string.Join(" | ", cells.Select(escape))).AppendLine(" |");
        }
        return TodoDocument.normalizeLines(text.AppendLine(END).ToString());
    }

    public static List<DeviceUsage> parse(IReadOnlyList<string> lines) {
        var content = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (content.Count < 3 || content[0] != HEADING || content[1] != HEADER || content[2] != SEPARATOR) {
            throw new FormatException("測試機表格標題或欄位格式不正確。");
        }
        var devices = new List<DeviceUsage>();
        foreach (var line in content.Skip(3)) {
            var cells = splitRow(line);
            if (cells.Count != 6 || !DateTimeOffset.TryParseExact(cells[5],
                    ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updatedAt)) {
                throw new FormatException("測試機表格須有六欄，更新時間須為含時區的 ISO 8601 格式。");
            }
            var state = cells[2] switch {
                "未標記" => DeviceUsageState.UNMARKED, "閒置" => DeviceUsageState.IDLE, "使用中" => DeviceUsageState.IN_USE,
                _ => throw new FormatException("測試機使用狀態只接受未標記、閒置或使用中。")
            };
            var device = new DeviceUsage(cells[0], cells[1], state, cells[3], cells[4], updatedAt);
            device.validate();
            if (devices.Any(existing => existing.serial == device.serial)) { throw new FormatException("測試機序號重複。"); }
            devices.Add(device);
        }
        return devices;
    }

    // Device-only saves preserve every byte of the task text, including its newline convention and BOM.
    public static string replaceSection(string original, IEnumerable<DeviceUsage> devices) {
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : original.Contains('\r') ? "\r" : "\n";
        var section = serialize(devices).Replace("\n", newline);
        var lines = TodoDocument.normalizeLines(original).Split('\n');
        var startLine = Array.IndexOf(lines, START);
        if (startLine >= 0) {
            var endLine = Array.IndexOf(lines, END, startLine + 1);
            if (endLine < 0) { throw new FormatException("測試機區塊缺少結束標記。"); }
            var start = getLineOffset(original, startLine);
            var end = getLineOffset(original, endLine + 1);
            return original[..start] + section + original[end..];
        }
        var titleEnd = original.IndexOfAny(['\r', '\n']);
        if (original.TrimStart('\uFEFF').StartsWith("# 我的待辦", StringComparison.Ordinal)) {
            if (titleEnd < 0) { return original + newline + newline + section; }
            var afterTitle = getLineOffset(original, 1);
            return original[..afterTitle] + newline + section + original[afterTitle..];
        }
        var bomLength = original.StartsWith('\uFEFF') ? 1 : 0;
        return original[..bomLength] + section + newline + original[bomLength..];
    }

    private static int getLineOffset(string text, int line) {
        var position = 0;
        for (var index = 0; index < line; index++) {
            var next = text.IndexOfAny(['\r', '\n'], position);
            if (next < 0) { return text.Length; }
            position = next + (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1);
        }
        return position;
    }

    private static string escape(string value) => value.Replace("\\", "\\\\").Replace("|", "\\|");

    private static List<string> splitRow(string line) {
        if (!line.StartsWith('|') || !line.EndsWith('|')) { throw new FormatException("測試機表格列格式不正確。"); }
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var index = 1; index < line.Length - 1; index++) {
            var character = line[index];
            if (character == '\\' && index + 1 < line.Length - 1 && line[index + 1] is '\\' or '|') {
                cell.Append(line[++index]);
            } else if (character == '|') {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            } else { cell.Append(character); }
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }
}
