using System.Diagnostics;
using StickyTodo.Core;

internal static class WirelessAdbTests {
    private const string HEADER = "List of discovered mdns services\n";
    private const string PAIRING_ADDRESS = "192.168.1.10:37001";
    private const string CONNECTION_ADDRESS = "192.168.1.10:38001";
    private const string CONNECTION_SERVICE = "adb-phone._adb-tls-connect._tcp";
    private const string SERVICES = HEADER + "adb-phone _adb-tls-connect._tcp " + CONNECTION_ADDRESS + "\n";

    internal static IEnumerable<(string name, Action action)> createTests(string root) {
        yield return ("無線搜尋只列配對服務、去重排序並容忍格式錯誤", () => {
            var adb = new WirelessAdb((arguments, token) => {
                check(arguments.SequenceEqual(new[] { "mdns", "services" }), "搜尋命令不符");
                return Task.FromResult(HEADER + "b _adb-tls-pairing._tcp 192.168.1.11:37002\r\n"
                    + "a _adb-tls-pairing._tcp. " + PAIRING_ADDRESS + "\n"
                    + "a _adb-tls-pairing._tcp " + PAIRING_ADDRESS + "\n"
                    + "v6 _adb-tls-pairing._tcp [fe80::1234%12]:37003\n"
                    + "connect _adb-tls-connect._tcp 192.168.1.10:38001\nlegacy _adb._tcp 192.168.1.10:5555\n"
                    + "bad _adb-tls-pairing._tcp host:port\nshort\n");
            });
            var found = adb.discover(CancellationToken.None).GetAwaiter().GetResult();
            check(found.Select(device => device.serviceName).SequenceEqual(new[] { "a", "b", "v6" }), "搜尋結果過濾、去重或排序失敗");
            check(found[0].address == PAIRING_ADDRESS && found[2].address == "[fe80::1234%12]:37003", "搜尋位址解析錯誤");
        });
        yield return ("無線搜尋空清單、失效回覆與舊版 ADB", () => {
            check(new WirelessAdb((_, _) => Task.FromResult(HEADER)).discover(CancellationToken.None).GetAwaiter().GetResult().Count == 0,
                "空清單誤判失敗");
            expect<IOException>(() => new WirelessAdb((_, _) => Task.FromResult("error: mdns unavailable"))
                .discover(CancellationToken.None).GetAwaiter().GetResult());
            var error = expect<IOException>(() => new WirelessAdb((_, _) => throw new IOException("adb: unknown command pair"))
                .pair(PAIRING_ADDRESS, "012345", CancellationToken.None).GetAwaiter().GetResult());
            check(error.Message.Contains("更新 Android SDK Platform-Tools"), "舊版 ADB 缺少可操作提示");
        });
        yield return ("配對驗證六位 ASCII 碼與明確 IP 連接埠，保留前導零", () => {
            var calls = new List<IReadOnlyList<string>>();
            var adb = new WirelessAdb((arguments, _) => {
                calls.Add(arguments);
                return Task.FromResult("Successfully paired to " + arguments[1] + " [guid=adb-phone]\n");
            });
            adb.pair(" " + PAIRING_ADDRESS + " ", "012345", CancellationToken.None).GetAwaiter().GetResult();
            adb.pair("[::1]:65535", "000000", CancellationToken.None).GetAwaiter().GetResult();
            check(calls[0].SequenceEqual(new[] { "pair", PAIRING_ADDRESS, "012345" }) && calls[1][1] == "[::1]:65535", "配對參數或前導零遺失");
            foreach (var address in new[] { "", "192.168.1.10", "192.168.1.10:0", "192.168.1.10:65536", "192.168.1.10:-1",
                         "192.168.1.10: +80", "host:37001", "127.1:37001", "::1:37001", "192.168.1.10:37001\nexit", "[192.168.1.10]:37001" }) {
                expect<FormatException>(() => adb.pair(address, "012345", CancellationToken.None).GetAwaiter().GetResult());
            }
            foreach (var code in new[] { "", "12345", "1234567", "12a456", "１２３４５６", "12\n456" }) {
                expect<FormatException>(() => adb.pair(PAIRING_ADDRESS, code, CancellationToken.None).GetAwaiter().GetResult());
            }
            check(calls.Count == 2, "無效輸入仍執行 ADB");
        });
        yield return ("配對不能以結束碼或其他裝置的成功文字判定成功", () => {
            foreach (var reply in new[] { "", "Failed to pair", "error: wrong password", "Successfully paired to 192.168.1.11:37001" }) {
                expect<IOException>(() => new WirelessAdb((_, _) => Task.FromResult(reply))
                    .pair(PAIRING_ADDRESS, "123456", CancellationToken.None).GetAwaiter().GetResult());
            }
        });
        yield return ("自動連線辨識目標的 mDNS transport，避免重複連線", () => {
            var calls = new List<string>();
            var adb = new WirelessAdb((arguments, _) => {
                calls.Add(arguments[0]);
                return Task.FromResult(arguments[0] == "mdns" ? SERVICES : "List of devices attached\nUSB device\n" + CONNECTION_SERVICE + ". device model:Phone\n");
            });
            check(adb.connect(PAIRING_ADDRESS, null, CancellationToken.None).GetAwaiter().GetResult().isConnected, "未識別目標已自動連線");
            check(calls.SequenceEqual(new[] { "mdns", "devices" }), "已連線仍送出 connect");
        });
        yield return ("自動連線使用唯一同 IP 的連線埠，並確認實際 transport", () => {
            var hasConnected = false;
            var adb = new WirelessAdb((arguments, _) => {
                if (arguments[0] == "mdns") { return Task.FromResult(SERVICES + "other _adb-tls-connect._tcp 192.168.1.11:38002\n"); }
                if (arguments[0] == "connect") {
                    check(arguments[1] == CONNECTION_ADDRESS, "誤用配對埠或其他手機的連線埠");
                    hasConnected = true;
                    return Task.FromResult("connected to " + CONNECTION_ADDRESS);
                }
                return Task.FromResult("List of devices attached\nUSB device\n" + CONNECTION_ADDRESS + (hasConnected ? " device\n" : " offline\n"));
            });
            check(adb.connect(PAIRING_ADDRESS, null, CancellationToken.None).GetAwaiter().GetResult().isConnected && hasConnected, "未連線或未驗證目標");
        });
        yield return ("無法唯一對應連線服務時要求手動輸入，不猜配對埠或 5555", () => {
            foreach (var output in new[] { HEADER, SERVICES + "second _adb-tls-connect._tcp 192.168.1.10:38002\n",
                         HEADER + "pair _adb-tls-connect._tcp " + PAIRING_ADDRESS + "\n" }) {
                var adb = new WirelessAdb((arguments, _) => {
                    check(arguments[0] == "mdns", "未唯一對應仍發送連線命令"); return Task.FromResult(output);
                });
                var result = adb.connect(PAIRING_ADDRESS, null, CancellationToken.None).GetAwaiter().GetResult();
                check(!result.isConnected && result.message.Contains("主畫面"), "未引導輸入連線位址");
            }
        });
        yield return ("手動連線驗證同一 IP 與不同埠，無需 mDNS", () => {
            var calls = 0;
            var adb = new WirelessAdb((arguments, _) => {
                calls++;
                check(arguments[0] != "mdns", "手動連線依賴 mDNS");
                return Task.FromResult(arguments[0] == "connect" ? "already connected to " + CONNECTION_ADDRESS
                    : "List of devices attached\n" + CONNECTION_ADDRESS + " device\n");
            });
            foreach (var address in new[] { PAIRING_ADDRESS, "192.168.1.11:38001", "192.168.1.10", "192.168.1.10:38001 & exit" }) {
                expect<FormatException>(() => adb.connect(PAIRING_ADDRESS, address, CancellationToken.None).GetAwaiter().GetResult());
            }
            check(calls == 0, "無效連線位址仍執行 ADB");
            check(adb.connect(PAIRING_ADDRESS, CONNECTION_ADDRESS, CancellationToken.None).GetAwaiter().GetResult().isConnected, "手動連線失敗");
        });
        yield return ("連線回覆成功但目標離線、未授權或只有其他裝置時不能誤報", () => {
            foreach (var state in new[] { "offline", "unauthorized" }) {
                var connects = 0;
                var adb = new WirelessAdb((arguments, _) => {
                    if (arguments[0] == "connect") { connects++; return Task.FromResult("connected to " + CONNECTION_ADDRESS); }
                    return Task.FromResult("List of devices attached\nOTHER device\n192.168.1.11:38001 device\n" + CONNECTION_ADDRESS + " " + state + "\n");
                });
                check(!adb.connect(PAIRING_ADDRESS, CONNECTION_ADDRESS, CancellationToken.None).GetAwaiter().GetResult().isConnected
                    && connects == 1, "誤報連線成功或自動重送");
            }
        });
        yield return ("無線搜尋、配對與連線遵守取消，不自動重送", () => {
            foreach (var phase in new[] { "search", "pair", "connect" }) {
                using var cancellation = new CancellationTokenSource();
                var calls = 0;
                var adb = new WirelessAdb(async (_, token) => { calls++; await Task.Delay(Timeout.Infinite, token); return ""; });
                Task operation = phase == "search" ? adb.discover(cancellation.Token) : phase == "pair"
                    ? adb.pair(PAIRING_ADDRESS, "012345", cancellation.Token) : adb.connect(PAIRING_ADDRESS, CONNECTION_ADDRESS, cancellation.Token);
                cancellation.Cancel();
                expect<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
                check(calls == 1, "取消後仍重送命令");
            }
        });
        yield return ("無線 ADB 命令十五秒逾時", () => {
            var calls = 0;
            var adb = new WirelessAdb(async (_, token) => { calls++; await Task.Delay(Timeout.Infinite, token); return ""; });
            expect<OperationCanceledException>(() => adb.pair(PAIRING_ADDRESS, "012345", CancellationToken.None).GetAwaiter().GetResult());
            check(calls == 1, "逾時後仍重送配對");
        });
        yield return ("共用 ADB 執行器保留錯誤輸出，取消後等待客戶端結束", () => {
            var error = expect<IOException>(() => AdbCommand.runProcess("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
                "[Console]::Out.WriteLine('test-out'); [Console]::Error.WriteLine('test-error'); exit 3"], CancellationToken.None).GetAwaiter().GetResult());
            check(error.Message.Contains("test-out") && error.Message.Contains("test-error"), "遺失程序輸出");
            var pidFile = Path.Combine(root, "adb-client-pid.txt");
            using var cancellation = new CancellationTokenSource();
            var operation = AdbCommand.runProcess("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
                "[IO.File]::WriteAllText('" + pidFile.Replace("'", "''") + "', [string]$PID); Start-Sleep -Seconds 60"], cancellation.Token);
            try {
                check(SpinWait.SpinUntil(() => File.Exists(pidFile) || operation.IsCompleted, TimeSpan.FromSeconds(8)) && File.Exists(pidFile), "測試程序未啟動");
            } finally { cancellation.Cancel(); }
            expect<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
            var pid = int.Parse(File.ReadAllText(pidFile));
            try { using var process = Process.GetProcessById(pid); check(process.HasExited, "取消完成後客戶端仍執行"); }
            catch (ArgumentException) { /* The OS has already removed the process. */ }
        });
    }

    private static T expect<T>(Action action) where T : Exception {
        try { action(); } catch (T exception) { return exception; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void check(bool condition, string message) { if (!condition) { throw new Exception(message); } }
}
