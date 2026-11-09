using System.Diagnostics;
using System.Text;

namespace StickyTodo.Core;

internal static class AdbCommand {
    internal static Task<string> run(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        runProcess(resolveAdb(), arguments, cancellationToken);

    internal static async Task<string> runProcess(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
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
            if (process.ExitCode != 0) { throw new IOException("ADB 執行失敗：" + (errorText + "\n" + text).Trim()); }
            return text;
        } finally {
            if (!process.HasExited) {
                // Only stop our client; the shared ADB server can be used by other sessions.
                try { process.Kill(); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            // Observe redirected reads and finish the client process before cancellation completes.
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
