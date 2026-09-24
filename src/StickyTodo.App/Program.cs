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
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = AppPaths.resolve(args, userRoot, localRoot);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.dataDirectory.ToUpperInvariant())))[..24];
        using var instanceMutex = new Mutex(true, @"Local\StickyTodo-" + identity, out var isNew);
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\StickyTodo-Activate-" + identity);
        if (!isNew) {
            activationEvent.Set();
            return;
        }
        try {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.install(application);
            var window = new MainWindow(paths.projectRoot, Path.Combine(paths.dataDirectory, "TODO.md"), paths.settingsDirectory,
                paths.isTestInstance, activationEvent);
            application.MainWindow = window;
            application.Run(window);
        } catch (Exception exception) {
            MessageBox.Show("StickyTodo 無法啟動：\n" + exception.Message, "StickyTodo", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            instanceMutex.ReleaseMutex();
        }
    }

}

internal sealed record AppPaths(string projectRoot, string dataDirectory, string settingsDirectory, bool isTestInstance) {
    internal static AppPaths resolve(string[] args, string userRoot, string localRoot) {
        var projectRoot = Path.GetFullPath(getArgument(args, "--project-root") ?? Path.Combine(userRoot, "git"));
        var dataArgument = getArgument(args, "--data-dir");
        var dataDirectory = Path.GetFullPath(dataArgument ?? Path.Combine(projectRoot, "StickyTodo"));
        var settingsDirectory = Path.GetFullPath(getArgument(args, "--settings-dir") ?? Path.Combine(localRoot, "StickyTodo"));
        return new AppPaths(projectRoot, dataDirectory, settingsDirectory, dataArgument != null);
    }

    private static string? getArgument(string[] args, string name) {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
