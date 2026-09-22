using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexInputEnhancer.Services;

/// <summary>默认实现：真实文件系统 + 只读 git 子进程。每次调用都受超时保护。</summary>
internal sealed class DefaultContextIo : IContextIo
{
    internal static readonly DefaultContextIo Instance = new();

    public string GetCodexHome()
    {
        var custom = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(custom)) return custom!;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".codex");
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string ReadFirstLine(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadLine() ?? string.Empty;
    }

    public long GetLength(string path) => new FileInfo(path).Length;

    public DateTimeOffset GetLastWriteUtc(string path) =>
        new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

    public TailReadResult ReadFrom(string path, long offset, int maxBytes)
    {
        using var stream = OpenRead(path);
        var length = stream.Length;
        var reset = false;
        if (offset <= 0 || offset > length)
        {
            reset = true;
            offset = Math.Max(0, length - maxBytes);
        }

        var remaining = length - offset;
        if (remaining <= 0) return new TailReadResult([], length, reset);

        var take = (int)Math.Min(remaining, maxBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[take];
        var read = 0;
        while (read < take)
        {
            var chunk = stream.Read(buffer, read, take - read);
            if (chunk <= 0) break;
            read += chunk;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var raw = text.Split('\n');
        var lines = new List<string>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            // offset 落在行中间时，第一段是半行，丢弃。
            if (i == 0 && offset > 0) continue;
            var line = raw[i].TrimEnd('\r');
            if (line.Length > 0) lines.Add(line);
        }

        return new TailReadResult(lines, offset + read, reset);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, option);
        }
        catch
        {
            return [];
        }
    }

    public string? RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            if (!process.Start()) return null;

            // 先挂上异步读取，避免输出超过管道缓冲时死锁。
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}