using System.Text;
using StickyTodo.Core;

internal static class DeviceUsageTests {
    internal static IEnumerable<(string name, Action action)> createTests(string root) {
        var time = DateTimeOffset.Parse("2026-11-05T10:30:00+08:00");
        DeviceUsage sample() => new("SERIAL-A", "測試機 | 名稱", DeviceUsageState.IN_USE, "Codex", @"測試 C:\work\a | b", time);
        yield return ("裝置表格往返、跳脫字元、時區與待辦獨立", () => {
            var document = new TodoDocument();
            document.add("A", "保留原文\n第二行", TodoPriority.HIGH);
            document.devices.Add(sample());
            var text = MarkdownCodec.serialize(document);
            var parsed = MarkdownCodec.parse(text);
            check(parsed.devices.Single() == sample() && parsed.hasDeviceSection, "裝置表格往返失敗");
            check(parsed.groups.Single().items.Single() == document.groups[0].items[0], "裝置影響待辦");
            check(parsed.clone().devices.Single() == sample(), "複製文件遺失裝置");
            var withoutFractions = text.Replace("2026-11-05T10:30:00.0000000+08:00", "2026-11-05T10:30:00+08:00");
            check(MarkdownCodec.parse(withoutFractions).devices.Single() == sample(), "不接受一般 ISO 8601 時間");
            check(!MarkdownCodec.serialize(new TodoDocument()).Contains(DeviceMarkdownCodec.START), "舊文件自動新增空區塊");
            check(MarkdownCodec.parse(DeviceMarkdownCodec.replaceSection("\uFEFF", [sample()])).devices.Single() == sample(), "空 BOM 文件無法登記");
            var crText = "# 我的待辦\r\r## A\r1. [ ] CR 換行\r";
            var first = DeviceMarkdownCodec.replaceSection(crText, [sample()]);
            var second = DeviceMarkdownCodec.replaceSection(first, [sample().changeUsage(DeviceUsageState.IDLE, "", "")]);
            check(MarkdownCodec.parse(second).groups[0].items[0].content == "CR 換行" && !second.Contains('\n'), "CR 文件替換區塊失敗");
        });
        yield return ("裝置狀態驗證、必填欄位與釋放清空", () => {
            expect<FormatException>(() => sample().changeUsage(DeviceUsageState.IN_USE, " ", "測試"));
            expect<FormatException>(() => sample().changeUsage(DeviceUsageState.IN_USE, "我", ""));
            expect<FormatException>(() => (sample() with { user = "第一行\n第二行" }).validate());
            expect<FormatException>(() => (sample() with { state = (DeviceUsageState)88 }).validate());
            expect<FormatException>(() => (sample() with { serial = " " }).validate());
            expect<FormatException>(() => (sample() with { state = DeviceUsageState.IDLE }).validate());
            var idle = sample().changeUsage(DeviceUsageState.IDLE, "舊使用者", "舊用途");
            check(idle.user == "" && idle.purpose == "" && idle.state == DeviceUsageState.IDLE, "釋放未清空");
        });
        yield return ("裝置表格拒絕重複序號、未知狀態、錯誤區塊與時間", () => {
            var section = DeviceMarkdownCodec.serialize([sample()]);
            foreach (var invalid in new[] {
                section.Replace(DeviceMarkdownCodec.END, ""), section + section,
                section.Replace("使用中", "忙碌"), section.Replace(time.ToString("o"), "2026-11-05 10:30"),
                section.Replace("| Codex |", "|  |"), section.Replace("| --- | --- | --- | --- | --- | --- |", "| --- |"),
                section.Replace(DeviceMarkdownCodec.START, "<!-- stickytodo:devices:v2:start -->") }) {
                expect<FormatException>(() => MarkdownCodec.parse("# 我的待辦\n\n" + invalid));
            }
            expect<FormatException>(() => DeviceMarkdownCodec.serialize([sample(), sample()]));
            var row = section.Split('\n').Single(line => line.StartsWith("| SERIAL-A"));
            expect<FormatException>(() => MarkdownCodec.parse("# 我的待辦\n" + section.Replace(row, row + "\n" + row)));
        });
        yield return ("裝置登記保留 BOM、CRLF 與待辦原文，不重複存檔", () => {
            var path = Path.Combine(root, "devices-preserve.md");
            var original = "\uFEFF# 我的待辦\r\n\r\n## A\r\n1. [ ] 保留末尾空白  \r\n    第二行\r\n\r\n";
            File.WriteAllText(path, original, new UTF8Encoding(false));
            var store = new TodoStore(path);
            var result = store.registerDevices([new("A", "手機"), new("A", "重複連線")]);
            check(result.document.devices.Single().state == DeviceUsageState.UNMARKED, "首次登記不是未標記");
            var text = File.ReadAllText(path, new UTF8Encoding(false));
            check(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "BOM 遺失");
            check(text[text.IndexOf("## A", StringComparison.Ordinal)..] == original[original.IndexOf("## A", StringComparison.Ordinal)..], "待辦原文被改寫");
            check(!text.Replace("\r\n", "").Contains('\n'), "換行格式改變");
            check(File.ReadAllBytes(path + ".bak").SequenceEqual(Encoding.UTF8.GetBytes(original)), "備份不是原始檔案");
            var bytes = File.ReadAllBytes(path);
            var backup = File.ReadAllBytes(path + ".bak");
            store.registerDevices([new("A", "手機")]);
            check(File.ReadAllBytes(path).SequenceEqual(bytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "相同裝置反覆寫檔");
            var expected = result.document.devices.Single();
            store.saveDevice(expected.changeUsage(DeviceUsageState.IN_USE, "我", "驗證"), expected);
            check(File.ReadAllText(path)[File.ReadAllText(path).IndexOf("## A", StringComparison.Ordinal)..]
                == original[original.IndexOf("## A", StringComparison.Ordinal)..], "裝置更新改寫待辦原文");
        });
        yield return ("裝置與待辦共同編輯安全合併，同台衝突保留外部資料", () => {
            var store = new TodoStore(Path.Combine(root, "device-conflicts.md"));
            store.initialize();
            var first = store.read(); first.document.add("A", "原始待辦");
            store.save(first.document, first.version);
            var baseline = store.registerDevices([new("A", "手機"), new("B", "平板")]);
            var device = baseline.document.devices.Single(device => device.serial == "A");
            var busy = device.changeUsage(DeviceUsageState.IN_USE, "Codex", "測試");
            store.saveDevice(busy, device);
            var draft = baseline.document.clone(); draft.edit("A", 0, "A", "待辦草稿");
            var merged = store.saveTodoChanges(draft, baseline);
            check(merged.document.devices.Single(device => device.serial == "A") == busy && merged.document.groups[0].items[0].content == "待辦草稿", "待辦存檔覆蓋裝置");
            var other = merged.document.devices.Single(device => device.serial == "B");
            var changed = store.read(); changed.document.add("A", "其他外部待辦");
            store.save(changed.document, changed.version);
            var result = store.saveDevice(other.changeUsage(DeviceUsageState.IDLE, "", ""), other);
            check(result.document.groups[0].items.Count == 2 && result.document.devices.Single(device => device.serial == "A") == busy, "裝置存檔覆蓋待辦或其他裝置");
            var bytes = File.ReadAllBytes(store.filePath);
            expect<DataConflictException>(() => store.saveDevice(device.changeUsage(DeviceUsageState.IDLE, "", ""), device));
            expect<DataConflictException>(() => store.saveTodoChanges(draft, baseline));
            check(File.ReadAllBytes(store.filePath).SequenceEqual(bytes), "衝突覆蓋外部資料");
        });
        yield return ("損壞裝置區塊及檔案鎖定不改寫原檔", () => {
            var store = new TodoStore(Path.Combine(root, "device-invalid.md")); store.initialize();
            store.registerDevices([new("A", "手機")]);
            var device = store.read().document.devices.Single();
            var invalid = File.ReadAllText(store.filePath).Replace("未標記", "非法狀態");
            File.WriteAllText(store.filePath, invalid);
            expect<FormatException>(() => store.registerDevices([new("B", "平板")]));
            expect<FormatException>(() => store.saveDevice(device.changeUsage(DeviceUsageState.IDLE, "", ""), device));
            check(File.ReadAllText(store.filePath) == invalid, "格式錯誤覆蓋原檔");
            File.WriteAllText(store.filePath, MarkdownCodec.serialize(new TodoDocument()));
            using var locked = new FileStream(store.filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            expect<IOException>(() => store.registerDevices([new("C", "新裝置")]));
        });
        yield return ("ADB 真機去重、模擬器排除及連線異常", () => {
            var calls = new List<string>();
            var discovery = new AdbDeviceDiscovery((arguments, token) => {
                calls.Add(string.Join(' ', arguments));
                if (arguments[0] == "devices") { return Task.FromResult("List of devices attached\nusb device product:p\nwifi device\nemulator-5554 device\nqemu device\nlocked unauthorized\ndead offline\nmissing device\n"); }
                return Task.FromResult(arguments[1] switch {
                    "qemu" => "[ro.kernel.qemu]: [1]\n[ro.serialno]: [EMU]",
                    "missing" => "[ro.product.model]: [Unknown]",
                    _ => "[ro.kernel.qemu]: [0]\n[ro.serialno]: [SAME]\n[ro.product.model]: [Phone]"
                });
            });
            var result = discovery.scan(CancellationToken.None).GetAwaiter().GetResult();
            check(result.devices.SequenceEqual(new[] { new ConnectedDevice("SAME", "Phone") }), "未去重真機或未排除模擬器");
            check(!result.isComplete && result.warning!.Contains("unauthorized") && result.warning.Contains("offline")
                && result.warning.Contains("穩定裝置序號"), "異常狀態未回報");
            check(!calls.Any(call => call.Contains("-s emulator-")), "對模擬器讀取屬性");
        });
        yield return ("ADB 無裝置、命令失敗及取消", () => {
            var empty = new AdbDeviceDiscovery((arguments, token) => Task.FromResult("List of devices attached\n"));
            check(empty.scan(CancellationToken.None).GetAwaiter().GetResult() is { isComplete: true, devices.Count: 0 }, "空清單誤判失敗");
            var failed = new AdbDeviceDiscovery((arguments, token) => throw new IOException("找不到 ADB"));
            expect<IOException>(() => failed.scan(CancellationToken.None).GetAwaiter().GetResult());
            var canceled = new AdbDeviceDiscovery(async (arguments, token) => { await Task.Delay(Timeout.Infinite, token); return ""; });
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            expect<OperationCanceledException>(() => canceled.scan(cancellation.Token).GetAwaiter().GetResult());
        });
    }

    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
    private static void expect<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
