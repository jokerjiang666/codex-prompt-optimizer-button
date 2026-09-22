using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer.ContextTests;

/// <summary>内存版 IContextIo：所有上下文访问都经过它，便于断言"关闭态零 I/O"。</summary>
internal sealed class FakeIo : IContextIo
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _writes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    internal int FileReads { get; private set; }

    internal int DirectoryReads { get; private set; }

    internal int ProcessRuns { get; private set; }

    internal string Home { get; set; } = @"C:\fake\.codex";

    internal Dictionary<string, string?> ProcessOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal void AddDirectory(string path) => _directories.Add(path);

    internal void AddFile(string path, string content, DateTimeOffset? lastWrite = null)
    {
        _files[path] = Encoding.UTF8.GetBytes(content);
        _writes[path] = lastWrite ?? DateTimeOffset.UtcNow;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) _directories.Add(directory!);
    }

    internal void RemoveFile(string path)
    {
        _files.Remove(path);
        _writes.Remove(path);
    }

    public string GetCodexHome() => Home;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path)
    {
        if (_directories.Contains(path)) return true;
        var prefix = path.TrimEnd('\\') + "\\";
        return _files.Keys.Any(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public string[] ReadAllLines(string path)
    {
        FileReads++;
        return Encoding.UTF8.GetString(_files[path]).Replace("\r\n", "\n").Split('\n');
    }

    public string ReadAllText(string path)
    {
        FileReads++;
        return Encoding.UTF8.GetString(_files[path]);
    }

    public string ReadFirstLine(string path)
    {
        FileReads++;
        var text = Encoding.UTF8.GetString(_files[path]);
        var index = text.IndexOf('\n');
        return (index < 0 ? text : text[..index]).TrimEnd('\r');
    }

    public long GetLength(string path) => _files[path].Length;

    public DateTimeOffset GetLastWriteUtc(string path) => _writes[path];

    public IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption option)
    {
        DirectoryReads++;
        var prefix = directory.TrimEnd('\\') + "\\";
        foreach (var file in _files.Keys.ToArray())
        {
            var parent = Path.GetDirectoryName(file) ?? string.Empty;
            var inScope = option == SearchOption.AllDirectories
                ? file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase);
            if (!inScope) continue;
            if (!Wildcard(Path.GetFileName(file), pattern)) continue;
            yield return file;
        }
    }

    public string? RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        ProcessRuns++;
        foreach (var pair in ProcessOutputs)
        {
            if (arguments.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }

        return null;
    }

    public TailReadResult ReadFrom(string path, long offset, int maxBytes)
    {
        FileReads++;
        var bytes = _files[path];
        var length = bytes.LongLength;
        var reset = false;
        if (offset <= 0 || offset > length)
        {
            reset = true;
            offset = Math.Max(0, length - maxBytes);
        }

        var remaining = length - offset;
        if (remaining <= 0) return new TailReadResult([], length, reset);

        var take = (int)Math.Min(remaining, maxBytes);
        var text = Encoding.UTF8.GetString(bytes, (int)offset, take);
        var raw = text.Split('\n');
        var lines = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i == 0 && offset > 0) continue;
            var line = raw[i].TrimEnd('\r');
            if (line.Length > 0) lines.Add(line);
        }

        return new TailReadResult(lines, offset + take, reset);
    }

    private static bool Wildcard(string name, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
    }
}

/// <summary>一旦被触碰就抛异常的探针：用于证明关闭态"零 I/O"。</summary>
internal sealed class ForbiddenIo : IContextIo
{
    internal int Calls { get; private set; }

    private void Hit()
    {
        Calls++;
        throw new InvalidOperationException("关闭态不允许触碰上下文 I/O");
    }

    public string GetCodexHome() { Hit(); return string.Empty; }

    public bool FileExists(string path) { Hit(); return false; }

    public bool DirectoryExists(string path) { Hit(); return false; }

    public string[] ReadAllLines(string path) { Hit(); return []; }

    public string ReadAllText(string path) { Hit(); return string.Empty; }

    public string ReadFirstLine(string path) { Hit(); return string.Empty; }

    public long GetLength(string path) { Hit(); return 0; }

    public DateTimeOffset GetLastWriteUtc(string path) { Hit(); return DateTimeOffset.MinValue; }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption option) { Hit(); return []; }

    public string? RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs) { Hit(); return null; }

    public TailReadResult ReadFrom(string path, long offset, int maxBytes) { Hit(); return new TailReadResult([], 0, false); }
}