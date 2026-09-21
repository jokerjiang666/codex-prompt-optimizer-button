using System.Diagnostics;
using System.IO;
using System.Text;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class CodexCliProvider : IOptimizerProvider
{
    private readonly string _codexExe;
    private readonly string _model;
    private readonly string _reasoningEffort;

    public CodexCliProvider(AppSettings settings)
    {
        _codexExe = FindNativeCodexExecutable()
            ?? throw new FileNotFoundException("未找到 Codex 原生 codex.exe。请先安装或启动 Codex Desktop。");
        _model = settings.Model.Trim();
        _reasoningEffort = string.IsNullOrWhiteSpace(settings.ReasoningEffort)
            ? "low"
            : settings.ReasoningEffort.Trim();
    }

    public async Task<string> OptimizeAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(AppContext.BaseDirectory, "temp");
        Directory.CreateDirectory(tempRoot);
        var resultFile = Path.Combine(tempRoot, $"codex-input-enhancer-{Guid.NewGuid():N}.txt");

        try
        {
            using var process = new Process
            {
                StartInfo = BuildStartInfo(resultFile),
                EnableRaisingEvents = true
            };

            if (!process.Start())
                throw new InvalidOperationException("Codex CLI 启动失败。");

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
            });

            await process.StandardInput.WriteAsync(BuildPrompt(systemPrompt, userPrompt));
            process.StandardInput.Close();

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"Codex CLI 退出码 {process.ExitCode}"
                    : stderr.Trim());

            if (!File.Exists(resultFile))
                throw new InvalidOperationException("Codex CLI 未返回优化文本。");

            var result = (await File.ReadAllTextAsync(resultFile, cancellationToken)).Trim();
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("优化结果为空。");

            return result;
        }
        finally
        {
            try
            {
                if (File.Exists(resultFile)) File.Delete(resultFile);
            }
            catch { }
        }
    }

    private ProcessStartInfo BuildStartInfo(string resultFile)
    {
        var info = new ProcessStartInfo
        {
            FileName = _codexExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = AppContext.BaseDirectory
        };

        info.ArgumentList.Add("-a");
        info.ArgumentList.Add("never");
        if (!string.IsNullOrWhiteSpace(_model))
        {
            info.ArgumentList.Add("-m");
            info.ArgumentList.Add(_model);
        }
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add($"model_reasoning_effort=\"{_reasoningEffort}\"");
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--skip-git-repo-check");
        info.ArgumentList.Add("--ephemeral");
        info.ArgumentList.Add("--ignore-rules");
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add("read-only");
        info.ArgumentList.Add("--color");
        info.ArgumentList.Add("never");
        info.ArgumentList.Add("--output-last-message");
        info.ArgumentList.Add(resultFile);
        info.ArgumentList.Add("-");
        return info;
    }

    private static string BuildPrompt(string systemPrompt, string userPrompt)
    {
        var system = string.IsNullOrWhiteSpace(systemPrompt)
            ? AppSettings.DefaultOptimizationPrompt
            : systemPrompt.Trim();
        return $"{system}{Environment.NewLine}{Environment.NewLine}{userPrompt}";
    }

    private static string? FindNativeCodexExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(binRoot))
        {
            var file = Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .FirstOrDefault();
            if (file is not null) return file.FullName;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }
}
