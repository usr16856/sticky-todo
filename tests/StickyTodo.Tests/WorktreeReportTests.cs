using System.Diagnostics;
using System.Text.Json;
using StickyTodo.Core;

internal static class WorktreeReportTests {
    // Shape follows agent-rules worktree_report.py schemaVersion 1; values are synthetic.
    internal static string createReport(DateTimeOffset createdAt, string? extraTopLevel = null) => """
        {
         "schemaVersion": 1,
         "createdAt": "CREATED",
         "thresholds": {"likelyDoneIdleDays": 7, "riskIdleDays": 14},
         "basis": "local-git-refs-without-fetch",
         "deletionAuthorized": false,
         "counts": {"risk": 1, "likely-done": 1, "broken": 2, "stale-registry": 1, "unlisted": 0, "unscanned": 0,
                    "active": 1, "keep": 1, "locked": 0, "primary": 1},
         "gaps": [],EXTRA
         "worktrees": [
          {"project": "Alpha", "repository": "C:\\repo\\Alpha", "path": "C:/repo/Alpha", "primary": true, "branch": "main",
           "head": "1111111111111111111111111111111111111111", "detached": false, "locked": "", "broken": false, "manager": "git",
           "registered": false, "owner": "", "purpose": "", "roadmap": "", "keepReason": "", "warnings": [], "dirtyCount": 0,
           "unpushedCommits": 0, "mergedIntoDefault": true, "sizeBytes": 0, "lastActivity": "", "idleDays": 0.1, "status": "primary"},
          {"project": "beta", "path": "C:/wt/beta-keep", "primary": false, "branch": "feature/keep", "head": "2222222222222222222222222222222222222222",
           "detached": false, "manager": "claude-app", "owner": "Claude/abc", "keepReason": "長期保留", "warnings": [], "sizeBytes": 1073741824, "idleDays": 30, "status": "keep"},
          {"project": "Alpha", "path": "C:/wt/alpha-active", "primary": false, "branch": "", "head": "3333333333333333333333333333333333333333",
           "detached": true, "manager": "git", "owner": "", "warnings": ["unregistered", "outside-worktree-root"], "sizeBytes": 1826454690, "idleDays": 4.3,
           "status": "active"},
          {"project": "Zeta", "path": "C:/wt/zeta-broken", "primary": false, "branch": "", "head": "4444444444444444444444444444444444444444",
           "detached": true, "locked": "initializing", "broken": true, "manager": "codex-app", "warnings": ["locked"], "sizeBytes": 0, "idleDays": null,
           "status": "broken"},
          {"project": "alpha", "path": "C:/wt/alpha-broken", "primary": false, "branch": "", "head": "", "detached": true, "manager": 7,
           "warnings": ["locked", 5], "sizeBytes": 12.7, "idleDays": "bad", "status": "broken"},
          {"project": "Gamma", "path": "C:/wt/gamma-stale", "registered": true, "owner": "Codex/x", "purpose": "舊工作", "status": "stale-registry"},
          {"project": "Beta", "path": "C:/wt/beta-done", "primary": false, "branch": "done", "head": "5555555555555555555555555555555555555555",
           "warnings": [], "sizeBytes": 2147483648, "idleDays": 9.04, "status": "likely-done"},
          {"project": "Delta", "path": "C:/wt/delta-risk", "primary": false, "branch": "risky", "manager": "future-manager", "warnings": ["idle-unpushed-or-uncommitted"],
           "sizeBytes": 5, "idleDays": 20, "status": "risk"},
          {"project": "Omega", "path": "C:/wt/omega-new", "status": "future-state"},
          "not-an-object"
         ]
        }
        """.Replace("CREATED", createdAt.ToString("yyyy-MM-ddTHH:mm:sszzz")).Replace("EXTRA", extraTopLevel ?? "");

