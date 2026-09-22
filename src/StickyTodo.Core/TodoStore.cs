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
