using System.Text;
using System.Text.RegularExpressions;

namespace StickyTodo.Core;

public sealed record TodoItem(string content, bool isCompleted = false);

public sealed class TodoGroup(string name) {
    public string name { get; } = name;
    public List<TodoItem> items { get; } = [];
}

public sealed class TodoDocument {
    public List<TodoGroup> groups { get; } = [];

    public TodoDocument clone() {
        var copy = new TodoDocument();
        foreach (var group in groups) {
            var newGroup = new TodoGroup(group.name);
            newGroup.items.AddRange(group.items);
            copy.groups.Add(newGroup);
        }
        return copy;
    }

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

    public void add(string project, string content) {
        validateContent(content);
        getOrAddGroup(project).items.Add(new TodoItem(normalizeLines(content)));
    }

    public void edit(string oldProject, int index, string newProject, string content) {
        validateContent(content);
        var source = groups.Single(group => group.name == oldProject);
        var updated = source.items[index] with { content = normalizeLines(content) };
        var target = getOrAddGroup(newProject);
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
}

public static partial class MarkdownCodec {
    [GeneratedRegex(@"^([1-9][0-9]*)\. \[([ xX])\] (.*)$")]
    private static partial Regex getItemPattern();

    public static string serialize(TodoDocument document) {
        var text = new StringBuilder("# 我的待辦\n");
        foreach (var group in document.groups.Where(group => group.items.Count > 0)
                     .OrderBy(group => group.name == "其他" ? 1 : 0)
                     .ThenBy(group => group.name, StringComparer.OrdinalIgnoreCase)) {
            text.Append("\n## ").Append(group.name).Append('\n');
            for (var index = 0; index < group.items.Count; index++) {
                var item = group.items[index];
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
        var lines = TodoDocument.normalizeLines(text.TrimStart('\uFEFF')).Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var line = lines[index];
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
            var match = getItemPattern().Match(line);
            if (current == null || !match.Success || !int.TryParse(match.Groups[1].Value, out var number)
                || number != current.items.Count + 1) {
                throw invalid(index);
            }
            current.items.Add(new TodoItem(match.Groups[3].Value, match.Groups[2].Value != " "));
        }
        if (document.groups.SelectMany(group => group.items).Any(item => string.IsNullOrWhiteSpace(item.content))) {
            throw new FormatException("待辦內容不可空白。請修正資料檔後重新載入。");
        }
        return document;
    }

    private static FormatException invalid(int index) => new($"資料檔第 {index + 1} 行格式不正確。原檔未修改，請修正後重新載入。");
}
