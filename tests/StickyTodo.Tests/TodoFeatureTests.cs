using StickyTodo.Core;

internal static class TodoFeatureTests {
    internal static IEnumerable<(string name, Action action)> createTests(string root) => [
        ("跨專案同步包含已完成、多筆及多行，保留位置與獨立等級", () => {
            var document = new TodoDocument();
            var source = document.getOrAddGroup("A");
            source.items.AddRange([new("前一筆"), new("共同\n需求", true, TodoPriority.HIGH), new("共同\n需求")]);
            var target = document.getOrAddGroup("B");
            target.items.AddRange([new("不同需求"), new("共同\n需求", true, TodoPriority.LOW), new("共同\n需求")]);
            document.getOrAddGroup("C").items.Add(new("共同\r\n需求", false, TodoPriority.HIGH));
            document.add("D", "更新\n內容");
            var matches = document.findMatchingItems("A", 1);
            check(matches.Count == 3 && matches.Count(item => item.isCompleted) == 1, "同步範圍或完成數錯誤");
            document.edit("A", 1, "A", "更新\r\n內容", TodoPriority.LOW, syncMatchingContent: true);
            check(source.items[0].content == "前一筆" && source.items[2].content == "共同\n需求", "同專案其他項目被修改");
            check(source.items[1] == new TodoItem("更新\n內容", true, TodoPriority.LOW), "目前項目或完成狀態錯誤");
            check(target.items.SequenceEqual(new TodoItem[] { new("不同需求"), new("更新\n內容", true, TodoPriority.LOW), new("更新\n內容") }),
                "其他專案的順序、完成狀態或等級被改動");
            check(document.groups[2].items[0] == new TodoItem("更新\n內容", false, TodoPriority.HIGH), "其他專案等級遺失");
            check(document.groups[3].items.Count == 1, "新文字被誤當原始同步目標");
        }),
        ("同文只統一換行，不忽略大小寫、空白或部分內容", () => {
            var document = new TodoDocument();
            document.add("A", "Same\nNext");
            document.add("A", "Same\nNext");
            var target = document.getOrAddGroup("B");
            foreach (var content in new[] { "Same\r\nNext", "Same\rNext", "same\nNext", "Same\nNext ", " Same\nNext", "Same", "Same\nNext\n" }) {
                target.items.Add(new(content));
            }
            check(document.findMatchingItems("A", 0).Select(item => item.index).SequenceEqual(new[] { 0, 1 }), "比對改動了文字語意");
        }),
        ("只改等級、單筆編輯及移動維持獨立項目", () => {
            var document = new TodoDocument();
            document.addToProjects(["A", "B"], "共同需求", TodoPriority.HIGH);
            document.edit("A", 0, "A", "共同需求", TodoPriority.LOW, syncMatchingContent: true);
            check(document.groups[1].items[0].priority == TodoPriority.HIGH, "同步修改了其他專案等級");
            document.edit("A", 0, "A", "單筆修改");
            check(document.groups[1].items[0].content == "共同需求", "單筆編輯仍然同步");
            check(document.findMatchingItems("A", 0).Count == 0, "文字分開後仍永久連動");
            document.groups[0].items[0] = document.groups[0].items[0] with { isCompleted = true };
            document.edit("A", 0, "C", "移動修改");
            check(document.groups.Single(group => group.name == "C").items[0] == new TodoItem("移動修改", true, TodoPriority.LOW),
                "移動遺失完成狀態或優先等級");
        }),
        ("同步移動、空白內容及無效等級不會部分修改", () => {
            var document = new TodoDocument();
            document.addToProjects(["A", "B"], "原文");
            var original = MarkdownCodec.serialize(document);
            expect<ArgumentException>(() => document.edit("A", 0, "C", "修改", syncMatchingContent: true));
            expect<ArgumentException>(() => document.edit("A", 0, "A", " ", syncMatchingContent: true));
            expect<ArgumentOutOfRangeException>(() => document.edit("A", 0, "A", "修改", (TodoPriority)99, true));
            expect<ArgumentOutOfRangeException>(() => document.addToProjects(["C", "D"], "新增", (TodoPriority)99));
            check(MarkdownCodec.serialize(document) == original, "驗證失敗仍修改了項目");
        }),
        ("優先等級 Markdown 舊格式及特殊內容往返", () => {
            var document = new TodoDocument();
            document.add("A", "一般內容");
            document.add("A", "<!-- stickytodo:priority=high -->\n<!-- stickytodo:priority=low -->\n\n## 文字與尾端空白  ", TodoPriority.HIGH);
            document.getOrAddGroup("B").items.Add(new("\n第二行\n", true, TodoPriority.LOW));
            var text = MarkdownCodec.serialize(document);
            check(text.Contains("## A\n1. [ ] 一般內容\n<!-- stickytodo:priority=high -->\n2. [ ] <!-- stickytodo:priority=high -->"),
                "等級註解位置或一般格式錯誤");
            var parsed = MarkdownCodec.parse("\uFEFF" + text.Replace("\n", "\r\n"));
            check(parsed.groups.SelectMany(group => group.items).SequenceEqual(document.groups.SelectMany(group => group.items)), "等級、完成狀態或文字往返遺失");
            check(MarkdownCodec.serialize(parsed) == text, "註解往返產生額外文字");
            var legacy = MarkdownCodec.parse("# 我的待辦\n\n## A\n1. [ ] 舊項目\n2. [x] 舊完成\n");
            check(legacy.groups[0].items.All(item => item.priority == TodoPriority.NORMAL), "舊資料未使用一般等級");
        }),
        ("未知、重複、孤立及錯位的等級註解拒絕讀取", () => {
            const string header = "# 我的待辦\n\n## A\n";
            const string annotation = "<!-- stickytodo:priority=high -->\n";
            foreach (var text in new[] {
                annotation + header + "1. [ ] 內容\n",
                header + "<!-- stickytodo:priority=urgent -->\n1. [ ] 內容\n",
                header + "<!-- stickytodo:priority=HIGH -->\n1. [ ] 內容\n",
                header + annotation + annotation + "1. [ ] 內容\n",
                header + annotation,
                header + annotation + "\n1. [ ] 內容\n",
                header + annotation + "## B\n1. [ ] 內容\n",
                header + "1. [ ] 內容\n" + annotation + "    延續行\n"
            }) { expect<FormatException>(() => MarkdownCodec.parse(text)); }
        }),
        ("同步存檔衝突阻擋整批更新並保留前一版備份", () => {
            var store = new TodoStore(Path.Combine(root, "sync-conflict.md"));
            store.initialize();
            var initial = store.read();
            initial.document.addToProjects(["A", "B"], "原文", TodoPriority.HIGH);
            initial.document.groups[1].items[0] = new("原文", true, TodoPriority.LOW);
            var baseline = store.save(initial.document, initial.version);
            var draft = baseline.document.clone();
            draft.edit("A", 0, "A", "同步修改", TodoPriority.NORMAL, true);
            var external = baseline.document.clone();
            external.add("C", "外部新增");
            var externalText = MarkdownCodec.serialize(external);
            File.WriteAllText(store.filePath, externalText);
            var backupBefore = File.ReadAllBytes(store.filePath + ".bak");
            expect<DataConflictException>(() => store.save(draft, baseline.version));
            check(File.ReadAllText(store.filePath) == externalText, "衝突仍寫入部分同步項目");
            check(File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(backupBefore), "衝突改寫了備份");
            var fresh = store.read();
            var updated = fresh.document.clone();
            updated.edit("A", 0, "A", "同步修改", TodoPriority.NORMAL, true);
            var result = store.save(updated, fresh.version);
            check(File.ReadAllText(store.filePath + ".bak") == externalText, "成功同步未備份整份前一版本");
            check(result.document.groups[1].items[0] == new TodoItem("同步修改", true, TodoPriority.LOW), "存檔遺失其他專案狀態");
            check(result.document.groups[2].items[0].content == "外部新增", "其他內容被覆蓋");
            check(!Directory.EnumerateFiles(root, "sync-conflict.md.*.tmp").Any(), "暫存檔未清理");
        }),
        ("錯誤等級資料及無效存檔不會改寫原檔", () => {
            var path = Path.Combine(root, "invalid-priority.md");
            var original = "# 我的待辦\n## A\n<!-- stickytodo:priority=urgent -->\n1. [ ] 內容\n";
            File.WriteAllText(path, original);
            var store = new TodoStore(path);
            expect<FormatException>(() => store.read());
            check(File.ReadAllText(path) == original && !File.Exists(path + ".bak"), "讀取錯誤改動了原檔");
            var document = new TodoDocument();
            document.getOrAddGroup("A").items.Add(new("內容", priority: (TodoPriority)99));
            expect<ArgumentOutOfRangeException>(() => store.save(document, "invalid-version"));
            check(File.ReadAllText(path) == original, "無效存檔改動了原檔");
        }),
        ("優先等級文字在預設與自訂底色維持對比及色彩區分", () => {
            string[] colors = [.. ThemePalette.presets.Select(preset => preset.color), "#000000", "#FFFFFF", "#757575", "#767676", "#777777",
                .. from r in Enumerable.Range(0, 6) from g in Enumerable.Range(0, 6) from b in Enumerable.Range(0, 6)
                   select $"#{r * 51:X2}{g * 51:X2}{b * 51:X2}"];
            foreach (var color in colors) {
                var palette = ThemePalette.from(color);
                check(ThemeColor.contrast(palette.highPriority, palette.paper) >= 4.5, "高等級對比不足：" + color);
                check(ThemeColor.contrast(palette.lowPriority, palette.paper) >= 4.5, "低等級對比不足：" + color);
                check(palette.highPriority != palette.lowPriority, "高低等級無法區分：" + color);
            }
        })
    ];

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }

    private static void expect<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