    internal static IEnumerable<(string name, Action action)> createTests(string root) {
        var now = new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.FromHours(8));
        yield return ("工作樹報告排除主 checkout 並依嚴重度、專案、路徑排序", () => {
            var report = WorktreeReport.parse(createReport(now.AddDays(-1)));
            check(report.worktrees.All(entry => entry.status != WorktreeStatuses.PRIMARY), "主 checkout 未排除");
            var paths = report.worktrees.Select(entry => entry.path).ToList();
            check(paths.SequenceEqual(new[] { "C:/wt/delta-risk", "C:/wt/beta-done", "C:/wt/alpha-broken", "C:/wt/zeta-broken",
                "C:/wt/gamma-stale", "C:/wt/alpha-active", "C:/wt/beta-keep", "C:/wt/omega-new" }), "排序錯誤：" + string.Join(",", paths));
            check(report.schemaVersion == 1 && report.createdAt == now.AddDays(-1), "版本或產生時間錯誤");
            check(report.getAlerts(now).Count == 0, "正常報告出現警示：" + string.Join("；", report.getAlerts(now)));
        });
        yield return ("工作樹狀態計數固定列出五類並只在有數量時列其他狀態", () => {
            var report = WorktreeReport.parse(createReport(now));
            var counts = report.getStatusCounts().Select(item => $"{item.status}={item.count}").ToList();
            check(counts.SequenceEqual(new[] { "risk=1", "likely-done=1", "broken=2", "stale-registry=1", "active=1", "keep=1", "future-state=1" }),
                "計數錯誤：" + string.Join(",", counts));
            var empty = WorktreeReport.parse("{\"schemaVersion\":1,\"worktrees\":[]}");
            check(empty.getStatusCounts().Select(item => $"{item.status}={item.count}")
                .SequenceEqual(new[] { "risk=0", "likely-done=0", "broken=0", "active=0", "keep=0" }), "空報告未列出主要狀態");
            check(WorktreeStatuses.getName("likely-done") == "可能已完成" && WorktreeStatuses.getName("future-state") == "future-state"
                && WorktreeStatuses.getName("") == "未知狀態", "狀態名稱錯誤");
        });
        yield return ("工作樹欄位容錯：缺漏、型別錯誤、分支與大小顯示", () => {
            var report = WorktreeReport.parse(createReport(now));
            WorktreeEntry find(string path) => report.worktrees.Single(entry => entry.path == path);
            var active = find("C:/wt/alpha-active");
            check(active.branchText == "detached 3333333" && active.idleText == "閒置 4.3 天" && active.sizeText == "1.70 GiB"
                && active.warnings.SequenceEqual(new[] { "unregistered", "outside-worktree-root" }), "使用中列顯示錯誤");
            var keep = find("C:/wt/beta-keep");
            check(keep.branchText == "feature/keep" && keep.owner == "Claude/abc" && keep.sizeText == "1.00 GiB" && keep.idleText == "閒置 30 天"
                && keep.keepReason == "長期保留", "保留列欄位錯誤");
            var odd = find("C:/wt/alpha-broken");
            check(odd.idleDays == null && odd.idleText == "閒置未知" && odd.sizeBytes == 12 && odd.branchText == "（無分支資訊）"
                && odd.warnings.SequenceEqual(new[] { "locked", "5" }), "錯誤型別未容錯");
            var broken = find("C:/wt/zeta-broken");
            check(broken.isBroken && broken.sizeBytes == 0 && broken.sizeText == "大小未量測" && odd.sizeText == "大小未量測",
                "損壞列把未量測的 0 顯示成大小");
            var stale = find("C:/wt/gamma-stale");
            check(stale.branch == "" && stale.sizeBytes == null && stale.sizeText == "大小未知" && stale.warnings.Count == 0
                && stale.owner == "Codex/x" && !stale.isDetached, "登記表列缺欄位未容錯");
            check(find("C:/wt/beta-done").idleText == "閒置 9 天", "閒置天數格式錯誤");
            var managers = new[] { "C:/wt/zeta-broken", "C:/wt/beta-keep", "C:/wt/alpha-active", "C:/wt/delta-risk", "C:/wt/alpha-broken",
                "C:/wt/gamma-stale" }.Select(path => find(path).managerText).ToList();
            check(managers.SequenceEqual(new[] { "Codex app 管理", "Claude app 管理", "Git 管理", "管理：future-manager", "管理者未知", "管理者未知" }),
                "管理者顯示錯誤：" + string.Join(",", managers));
        });
        yield return ("工作樹報告格式錯誤明確拒絕", () => {
            foreach (var invalid in new[] { "", "{bad", "[]", "\"text\"", "{}", "{\"worktrees\":{}}", "{\"worktrees\":null}" }) {
                expect<FormatException>(() => WorktreeReport.parse(invalid));
            }
        });
        yield return ("工作樹報告過期、缺時間、版本、缺口與寫入失敗警示", () => {
            check(WorktreeReport.parse(createReport(now.AddDays(-8))).getAlerts(now).Count == 0, "剛好 8 天就視為過期");
            var stale = WorktreeReport.parse(createReport(now.AddDays(-9))).getAlerts(now);
            check(stale.Count == 1 && stale[0].StartsWith("報告已 9 天未更新", StringComparison.Ordinal), "過期未警示：" + string.Join("；", stale));
            var noTime = WorktreeReport.parse("{\"schemaVersion\":1,\"createdAt\":\"not-a-date\",\"worktrees\":[]}").getAlerts(now);
            check(noTime.Single().Contains("缺少有效的產生時間"), "無效時間未警示");
            var version = WorktreeReport.parse($"{{\"schemaVersion\":2,\"createdAt\":\"{now:O}\",\"worktrees\":[]}}").getAlerts(now);
            check(version.Single().Contains("版本 2"), "未知版本未警示");
            check(WorktreeReport.parse($"{{\"createdAt\":\"{now:O}\",\"worktrees\":[]}}").getAlerts(now).Single().Contains("版本 未知"), "缺版本未警示");
            var gaps = WorktreeReport.parse(createReport(now, """
                 "writeFailures": ["worktree-report.csv"],
                """).Replace("\"gaps\": []", """
                "gaps": [{"project": "SpotCam", "path": "C:/x", "reason": "git-timeout"}, "registry-unavailable",
                         {"path": "C:/y", "reason": "r3"}, {"project": "P4", "reason": "r4"}]
                """)).getAlerts(now);
            check(gaps.Count == 2 && gaps[0] == "盤點缺口 4 項：SpotCam（git-timeout）、registry-unavailable、C:/y（r3） 等"
                && gaps[1] == "報告檔寫入失敗：worktree-report.csv", "缺口或寫入失敗警示錯誤：" + string.Join("；", gaps));
        });
        yield return ("工作樹報告讀檔：不存在、損壞、占用與正常", () => {
            var directory = Path.Combine(root, "worktree-report"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, WorktreeToolConfig.REPORT_FILE_NAME);
            var missing = WorktreeReportLoad.read(path);
            check(missing.state == WorktreeReportState.MISSING && missing.report == null && missing.message!.Contains(path), "不存在未提示");
            File.WriteAllText(path, "{\"worktrees\":");
            var invalid = WorktreeReportLoad.read(path);
            check(invalid.state == WorktreeReportState.INVALID && invalid.message!.StartsWith("報告無法解析", StringComparison.Ordinal), "損壞未提示");
            File.WriteAllText(path, createReport(now));
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                var locked = WorktreeReportLoad.read(path);
                check(locked.state == WorktreeReportState.INVALID && locked.message!.StartsWith("報告無法讀取", StringComparison.Ordinal), "占用未提示");
            }
            var loaded = WorktreeReportLoad.read(path);
            check(loaded.state == WorktreeReportState.LOADED && loaded.report!.worktrees.Count == 8 && loaded.message == null, "正常報告未載入");
        });
        yield return ("工作樹 profile 推得報告、Python 與腳本路徑", () => {
            var directory = Path.Combine(root, "worktree-profile"); Directory.CreateDirectory(directory);
            var profile = Path.Combine(directory, "profile.json");
            expect<FileNotFoundException>(() => WorktreeToolConfig.read(profile));
            File.WriteAllText(profile, "{bad");
            expect<FormatException>(() => WorktreeToolConfig.read(profile));
            File.WriteAllText(profile, "{\"tools\":{\"python\":\"python\"}}");
            expect<FormatException>(() => WorktreeToolConfig.read(profile));
            var python = Path.Combine(directory, "python.exe"); var script = Path.Combine(directory, "tools", "worktree_report.py");
            File.WriteAllText(profile, JsonSerializer.Serialize(new { worktrees = new { reportDir = "報告" },
                tools = new { python, worktreeReportScript = "tools/worktree_report.py" } }));
            var config = WorktreeToolConfig.read(profile);
            check(config.reportDirectory == Path.Combine(directory, "報告") && config.reportPath == Path.Combine(directory, "報告", "worktree-report.json")
                && config.pythonPath == python && config.scriptPath == Path.GetFullPath(script), "profile 路徑解析錯誤");
            check(config.regenerateUnavailableReason == "找不到 Python：" + python, "缺 Python 未提示");
            expect<InvalidOperationException>(() => config.createRegenerateStartInfo());
            File.WriteAllText(python, "");
            check(config.regenerateUnavailableReason == "找不到報告腳本：" + Path.GetFullPath(script), "缺腳本未提示");
            Directory.CreateDirectory(Path.GetDirectoryName(script)!); File.WriteAllText(script, "");
            check(config.regenerateUnavailableReason == null, "完整設定仍不可產生");
            var startInfo = config.createRegenerateStartInfo();
            check(startInfo.FileName == python && startInfo.ArgumentList.SequenceEqual(new[] { "-B", Path.GetFullPath(script), "report", "--profile", profile })
                && !startInfo.UseShellExecute && startInfo.CreateNoWindow && startInfo.RedirectStandardOutput && startInfo.RedirectStandardError
                && startInfo.WorkingDirectory == directory && startInfo.Environment["PYTHONIOENCODING"] == "utf-8",
                "產生報告命令錯誤：" + string.Join(" ", startInfo.ArgumentList));
            File.WriteAllText(profile, "{\"worktrees\":{\"reportDir\":\"r\"},\"tools\":{\"python\":\"python\"}}");
            var partial = WorktreeToolConfig.read(profile);
            check(partial.pythonPath == "python" && partial.regenerateUnavailableReason == "profile 缺少 tools.worktreeReportScript。", "PATH 上的 Python 或缺腳本設定錯誤");
            File.WriteAllText(profile, "{\"worktrees\":{\"reportDir\":\"r\"}}");
            check(WorktreeToolConfig.read(profile).regenerateUnavailableReason == "profile 缺少 tools.python。", "缺 Python 設定未提示");
        });
        yield return ("工作樹報告程式結果訊息", () => {
            check(new WorktreeRunResult(0, "{}", "").failureMessage == null, "成功仍有錯誤");
            check(new WorktreeRunResult(1, "{\"writeFailures\":[\"worktree-report.txt\"]}", "").failureMessage
                == "報告產生失敗（結束碼 1）：報告檔寫入失敗：worktree-report.txt", "寫入失敗未說明");
            check(new WorktreeRunResult(2, "", "Traceback\r\n  File x\r\nValueError: report-output-required\r\n\r\n").failureMessage
                == "報告產生失敗（結束碼 2）：ValueError: report-output-required", "stderr 最後一行未說明");
            check(new WorktreeRunResult(3, "not json", "").failureMessage == "報告產生失敗（結束碼 3）。", "無輸出失敗訊息錯誤");
            check(new WorktreeRunResult(4, "", new string('x', 400)).failureMessage!.EndsWith("…", StringComparison.Ordinal), "過長錯誤未截斷");
        });
        yield return ("工作樹報告程式背景執行、取消與逾時會停止程序", () => {
            var runner = new WorktreeReportRunner();
            var echo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "echo out& echo err 1>&2& exit /b 3" }) { echo.ArgumentList.Add(argument); }
            var result = runner.run(echo, CancellationToken.None).GetAwaiter().GetResult();
            check(result.exitCode == 3 && result.output.Trim() == "out" && result.error.Trim() == "err", "輸出或結束碼未取得");
            ProcessStartInfo sleeper() {
                var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" }) { info.ArgumentList.Add(argument); }
                return info;
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var watch = Stopwatch.StartNew();
            try { runner.run(sleeper(), cancellation.Token).GetAwaiter().GetResult(); throw new Exception("取消未停止"); }
            catch (OperationCanceledException) { }
            check(watch.Elapsed < TimeSpan.FromSeconds(20), "取消後仍等待程序結束");
            watch.Restart();
            try { new WorktreeReportRunner(TimeSpan.FromMilliseconds(500)).run(sleeper(), CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("逾時未停止"); }
            catch (TimeoutException exception) { check(exception.Message.Contains("已停止"), "逾時訊息錯誤"); }
            check(watch.Elapsed < TimeSpan.FromSeconds(20), "逾時後仍等待程序結束");
        });
        yield return ("工作樹面板設定預設收合、舊設定相容與 profile 往返", () => {
            var path = Path.Combine(root, "worktree-settings.json");
            var store = new SettingsStore(path);
            check(store.read().isWorktreePanelCollapsed && store.read().worktreeProfilePath == null, "預設值錯誤");
            File.WriteAllText(path, "{\"isDevicePanelCollapsed\":true}");
            check(store.read().isWorktreePanelCollapsed && store.read().worktreeProfilePath == null, "舊設定不相容");
            store.save(new AppSettings { isWorktreePanelCollapsed = false, worktreeProfilePath = @"D:\local\profile.json" });
            var loaded = store.read();
            check(!loaded.isWorktreePanelCollapsed && loaded.worktreeProfilePath == @"D:\local\profile.json", "工作樹設定沒有保存");
        });
    }

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    private static void expect<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
