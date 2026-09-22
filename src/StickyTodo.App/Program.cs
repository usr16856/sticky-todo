using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace StickyTodo;

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var projectRoot = getArgument(args, "--project-root") ?? @"C:\Users\bench\git";
        var dataDirectory = getArgument(args, "--data-dir") ?? @"C:\Users\bench\git\StickyTodo";
        var settingsDirectory = getArgument(args, "--settings-dir") ?? Path.Combine(localRoot, "StickyTodo");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..24];
        using var instanceMutex = new Mutex(true, @"Local\StickyTodo-" + identity, out var isNew);
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StickyTodo-Activate-" + identity);
        if (!isNew) {
            activationEvent.Set();
            return;
        }
        try {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.install(application);
            var window = new MainWindow(projectRoot, Path.Combine(dataDirectory, "TODO.md"), settingsDirectory,
                getArgument(args, "--data-dir") != null, activationEvent);
            application.MainWindow = window;
            application.Run(window);
        } catch (Exception exception) {
            MessageBox.Show("StickyTodo 無法啟動：\n" + exception.Message, "StickyTodo", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            instanceMutex.ReleaseMutex();
        }
    }

    private static string? getArgument(string[] args, string name) {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
