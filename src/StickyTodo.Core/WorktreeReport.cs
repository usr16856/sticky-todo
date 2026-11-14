using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StickyTodo.Core;

// Read-only view of the agent-rules worktree report; nothing here removes, prunes or registers worktrees.
public static class WorktreeStatuses {
    public const string PRIMARY = "primary";
    private static readonly string[] orderedStatuses =
        ["risk", "likely-done", "broken", "stale-registry", "unlisted", "unscanned", "active", "keep", "locked"];
    public static IReadOnlyList<string> order => orderedStatuses;
    private static readonly HashSet<string> alwaysShown = new(StringComparer.Ordinal) { "risk", "likely-done", "broken", "active", "keep" };
    private static readonly Dictionary<string, string> names = new(StringComparer.Ordinal) {
        ["risk"] = "有風險", ["likely-done"] = "可能已完成", ["broken"] = "損壞", ["stale-registry"] = "登記殘留",
        ["unlisted"] = "Git 未列出", ["unscanned"] = "未盤點", ["active"] = "使用中", ["keep"] = "保留", ["locked"] = "鎖定"
    };

    public static int getRank(string status) {
        var index = Array.IndexOf(orderedStatuses, status);
        return index >= 0 ? index : orderedStatuses.Length;
    }

    public static string getName(string status) => names.GetValueOrDefault(status)
        ?? (string.IsNullOrWhiteSpace(status) ? "未知狀態" : status);

    public static bool isAlwaysShown(string status) => alwaysShown.Contains(status);
}

public sealed record WorktreeEntry(string status, string project, string path, string branch, string head, bool isDetached,
    string owner, string purpose, string keepReason, double? idleDays, long? sizeBytes, IReadOnlyList<string> warnings,
    bool isBroken = false, string manager = "") {
    private const double BYTES_PER_GIB = 1024d * 1024 * 1024;

    public string statusName => WorktreeStatuses.getName(status);

    // App-managed worktrees are retired through their app, not by Git removal; registry-only rows carry no manager.
    public string managerText => manager switch {
        "codex-app" => "Codex app 管理",
        "claude-app" => "Claude app 管理",
        "git" => "Git 管理",
        "" => "管理者未知",
        _ => "管理：" + manager
    };

    public string branchText => !string.IsNullOrWhiteSpace(branch) ? branch
        : head.Length >= 7 ? (isDetached ? "detached " : "") + head[..7] : "（無分支資訊）";

    public string idleText => idleDays is double days ? $"閒置 {days.ToString("0.#", CultureInfo.InvariantCulture)} 天" : "閒置未知";

    // The generator skips measuring broken worktrees and still writes sizeBytes 0.
    public string sizeText => isBroken || status == "broken" ? "大小未量測" : sizeBytes is long bytes ? $"{(bytes / BYTES_PER_GIB).ToString("0.00", CultureInfo.InvariantCulture)} GiB" : "大小未知";
}

public sealed record WorktreeReportGap(string project, string path, string reason);

