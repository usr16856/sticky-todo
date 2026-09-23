using System.Text;
using StickyTodo.Core;

var root = Path.Combine(Path.GetTempPath(), "StickyTodo.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = new List<(string name, Action action)> {
    ("刪除重新編號、移除空分組與保留備份", () => {
        var store = new TodoStore(Path.Combine(root, "delete.md")); store.initialize();
        var initial = store.read(); initial.document.add("A", "甲"); initial.document.add("A", "乙");
        initial.document.add("B", "保留");
        var saved = store.save(initial.document, initial.version);
        var changed = saved.document.clone(); changed.remove("A", 0);
        var result = store.save(changed, saved.version);
        check(MarkdownCodec.serialize(result.document).Contains("1. [ ] 乙"), "刪除未重新編號");
        check(MarkdownCodec.parse(File.ReadAllText(store.filePath + ".bak")).groups[0].items.Count == 2, "刪除前備份遺失");
        result.document.remove("A", 0);
        check(result.document.groups.Count == 1 && result.document.groups[0].name == "B", "空分組或其他分組錯誤");
        try { store.save(result.document, saved.version); throw new Exception("舊版本刪除未被阻擋"); }
        catch (DataConflictException) { }
        check(store.read().document.groups[0].items[0].content == "乙", "衝突刪除改動了資料");
    }),
    ("不透明度預設、往返與舊設定相容", () => {
        var path = Path.Combine(root, "opacity.json");
        var store = new SettingsStore(path);
        check(store.read().windowOpacity == 1.0, "首次預設錯誤");
        File.WriteAllText(path, "{\"width\":400}");
        check(store.read().windowOpacity == 1.0 && store.read().width == 400, "舊設定不相容");
        store.save(new AppSettings { windowOpacity = 0.75 });
        check(store.read().windowOpacity == 0.75, "透明度沒有保存");
    }),
    ("不透明度限制安全範圍", () => {
        var path = Path.Combine(root, "opacity-range.json");
        var store = new SettingsStore(path);
        File.WriteAllText(path, "{\"windowOpacity\":0}");
        check(store.read().windowOpacity == 0.1, "下限錯誤");
        File.WriteAllText(path, "{\"windowOpacity\":0.1}");
        check(store.read().windowOpacity == 0.1, "10% 透明度沒有保留");
        File.WriteAllText(path, "{\"windowOpacity\":2}");
        check(store.read().windowOpacity == 1.0, "上限錯誤");
    }),
    ("UTF-8 中文、多行、特殊字元與空白往返", () => {
        var document = new TodoDocument();
        string[] contents = ["中文 😀 & < > **粗體** [連結](file) ", "首行\n\n## 不應變成專案\n1. [x] 不應變成新項目\n    保留縮排\n", "\n第二行", "  空白保留  "];
        foreach (var content in contents) { document.add("SpotCam", content); }
        var parsed = MarkdownCodec.parse(MarkdownCodec.serialize(document));
        check(parsed.groups.Single().items.Select(item => item.content).SequenceEqual(contents), "文字被改寫");
    }),
    ("完成狀態與各專案獨立編號", () => {
        var document = new TodoDocument();
        document.add("其他", "其他項目"); document.add("SpotCam", "甲"); document.add("SpotCam", "乙");
        document.groups.Single(group => group.name == "SpotCam").items[0] = new TodoItem("甲", true);
        var text = MarkdownCodec.serialize(document);
        check(text.IndexOf("## SpotCam", StringComparison.Ordinal) < text.IndexOf("## 其他", StringComparison.Ordinal), "其他不在末尾");
        check(text.Contains("1. [x] 甲\n2. [ ] 乙") && text.Contains("## 其他\n1. [ ]"), "編號或狀態錯誤");
        check(MarkdownCodec.parse(text).groups[0].items[0].isCompleted, "完成狀態遺失");
    }),
    ("多專案新增建立獨立副本並排除重複選取", () => {
        var document = new TodoDocument();
        document.addToProjects(["SpotCam", "其他", "spotcam"], "同一段\n多行內容");
        check(document.groups.Count == 2, "重複專案沒有合併");
        check(document.groups.All(group => group.items.Single().content == "同一段\n多行內容"), "多專案內容不一致");
        document.groups.Single(group => group.name == "SpotCam").items[0] = new TodoItem("已修改", true);
        check(document.groups.Single(group => group.name == "其他").items[0] == new TodoItem("同一段\n多行內容"), "副本不是獨立項目");
        var parsed = MarkdownCodec.parse(MarkdownCodec.serialize(document));
        check(parsed.groups.Count == 2 && parsed.groups.All(group => group.items.Count == 1), "多專案 Markdown 往返失敗");
        expect<ArgumentException>(() => document.addToProjects([], "內容"));
    }),
    ("跨專案移動保留狀態並接續編號", () => {
        var document = new TodoDocument(); document.add("A", "甲"); document.add("B", "乙");
        document.groups[0].items[0] = new TodoItem("甲", true);
        document.edit("A", 0, "B", "修改甲");
        check(document.groups.Count == 1 && document.groups[0].items[1] == new TodoItem("修改甲", true), "移動失敗");
        check(MarkdownCodec.serialize(document).Contains("2. [x] 修改甲"), "移動後編號錯誤");
    }),
    ("原專案編輯保留位置與狀態", () => {
        var document = new TodoDocument(); document.add("A", "甲"); document.add("A", "乙");
        document.edit("A", 0, "a", "更新");
        check(document.groups.Count == 1 && document.groups[0].items[0].content == "更新", "大小寫匹配失敗");
    }),
    ("首次空白檔與初始化不覆蓋既有內容", () => {
        var store = new TodoStore(Path.Combine(root, "initial.md")); store.initialize();
        check(store.read().document.groups.Count == 0, "有範例內容");
        var snapshot = store.read(); snapshot.document.add("A", "保留"); store.save(snapshot.document, snapshot.version);
        store.initialize(); check(store.read().document.groups[0].items[0].content == "保留", "初始化覆蓋資料");
    }),
    ("安全存檔保留上一版備份", () => {
        var store = new TodoStore(Path.Combine(root, "backup.md")); store.initialize();
        var original = File.ReadAllBytes(store.filePath); var snapshot = store.read(); snapshot.document.add("A", "甲");
        var saved = store.save(snapshot.document, snapshot.version);
        check(File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(original), "上一版備份不符");
        check(saved.version == store.read().version, "儲存版本不符");
        saved.document.add("A", "乙"); store.save(saved.document, saved.version);
        check(MarkdownCodec.parse(File.ReadAllText(store.filePath + ".bak")).groups[0].items.Count == 1, "備份不是上一版");
    }),
    ("外部變更衝突不覆蓋並保留草稿", () => {
        var store = new TodoStore(Path.Combine(root, "conflict.md")); store.initialize();
        var baseline = store.read(); baseline.document.add("A", "草稿");
        var external = "# 我的待辦\n\n## 其他\n1. [ ] 外部內容\n"; File.WriteAllText(store.filePath, external);
        expect<DataConflictException>(() => store.save(baseline.document, baseline.version));
        check(File.ReadAllText(store.filePath) == external && baseline.document.groups[0].items[0].content == "草稿", "衝突造成資料遺失");
    }),
    ("兩個儲存者使用舊版本時衝突", () => {
        var first = new TodoStore(Path.Combine(root, "writers.md")); first.initialize(); var second = new TodoStore(first.filePath);
        var a = first.read(); var b = second.read(); a.document.add("A", "一"); b.document.add("A", "二");
        first.save(a.document, a.version); expect<DataConflictException>(() => second.save(b.document, b.version));
        check(first.read().document.groups[0].items[0].content == "一", "第二個儲存者覆蓋第一個");
    }),
    ("檔案占用時原檔與草稿保留", () => {
        var store = new TodoStore(Path.Combine(root, "locked.md")); store.initialize(); var baseline = store.read();
        var original = File.ReadAllBytes(store.filePath); baseline.document.add("A", "草稿");
        using (var locked = new FileStream(store.filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            expect<IOException>(() => store.save(baseline.document, baseline.version));
        }
        check(File.ReadAllBytes(store.filePath).SequenceEqual(original), "占用時改寫了原檔");
        store.save(baseline.document, baseline.version);
    }),
    ("鎖定協定阻擋其他寫入者", () => {
        var store = new TodoStore(Path.Combine(root, "cooperative.md")); store.initialize(); var baseline = store.read();
        using var locked = new FileStream(store.filePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        expect<IOException>(() => store.save(baseline.document, baseline.version));
    }),
    ("檔案消失時不得悄悄重建覆蓋", () => {
        var store = new TodoStore(Path.Combine(root, "missing.md")); store.initialize(); var baseline = store.read();
        File.Delete(store.filePath); expect<DataConflictException>(() => store.save(baseline.document, baseline.version));
        check(!File.Exists(store.filePath), "刪除後被悄悄重建");
    }),
    ("唯讀資料檔寫入失敗保留原檔", () => {
        var store = new TodoStore(Path.Combine(root, "readonly.md")); store.initialize(); var baseline = store.read();
        baseline.document.add("A", "草稿"); File.SetAttributes(store.filePath, FileAttributes.ReadOnly);
        try { expect<UnauthorizedAccessException>(() => store.save(baseline.document, baseline.version)); }
        finally { File.SetAttributes(store.filePath, FileAttributes.Normal); }
        check(store.read().document.groups.Count == 0, "唯讀檔已改寫");
    }),
    ("格式異常、重複專案與錯誤編號拒絕載入", () => {
        foreach (var invalid in new[] { "不支援文字", "## A\n2. [ ] 跳號", "## A\n1. [ ] \n", "## A\n## a\n", "    無所屬項目", "##  A\n" }) {
            expect<FormatException>(() => MarkdownCodec.parse(invalid));
        }
    }),
    ("UTF-8 BOM 與 CRLF 可讀取", () => {
        var parsed = MarkdownCodec.parse("\uFEFF# 我的待辦\r\n\r\n## 中文\r\n1. [X] 已完成\r\n    下一行\r\n");
        check(parsed.groups[0].items[0] == new TodoItem("已完成\n下一行", true), "Windows 格式解析失敗");
    }),
    ("非法 UTF-8 不以替代字元覆蓋", () => {
        var path = Path.Combine(root, "encoding.md"); File.WriteAllBytes(path, [0xff, 0xfe, 0x80]);
        expect<DecoderFallbackException>(() => new TodoStore(path).read());
    }),
    ("空白內容拒絕新增與編輯", () => {
        var document = new TodoDocument(); expect<ArgumentException>(() => document.add("A", " \n\t"));
        document.add("A", "保留"); expect<ArgumentException>(() => document.edit("A", 0, "B", ""));
        check(document.groups[0].items[0].content == "保留", "驗證失敗改變原內容");
    }),
    ("專案選單包含非 Git 專案並排除管理目錄", () => {
        var projectRoot = Path.Combine(root, "projects"); Directory.CreateDirectory(projectRoot);
        foreach (var name in new[] { "Zebra", "alpha", "docs", ".codex-artifacts", "node_modules", "其他", "hidden" }) { Directory.CreateDirectory(Path.Combine(projectRoot, name)); }
        File.SetAttributes(Path.Combine(projectRoot, "hidden"), FileAttributes.Hidden);
        check(ProjectCatalog.discover(projectRoot).SequenceEqual(new[] { "alpha", "Zebra", "其他" }), "專案清單錯誤");
    }),
    ("設定重新讀取保留視窗、置頂、篩選及收合", () => {
        var store = new SettingsStore(Path.Combine(root, "settings", "settings.json"));
        var initial = store.read(); check(!initial.isTopmost && !initial.hideCompleted && initial.lastProject == "其他", "預設不符");
        initial.width = 460; initial.height = 640; initial.left = -130; initial.top = 80;
        initial.isTopmost = true; initial.hideCompleted = true; initial.lastProject = "SpotCam";
        initial.lastProjects = ["SpotCam", "其他"]; initial.collapsedProjects.Add("A");
        store.save(initial); var loaded = store.read();
        check(loaded.width == 460 && loaded.left == -130 && loaded.isTopmost && loaded.hideCompleted
            && loaded.lastProject == "SpotCam" && loaded.lastProjects.SequenceEqual(new[] { "SpotCam", "其他" })
            && loaded.collapsedProjects.Contains("A"), "設定遺失");
    }),
    ("舊版單一專案設定自動轉成多選集合", () => {
        var path = Path.Combine(root, "legacy-project.json");
        File.WriteAllText(path, "{\"lastProject\":\"SpotCam\"}");
        var loaded = new SettingsStore(path).read();
        check(loaded.lastProjects.SequenceEqual(new[] { "SpotCam" }) && loaded.lastProject == "SpotCam", "舊設定未遷移");
    }),
    ("損壞設定可被偵測", () => {
        var path = Path.Combine(root, "badsettings.json"); File.WriteAllText(path, "{bad}");
        expect<System.Text.Json.JsonException>(() => new SettingsStore(path).read());
    })
};
var failures = 0;
foreach (var test in tests) {
    try { test.action(); Console.WriteLine("PASS " + test.name); }
    catch (Exception exception) { failures++; Console.WriteLine("FAIL " + test.name + "\n" + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed. Evidence: {root}");
return failures == 0 ? 0 : 1;

static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
static void expect<T>(Action action) where T : Exception {
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
