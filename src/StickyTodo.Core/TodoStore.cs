using System.Security.Cryptography;
using System.Text;

namespace StickyTodo.Core;

public sealed record TodoSnapshot(TodoDocument document, string version);

public sealed class DataConflictException() : IOException("資料檔已在外部變更。您的草稿仍保留，請重新載入後再處理。");

public sealed class TodoStore(string filePath) {
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    public string filePath { get; } = Path.GetFullPath(filePath);

    public void initialize() {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var fileLock = acquireLock();
        if (!File.Exists(filePath)) {
            using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(strictUtf8.GetBytes("# 我的待辦\n"));
            stream.Flush(true);
        }
    }

    public TodoSnapshot read() {
        var bytes = File.ReadAllBytes(filePath);
        return new TodoSnapshot(MarkdownCodec.parse(strictUtf8.GetString(bytes)), getVersion(bytes));
    }

    public string readVersion() => getVersion(File.ReadAllBytes(filePath));

    public TodoSnapshot save(TodoDocument document, string expectedVersion) {
        var bytes = strictUtf8.GetBytes(MarkdownCodec.serialize(document));
        return write(bytes, expectedVersion);
    }

    public TodoSnapshot saveTodoChanges(TodoDocument document, TodoSnapshot baseline) {
        var latest = read();
        if (!baseline.document.hasSameTodos(latest.document)) { throw new DataConflictException(); }
        var merged = document.clone();
        merged.devices.Clear();
        merged.devices.AddRange(latest.document.devices);
        merged.hasDeviceSection = latest.document.hasDeviceSection;
        return save(merged, latest.version);
    }

    public TodoSnapshot registerDevices(IEnumerable<ConnectedDevice> devices) {
        var bytes = File.ReadAllBytes(filePath);
        var text = strictUtf8.GetString(bytes);
        var document = MarkdownCodec.parse(text);
        var added = false;
        foreach (var device in devices.DistinctBy(device => device.serial)) {
            if (document.devices.Any(existing => existing.serial == device.serial)) { continue; }
            document.devices.Add(new(device.serial, device.name, DeviceUsageState.UNMARKED, "", "", DateTimeOffset.Now));
            added = true;
        }
        return added ? write(strictUtf8.GetBytes(DeviceMarkdownCodec.replaceSection(text, document.devices)), getVersion(bytes))
            : new TodoSnapshot(document, getVersion(bytes));
    }

    public TodoSnapshot saveDevice(DeviceUsage changed, DeviceUsage expected) {
        changed.validate();
        if (changed.serial != expected.serial) { throw new ArgumentException("不可變更測試機序號。"); }
        var bytes = File.ReadAllBytes(filePath);
        var text = strictUtf8.GetString(bytes);
        var document = MarkdownCodec.parse(text);
        var index = document.devices.FindIndex(device => device.serial == expected.serial);
        if (index < 0 || document.devices[index] != expected) { throw new DataConflictException(); }
        document.devices[index] = changed;
        return write(strictUtf8.GetBytes(DeviceMarkdownCodec.replaceSection(text, document.devices)), getVersion(bytes));
    }

    private TodoSnapshot write(byte[] bytes, string expectedVersion) {
        // Validate before touching disk; unsupported content must not partially save.
        var validated = MarkdownCodec.parse(strictUtf8.GetString(bytes));
        using var fileLock = acquireLock();
        if (!File.Exists(filePath) || readVersion() != expectedVersion) {
            throw new DataConflictException();
        }
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (readVersion() != expectedVersion) {
                throw new DataConflictException();
            }
            File.Replace(temporaryPath, filePath, filePath + ".bak", true);
            return new TodoSnapshot(validated, getVersion(bytes));
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    private FileStream acquireLock() => new(filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static string getVersion(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