public sealed record WorktreeReport(int? schemaVersion, DateTimeOffset? createdAt, IReadOnlyList<WorktreeEntry> worktrees,
    IReadOnlyList<WorktreeReportGap> gaps, IReadOnlyList<string> writeFailures) {
    public const int SUPPORTED_SCHEMA_VERSION = 1;
    public static readonly TimeSpan STALE_AFTER = TimeSpan.FromDays(8);

    // Always lists the five main statuses; the rarer ones only when present, unknown statuses last.
    public IReadOnlyList<(string status, int count)> getStatusCounts() {
        var counts = worktrees.GroupBy(entry => entry.status, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count());
        return counts.Keys.Concat(WorktreeStatuses.order).Distinct(StringComparer.Ordinal)
            .Where(status => WorktreeStatuses.isAlwaysShown(status) || counts.GetValueOrDefault(status) > 0)
            .OrderBy(WorktreeStatuses.getRank).ThenBy(status => status, StringComparer.Ordinal)
            .Select(status => (status, counts.GetValueOrDefault(status))).ToList();
    }

    public IReadOnlyList<string> getAlerts(DateTimeOffset now) {
        var alerts = new List<string>();
        if (schemaVersion != SUPPORTED_SCHEMA_VERSION) {
            alerts.Add($"報告格式版本 {schemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 未經驗證，內容可能不完整。");
        }
        if (createdAt is not DateTimeOffset created) { alerts.Add("報告缺少有效的產生時間，無法判斷是否過期。"); }
        else if (now - created > STALE_AFTER) {
            alerts.Add($"報告已 {(int)(now - created).TotalDays} 天未更新（{created.ToLocalTime():yyyy/MM/dd HH:mm}），請重新產生。");
        }
        if (gaps.Count > 0) {
            var listed = gaps.Take(3).Select(gap => (string.IsNullOrEmpty(gap.project) ? gap.path : gap.project, gap.reason) switch {
                ("", var reason) => reason,
                (var name, "") => name,
                (var name, var reason) => $"{name}（{reason}）"
            });
            alerts.Add($"盤點缺口 {gaps.Count} 項：{string.Join("、", listed)}{(gaps.Count > 3 ? " 等" : "")}");
        }
        if (writeFailures.Count > 0) { alerts.Add("報告檔寫入失敗：" + string.Join("、", writeFailures)); }
        return alerts;
    }

    public static WorktreeReport parse(string json) {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw new FormatException("JSON 格式錯誤：" + exception.Message, exception); }
        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { throw new FormatException("報告最上層不是物件。"); }
            if (!root.TryGetProperty("worktrees", out var list) || list.ValueKind != JsonValueKind.Array) {
                throw new FormatException("報告缺少 worktrees 清單。");
            }
            int? schemaVersion = root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var parsedVersion) ? parsedVersion : null;
            DateTimeOffset? createdAt = DateTimeOffset.TryParse(getString(root, "createdAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var created) ? created : null;
            var entries = list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
                .Where(item => !getBool(item, "primary") && getString(item, "status") != WorktreeStatuses.PRIMARY)
                .Select(item => new WorktreeEntry(getString(item, "status"), getString(item, "project"), getString(item, "path"),
                    getString(item, "branch"), getString(item, "head"), getBool(item, "detached"), getString(item, "owner"),
                    getString(item, "purpose"), getString(item, "keepReason"), getDouble(item, "idleDays"), getLong(item, "sizeBytes"),
                    getStrings(item, "warnings"), getBool(item, "broken"), getString(item, "manager")))
                .OrderBy(entry => WorktreeStatuses.getRank(entry.status)).ThenBy(entry => entry.status, StringComparer.Ordinal)
                .ThenBy(entry => entry.project, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.path, StringComparer.Ordinal)
                .ToList();
            var gaps = new List<WorktreeReportGap>();
            if (root.TryGetProperty("gaps", out var gapList) && gapList.ValueKind == JsonValueKind.Array) {
                foreach (var gap in gapList.EnumerateArray()) {
                    if (gap.ValueKind == JsonValueKind.Object) {
                        gaps.Add(new(getString(gap, "project"), getString(gap, "path"), getString(gap, "reason")));
                    } else if (gap.ValueKind == JsonValueKind.String) { gaps.Add(new("", "", gap.GetString()!)); }
                    else { gaps.Add(new("", "", gap.GetRawText())); }
                }
            }
            return new WorktreeReport(schemaVersion, createdAt, entries, gaps, getStrings(root, "writeFailures"));
        }
    }

    private static string getString(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static bool getBool(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static double? getDouble(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    private static long? getLong(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) { return null; }
        if (value.TryGetInt64(out var number)) { return number; }
        return value.TryGetDouble(out var real) && double.IsFinite(real) && real >= long.MinValue && real <= long.MaxValue ? (long)real : null;
    }

    private static IReadOnlyList<string> getStrings(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) { return []; }
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())
            .Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
    }
}

public enum WorktreeReportState { NOT_CONFIGURED, CONFIG_ERROR, MISSING, INVALID, LOADED }

public sealed record WorktreeReportLoad(WorktreeReportState state, WorktreeReport? report, string? message) {
    public static WorktreeReportLoad read(string reportPath) {
        if (!File.Exists(reportPath)) {
            return new(WorktreeReportState.MISSING, null, $"找不到報告：{reportPath}，請按「重新產生報告」。");
        }
        try {
            // Share delete so the generator's atomic replace is never blocked by this reader.
            using var stream = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return new(WorktreeReportState.LOADED, WorktreeReport.parse(reader.ReadToEnd()), null);
        }
        catch (FileNotFoundException) {
            return new(WorktreeReportState.MISSING, null, $"找不到報告：{reportPath}，請按「重新產生報告」。");
        }
        catch (FormatException exception) { return new(WorktreeReportState.INVALID, null, "報告無法解析：" + exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return new(WorktreeReportState.INVALID, null, "報告無法讀取：" + exception.Message);
        }
    }
}

// Machine paths come from the local agent-rules profile, never from this repository.
public sealed record WorktreeToolConfig(string profilePath, string reportDirectory, string? pythonPath, string? scriptPath) {
    public const string REPORT_FILE_NAME = "worktree-report.json";

