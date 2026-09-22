using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StickyTodo;

internal static class ShellFileLauncher {
    [Flags]
    private enum OpenAsInfoFlags : uint {
        execute = 0x00000004
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo {
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string filePath;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? fileClass;
        internal OpenAsInfoFlags flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHOpenWithDialog(IntPtr ownerHandle, ref OpenAsInfo openAsInfo);

    internal static ProcessStartInfo createDefaultStartInfo(string filePath) {
        ensureFileExists(filePath);
        return new ProcessStartInfo(filePath) { UseShellExecute = true };
    }

    internal static void openDefault(string filePath) {
        Process.Start(createDefaultStartInfo(filePath));
    }

    internal static void chooseApplication(Window owner, string filePath) {
        ensureFileExists(filePath);
        var openAsInfo = new OpenAsInfo { filePath = filePath, fileClass = null, flags = OpenAsInfoFlags.execute };
        var result = SHOpenWithDialog(new WindowInteropHelper(owner).Handle, ref openAsInfo);
        if (result < 0) { Marshal.ThrowExceptionForHR(result); }
    }

    private static void ensureFileExists(string filePath) {
        if (!File.Exists(filePath)) { throw new FileNotFoundException("找不到待辦資料檔。", filePath); }
    }
}
