using System.Text.Json;

namespace StickyTodo.Core;

public sealed class AppSettings {
    public double width { get; set; } = 380;
    public double height { get; set; } = 520;
    public double? left { get; set; }
    public double? top { get; set; }
    public bool isTopmost { get; set; }
    public bool hideCompleted { get; set; }
    public double windowOpacity { get; set; } = 1.0;
    public string lastProject { get; set; } = "其他";
    public List<string> lastProjects { get; set; } = [];
    public List<string> collapsedProjects { get; set; } = [];
}

public sealed class SettingsStore(string filePath) {
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public AppSettings read() {
        if (!File.Exists(filePath)) {
            return new AppSettings();
        }
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath))
            ?? throw new FormatException("設定檔內容無效。");
        if (!double.IsFinite(settings.windowOpacity) || !double.IsFinite(settings.width) || !double.IsFinite(settings.height)
            || (settings.left.HasValue && !double.IsFinite(settings.left.Value))
            || (settings.top.HasValue && !double.IsFinite(settings.top.Value))
            || settings.collapsedProjects == null || settings.lastProject == null || settings.lastProjects == null) {
            throw new FormatException("設定檔內容無效。");
        }
        settings.windowOpacity = Math.Clamp(settings.windowOpacity, 0.1, 1.0);
        settings.lastProjects = settings.lastProjects
            .Where(project => !string.IsNullOrWhiteSpace(project) && !project.Contains('\n') && !project.Contains('\r'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (settings.lastProjects.Count == 0) {
            settings.lastProjects.Add(string.IsNullOrWhiteSpace(settings.lastProject) ? "其他" : settings.lastProject);
        }
        settings.lastProject = settings.lastProjects[0];
        return settings;
    }

    public void save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, jsonOptions));
            if (File.Exists(filePath)) {
                File.Replace(temporaryPath, filePath, filePath + ".bak", true);
            } else {
                File.Move(temporaryPath, filePath);
            }
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }
}