    public string reportPath => Path.Combine(reportDirectory, REPORT_FILE_NAME);

    public string? regenerateUnavailableReason => string.IsNullOrWhiteSpace(pythonPath) ? "profile 缺少 tools.python。"
        : string.IsNullOrWhiteSpace(scriptPath) ? "profile 缺少 tools.worktreeReportScript。"
        : Path.IsPathRooted(pythonPath) && !File.Exists(pythonPath) ? "找不到 Python：" + pythonPath
        : !File.Exists(scriptPath) ? "找不到報告腳本：" + scriptPath : null;

    public static WorktreeToolConfig read(string profilePath) {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profilePath.Trim()));
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("找不到 profile：" + fullPath, fullPath); }
        JsonDocument document;
        try { document = JsonDocument.Parse(File.ReadAllText(fullPath, Encoding.UTF8)); }
        catch (JsonException exception) { throw new FormatException("profile JSON 格式錯誤：" + exception.Message, exception); }
        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { throw new FormatException("profile 最上層不是物件。"); }
            var baseDirectory = Path.GetDirectoryName(fullPath)!;
            var reportDirectory = resolve(getNested(root, "worktrees", "reportDir"), baseDirectory)
                ?? throw new FormatException("profile 缺少 worktrees.reportDir。");
            var python = getNested(root, "tools", "python");
            // A bare command name such as "python" is left for PATH lookup.
            var pythonPath = string.IsNullOrWhiteSpace(python) ? null
                : python.IndexOfAny(['\\', '/']) < 0 ? python : resolve(python, baseDirectory);
            return new WorktreeToolConfig(fullPath, reportDirectory, pythonPath,
                resolve(getNested(root, "tools", "worktreeReportScript"), baseDirectory));
        }
    }

    public ProcessStartInfo createRegenerateStartInfo() {
        if (regenerateUnavailableReason is string reason) { throw new InvalidOperationException(reason); }
        var startInfo = new ProcessStartInfo(pythonPath!) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            // The generator resolves relative profile paths against its working directory; match resolve() below.
            WorkingDirectory = Path.GetDirectoryName(profilePath)!
        };
        foreach (var argument in new[] { "-B", scriptPath!, "report", "--profile", profilePath }) { startInfo.ArgumentList.Add(argument); }
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
    }

    private static string? getNested(JsonElement root, string section, string name) => root.TryGetProperty(section, out var parent)
        && parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static string? resolve(string? path, string baseDirectory) => string.IsNullOrWhiteSpace(path) ? null
        : Path.GetFullPath(path, baseDirectory);
}

public sealed record WorktreeRunResult(int exitCode, string output, string error) {
    public string? failureMessage {
        get {
            if (exitCode == 0) { return null; }
            var details = new List<string>();
            try {
                using var document = JsonDocument.Parse(output.Trim());
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("writeFailures", out var failures) && failures.ValueKind == JsonValueKind.Array
                    && failures.GetArrayLength() > 0) {
                    details.Add("報告檔寫入失敗：" + string.Join("、", failures.EnumerateArray().Select(item => item.ToString())));
                }
            } catch (JsonException) { }
            var lastError = error.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
            if (lastError != null) { details.Add(lastError.Length > 300 ? lastError[..300] + "…" : lastError); }
            return $"報告產生失敗（結束碼 {exitCode}）" + (details.Count > 0 ? "：" + string.Join("；", details) : "。");
        }
    }
}

public interface IWorktreeReportRunner {
    Task<WorktreeRunResult> run(ProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed class WorktreeReportRunner(TimeSpan? timeout = null) : IWorktreeReportRunner {
    public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromMinutes(15);
    private readonly TimeSpan limit = timeout ?? DEFAULT_TIMEOUT;

    public async Task<WorktreeRunResult> run(ProcessStartInfo startInfo, CancellationToken cancellationToken) {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("無法啟動報告程式。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            try { process.Kill(true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
                or AggregateException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"報告產生超過 {limit.TotalMinutes:0.#} 分鐘，已停止。");
        }
        return new WorktreeRunResult(process.ExitCode, await outputTask, await errorTask);
    }
}
