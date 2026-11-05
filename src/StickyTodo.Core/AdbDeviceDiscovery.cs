using System.Diagnostics;
using System.Text;

namespace StickyTodo.Core;

public sealed class AdbDeviceDiscovery(Func<IReadOnlyList<string>, CancellationToken, Task<string>>? command = null) : IDeviceDiscovery {
    public async Task<DeviceScanResult> scan(CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var run = command ?? runCommand;
        var output = await run(["devices", "-l"], timeout.Token);
        var devices = new Dictionary<string, ConnectedDevice>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var line in TodoDocument.normalizeLines(output).Split('\n')) {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2 || line.StartsWith("List of devices", StringComparison.Ordinal)
                || line.StartsWith('*') || columns[0].StartsWith("emulator-", StringComparison.Ordinal)) { continue; }
            var transport = columns[0];
            if (columns[1] != "device") {
                warnings.Add($"{transport}：{columns[1]}，請確認連線與裝置授權");
                continue;
            }
            try {
                var properties = await run(["-s", transport, "shell", "getprop"], timeout.Token);
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in TodoDocument.normalizeLines(properties).Split('\n')) {
                    var separator = property.IndexOf("]: [", StringComparison.Ordinal);
                    if (property.StartsWith('[') && property.EndsWith(']') && separator > 1) {
                        values[property[1..separator]] = property[(separator + 4)..^1];
                    }
                }
                if (values.GetValueOrDefault("ro.kernel.qemu") == "1" || values.GetValueOrDefault("ro.boot.qemu") == "1") { continue; }
                var serial = values.GetValueOrDefault("ro.serialno");
                if (string.IsNullOrWhiteSpace(serial) || serial.Equals("unknown", StringComparison.OrdinalIgnoreCase)) {
                    warnings.Add($"{transport}：無法取得穩定裝置序號");
                    continue;
                }
                var name = values.GetValueOrDefault("ro.product.model");
                devices.TryAdd(serial, new(serial, string.IsNullOrWhiteSpace(name) ? serial : name));
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                warnings.Add($"{transport}：{exception.Message}");
            }
        }
        return new(devices.Values.OrderBy(device => device.name, StringComparer.OrdinalIgnoreCase).ToList(),
            warnings.Count == 0, warnings.Count == 0 ? null : string.Join("\n", warnings));
    }

    private static async Task<string> runCommand(IReadOnlyList<string> arguments, CancellationToken cancellationToken) {
        var info = new ProcessStartInfo(resolveAdb()) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) { info.ArgumentList.Add(argument); }
        using var process = Process.Start(info) ?? throw new IOException("ADB 無法啟動。");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try {
            await process.WaitForExitAsync(cancellationToken);
            var text = await output;
            var errorText = await error;
            if (process.ExitCode != 0) { throw new IOException("ADB 讀取失敗：" + errorText.Trim()); }
            return text;
        } finally {
            if (!process.HasExited) { process.Kill(); }
            // Observe redirected reads even when cancellation interrupts the process.
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        }
    }

    private static string resolveAdb() {
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                     Environment.GetEnvironmentVariable("ANDROID_HOME"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk") }) {
            if (string.IsNullOrWhiteSpace(root)) { continue; }
            var path = Path.Combine(root, "platform-tools", "adb.exe");
            if (File.Exists(path)) { return path; }
        }
        return "adb.exe";
    }
}
