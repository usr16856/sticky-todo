using StickyTodo.Core;

internal static class DeviceBatteryTests {
    internal static IEnumerable<(string name, Action action)> createTests(string root) {
        yield return ("電量百分比包含零、滿電、非百分制與四捨五入", () => {
            foreach (var (level, scale, expected) in new[] { (0, 100, 0), (100, 100, 100), (5, 8, 63), (1, 8, 13), (int.MaxValue, int.MaxValue, 100) }) {
                var battery = DeviceBatteryInfo.parse($"present: true\r\n  level: {level}\r\n scale: {scale}\r\n status: 3\r\n")!;
                check(battery.percentage == expected && battery.chargingState == DeviceChargingState.DISCHARGING, "百分比轉換錯誤");
            }
            var device = new ConnectedDevice("A", "Phone") { battery = new(78, DeviceChargingState.CHARGING) };
            var (serial, name) = device;
            check(serial == "A" && name == "Phone", "既有建構或解構介面改變");
        });
        yield return ("非法或缺漏電量不猜零，仍保留已知充電狀態", () => {
            foreach (var fields in new[] { "", "level: 10", "scale: 100", "level: -1\nscale: 100", "level: 101\nscale: 100",
                         "level: 0\nscale: 0", "level: 10\nscale: -1", "level: 9999999999999\nscale: 100", "level: abc\nscale: 100" }) {
                var battery = DeviceBatteryInfo.parse(fields + "\nstatus: 2")!;
                check(battery.percentage == null && battery.displayText == "電量未知 · 充電中", "無效電量被猜測或已知狀態遺失");
            }
        });
        yield return ("無電池、失效回覆與未知狀態正確處理", () => {
            foreach (var output in new[] { "", "error: permission denied", "present: false\nlevel: 90\nscale: 100\nstatus: 2",
                         "present: invalid\nlevel: 90\nscale: 100\nstatus: 2", "status: 1" }) {
                check(DeviceBatteryInfo.parse(output) == null, "無電池或失效資料未顯示未知");
            }
            foreach (var status in new[] { "", "1", "0", "99", "abc" }) {
                var battery = DeviceBatteryInfo.parse("level: 78\nscale: 100\nstatus: " + status)!;
                check(battery.percentage == 78 && battery.chargingState == DeviceChargingState.UNKNOWN
                    && battery.displayText == "電量 78% · 充電狀態未知", "未知狀態抹除有效電量");
            }
        });
        yield return ("充電狀態不以 USB、百分比或供電旗標推測", () => {
            foreach (var (status, expected) in new[] { (2, "充電中"), (3, "使用電池"), (4, "未在充電"), (5, "已充滿") }) {
                var battery = DeviceBatteryInfo.parse($"USB powered: true\nAC powered: false\nlevel: 100\nscale: 100\nstatus: {status}")!;
                check(battery.displayText == "電量 100% · " + expected, "充電狀態映射錯誤");
            }
        });
        yield return ("先辨識並去重所有真機，再每台讀取一次電量", () => {
            var propertyReads = 0;
            var batteryReads = new List<string>();
            var discovery = new AdbDeviceDiscovery((arguments, _) => {
                if (arguments[0] == "devices") { return Task.FromResult("List of devices attached\nusb device\nwifi device\nb device\nqemu device\nemulator-5554 device\nmissing device\nlocked unauthorized\n"); }
                if (arguments[3] == "getprop") {
                    propertyReads++;
                    return Task.FromResult(arguments[1] switch {
                        "qemu" => "[ro.kernel.qemu]: [1]\n[ro.serialno]: [EMU]",
                        "missing" => "[ro.product.model]: [Missing]",
                        "b" => "[ro.serialno]: [B]\n[ro.product.model]: [Phone B]",
                        _ => "[ro.serialno]: [SAME]\n[ro.product.model]: [Phone]"
                    });
                }
                check(propertyReads == 5 && arguments.SequenceEqual(new[] { "-s", arguments[1], "shell", "dumpsys", "battery" }), "未先完成辨識或電量命令錯誤");
                batteryReads.Add(arguments[1]);
                return Task.FromResult("level: 78\nscale: 100\nstatus: 2");
            });
            var result = discovery.scan(CancellationToken.None).GetAwaiter().GetResult();
            check(result.devices.Count == 2 && result.devices.All(device => device.battery == new DeviceBatteryInfo(78, DeviceChargingState.CHARGING)), "電量未附加到已去重裝置");
            check(batteryReads.SequenceEqual(new[] { "usb", "b" }) && !result.isComplete && result.warning!.Contains("unauthorized"), "重複讀取或連線警告改變");
        });
        yield return ("單台電量失敗不影響連線判定與其他裝置", () => {
            var discovery = new AdbDeviceDiscovery((arguments, _) => {
                if (arguments[0] == "devices" || arguments[3] == "getprop") { return Task.FromResult(identityResponse(arguments)); }
                if (arguments[1] == "a") { throw new IOException("battery unavailable"); }
                return Task.FromResult("level: 22\nscale: 100\nstatus: 4");
            });
            var result = discovery.scan(CancellationToken.None).GetAwaiter().GetResult();
            check(result.isComplete && result.warning == null && result.devices.Count == 2 && result.devices[0].battery == null
                && result.devices[1].battery == new DeviceBatteryInfo(22, DeviceChargingState.NOT_CHARGING), "電量失敗影響連線或其他裝置");
        });
        yield return ("電量兩秒逾時後繼續下一台，不重送或丟棄連線", () => {
            var reads = new List<string>();
            var discovery = new AdbDeviceDiscovery(async (arguments, token) => {
                if (arguments[0] == "devices" || arguments[3] == "getprop") { return identityResponse(arguments); }
                reads.Add(arguments[1]);
                if (arguments[1] == "a") { await Task.Delay(Timeout.Infinite, token); }
                return "level: 66\nscale: 100\nstatus: 3";
            });
            var result = discovery.scan(CancellationToken.None).GetAwaiter().GetResult();
            check(result.isComplete && result.devices.Count == 2 && result.devices[0].battery == null && result.devices[1].battery?.percentage == 66
                && reads.SequenceEqual(new[] { "a", "b" }), "電量逾時未隔離或未繼續其他裝置");
        });
        yield return ("App 取消在電量階段仍向上傳遞", () => {
            using var cancellation = new CancellationTokenSource();
            var reads = 0;
            var discovery = new AdbDeviceDiscovery(async (arguments, token) => {
                if (arguments[0] == "devices" || arguments[3] == "getprop") { return identityResponse(arguments); }
                reads++;
                await Task.Delay(Timeout.Infinite, token);
                return "";
            });
            var operation = discovery.scan(cancellation.Token);
            cancellation.Cancel();
            expectCancellation(() => operation.GetAwaiter().GetResult());
            check(reads == 1, "取消後繼續讀取裝置");
        });
        yield return ("整輪十五秒預算在電量階段耗盡仍回傳已辨識連線", () => {
            var reads = new List<string>();
            var discovery = new AdbDeviceDiscovery(async (arguments, token) => {
                if (arguments[0] == "devices") { return identityResponse(arguments); }
                if (arguments[3] == "getprop") {
                    if (arguments[1] == "a") { await Task.Delay(13200, token); }
                    return identityResponse(arguments);
                }
                reads.Add(arguments[1]);
                await Task.Delay(Timeout.Infinite, token);
                return "";
            });
            var result = discovery.scan(CancellationToken.None).GetAwaiter().GetResult();
            check(result.isComplete && result.devices.Count == 2 && result.devices.All(device => device.battery == null)
                && reads.SequenceEqual(new[] { "a" }), "掃描預算耗盡丟棄連線或繼續電量查詢");
        });
        yield return ("電量只存在即時資料，不改寫待辦、備份與使用紀錄", () => {
            var store = new TodoStore(Path.Combine(root, "battery-transient.md"));
            store.initialize(); store.registerDevices([new("A", "Phone")]);
            var initial = store.read();
            var bytes = File.ReadAllBytes(store.filePath);
            var backup = File.ReadAllBytes(store.filePath + ".bak");
            store.registerDevices([new("A", "Phone") { battery = new(78, DeviceChargingState.CHARGING) }]);
            check(File.ReadAllBytes(store.filePath).SequenceEqual(bytes) && File.ReadAllBytes(store.filePath + ".bak").SequenceEqual(backup)
                && store.read().document.devices.SequenceEqual(initial.document.devices), "電量修改持久資料或使用狀態時間");
        });
    }

    private static string identityResponse(IReadOnlyList<string> arguments) => arguments[0] == "devices" ? "List of devices attached\na device\nb device\n"
        : $"[ro.serialno]: [{arguments[1].ToUpperInvariant()}]\n[ro.product.model]: [Phone {arguments[1].ToUpperInvariant()}]";
    private static void expectCancellation(Action action) {
        try { action(); } catch (OperationCanceledException) { return; }
        throw new Exception("Expected OperationCanceledException");
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
