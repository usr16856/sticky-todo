using System.Text;
using System.Text.RegularExpressions;

namespace StickyTodo.Core;

public enum TodoPriority { NORMAL, HIGH, LOW }

public sealed record TodoItem(string content, bool isCompleted = false, TodoPriority priority = TodoPriority.NORMAL);

public sealed record TodoMatch(string project, int index, bool isCompleted);

public sealed class TodoGroup(string name) {
    public string name { get; } = name;
    public List<TodoItem> items { get; } = [];
}

public sealed class TodoDocument {
    public List<TodoGroup> groups { get; } = [];
    public List<DeviceUsage> devices { get; } = [];
    public bool hasDeviceSection { get; set; }

    public TodoDocument clone() {
        var copy = new TodoDocument();
        copy.devices.AddRange(devices);
        copy.hasDeviceSection = hasDeviceSection;
        foreach (var group in groups) {
            var newGroup = new TodoGroup(group.name);
            newGroup.items.AddRange(group.items);
            copy.groups.Add(newGroup);
        }
        return copy;
    }

    public bool hasSameTodos(TodoDocument other) => groups.Count == other.groups.Count
        && groups.Zip(other.groups).All(pair => pair.First.name == pair.Second.name
            && pair.First.items.SequenceEqual(pair.Second.items));

    public TodoGroup getOrAddGroup(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\n') || name.Contains('\r')) {
            throw new ArgumentException("專案名稱不可空白或包含換行。");
        }
        var existing = groups.FirstOrDefault(group => group.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            return existing;
        }
        var added = new TodoGroup(name);
        groups.Add(added);
        return added;
    }

    public void add(string project, string content, TodoPriority priority = TodoPriority.NORMAL) {
        validateContent(content);
        validatePriority(priority);
        getOrAddGroup(project).items.Add(new TodoItem(normalizeLines(content), priority: priority));
    }

    public void addToProjects(IEnumerable<string> projects, string content, TodoPriority priority = TodoPriority.NORMAL) {
        validateContent(content);
        validatePriority(priority);
        var targets = projects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0) {
            throw new ArgumentException("請至少選擇一個專案。");
        }
        foreach (var project in targets) {
            validateProject(project);
        }
        var item = new TodoItem(normalizeLines(content), priority: priority);
        foreach (var project in targets) {
            getOrAddGroup(project).items.Add(item);
        }
    }

    public IReadOnlyList<TodoMatch> findMatchingItems(string project, int index) {
        var source = groups.Single(group => group.name == project);
        var originalContent = normalizeLines(source.items[index].content);
        return groups.Where(group => group != source)
            .SelectMany(group => group.items.Select((item, itemIndex) => (group, item, itemIndex)))
            .Where(entry => normalizeLines(entry.item.content).Equals(originalContent, StringComparison.Ordinal))
            .Select(entry => new TodoMatch(entry.group.name, entry.itemIndex, entry.item.isCompleted)).ToList();
    }

    public void edit(string oldProject, int index, string newProject, string content,
        TodoPriority? priority = null, bool syncMatchingContent = false) {
        validateContent(content);
        validateProject(newProject);
        var source = groups.Single(group => group.name == oldProject);
        var selectedPriority = priority ?? source.items[index].priority;
        validatePriority(selectedPriority);
        if (syncMatchingContent && !source.name.Equals(newProject, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("同步編輯時不可移動專案，請先取消同步。");
        }
        var matches = syncMatchingContent ? findMatchingItems(oldProject, index) : [];
        var updated = source.items[index] with { content = normalizeLines(content), priority = selectedPriority };
        var target = getOrAddGroup(newProject);
        foreach (var match in matches) {
            var group = groups.Single(group => group.name == match.project);
            group.items[match.index] = group.items[match.index] with { content = updated.content };
        }
        if (source == target) {
            source.items[index] = updated;
        } else {
            source.items.RemoveAt(index);
            target.items.Add(updated);
            if (source.items.Count == 0) {
                groups.Remove(source);
            }
        }
    }

    public void remove(string project, int index) {
        var group = groups.Single(group => group.name == project);
        group.items.RemoveAt(index);
        if (group.items.Count == 0) { groups.Remove(group); }
    }

    public static string normalizeLines(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static void validateContent(string content) {
        if (string.IsNullOrWhiteSpace(content)) {
            throw new ArgumentException("請填寫待辦內容。");
        }
    }

    private static void validatePriority(TodoPriority priority) {
        if (!Enum.IsDefined(priority)) { throw new ArgumentOutOfRangeException(nameof(priority)); }
    }

    private static void validateProject(string project) {
        if (string.IsNullOrWhiteSpace(project) || project.Contains('\n') || project.Contains('\r')) {
            throw new ArgumentException("專案名稱不可空白或包含換行。");
        }
    }
}

public static partial class MarkdownCodec {
    [GeneratedRegex(@"^([1-9][0-9]*)\. \[([ xX])\] (.*)$")]
    private static partial Regex getItemPattern();

    public static string serialize(TodoDocument document) {
        var text = new StringBuilder("# 我的待辦\n");
        if (document.hasDeviceSection || document.devices.Count > 0) {
            text.Append('\n').Append(DeviceMarkdownCodec.serialize(document.devices));
        }
        foreach (var group in document.groups.Where(group => group.items.Count > 0)
                     .OrderBy(group => group.name == "其他" ? 1 : 0)
                     .ThenBy(group => group.name, StringComparer.OrdinalIgnoreCase)) {
            text.Append("\n## ").Append(group.name).Append('\n');
            for (var index = 0; index < group.items.Count; index++) {
                var item = group.items[index];
                var priority = item.priority switch {
                    TodoPriority.HIGH => "high",
                    TodoPriority.LOW => "low",
                    TodoPriority.NORMAL => null,
                    _ => throw new ArgumentOutOfRangeException(nameof(item.priority))
                };
                if (priority != null) {
                    text.Append("<!-- stickytodo:priority=").Append(priority).Append(" -->\n");
                }
                var lines = TodoDocument.normalizeLines(item.content).Split('\n');
                text.Append(index + 1).Append(item.isCompleted ? ". [x] " : ". [ ] ").Append(lines[0]).Append('\n');
                foreach (var line in lines.Skip(1)) {
                    text.Append("    ").Append(line).Append('\n');
                }
            }
        }
        return text.ToString();
    }

    public static TodoDocument parse(string text) {
        var document = new TodoDocument();
        TodoGroup? current = null;
        var hasTitle = false;
        TodoPriority? pendingPriority = null;
        var priorityLine = -1;
        var lines = TodoDocument.normalizeLines(text.TrimStart('\uFEFF')).Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var line = lines[index];
            // Metadata must immediately precede an item, never a continuation or another annotation.
            if (pendingPriority.HasValue && !getItemPattern().IsMatch(line)) { throw invalid(index); }
            if (line == DeviceMarkdownCodec.START) {
                if (document.hasDeviceSection || current != null) { throw invalid(index); }
                var end = Array.IndexOf(lines, DeviceMarkdownCodec.END, index + 1);
                if (end < 0) { throw invalid(index); }
                document.devices.AddRange(DeviceMarkdownCodec.parse(lines[(index + 1)..end]));
                document.hasDeviceSection = true;
                index = end;
                continue;
            }
            if (line.StartsWith("    ", StringComparison.Ordinal)) {
                if (current == null || current.items.Count == 0) {
                    throw invalid(index);
                }
                var last = current.items[^1];
                current.items[^1] = last with { content = last.content + "\n" + line[4..] };
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }
            if (line == "# 我的待辦" && !hasTitle && document.groups.Count == 0) {
                hasTitle = true;
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                var name = line[3..];
                if (string.IsNullOrWhiteSpace(name) || name != name.Trim()
                    || document.groups.Any(group => group.name.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                    throw invalid(index);
                }
                current = document.getOrAddGroup(name);
                continue;
            }
            if (line.StartsWith("<!-- stickytodo:priority=", StringComparison.Ordinal)) {
                if (current == null) { throw invalid(index); }
                pendingPriority = line switch {
                    "<!-- stickytodo:priority=high -->" => TodoPriority.HIGH,
                    "<!-- stickytodo:priority=low -->" => TodoPriority.LOW,
                    _ => throw invalid(index)
                };
                priorityLine = index;
                continue;
            }
            var match = getItemPattern().Match(line);
            if (current == null || !match.Success || !int.TryParse(match.Groups[1].Value, out var number)
                || number != current.items.Count + 1) {
                throw invalid(index);
            }
            current.items.Add(new TodoItem(match.Groups[3].Value, match.Groups[2].Value != " ",
                pendingPriority ?? TodoPriority.NORMAL));
            pendingPriority = null;
        }
        if (pendingPriority.HasValue) { throw invalid(priorityLine); }
        if (document.groups.SelectMany(group => group.items).Any(item => string.IsNullOrWhiteSpace(item.content))) {
            throw new FormatException("待辦內容不可空白。請修正資料檔後重新載入。");
        }
        return document;
    }

    private static FormatException invalid(int index) => new($"資料檔第 {index + 1} 行格式不正確。原檔未修改，請修正後重新載入。");
}
