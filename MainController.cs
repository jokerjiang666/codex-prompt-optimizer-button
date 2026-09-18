using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodexInputEnhancer.Models;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer;

public sealed class MainController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly WindowTracker _windowTracker = new();
    private readonly ComposerAdapter _composerAdapter = new();
    private readonly EditHistory _history = new();
    private readonly OverlayWindow _overlay = new();
    private readonly TextTransitionWindow _transition = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly SecretStore _secretStore;
    private readonly RecentHistoryStore _recentHistoryStore;
    private readonly CancellationTokenSource _trackerCts = new();
    private readonly object _diagnosticLock = new();
    private AppSettings _settings;
    private IOptimizerProvider _optimizer;

    private IntPtr _lastWindowHandle;
    private CancellationTokenSource? _optimizeCts;
    private bool _busy;
    private string? _lastDiagnosticState;
    private SettingsWindow? _settingsWindow;
    private string _lastObservedDraft = string.Empty;
    private string? _pendingSentText;
    private DateTimeOffset _pendingSentExpiresAt;
    private Rect? _lastPermissionAnchorBounds;
    private DateTimeOffset _lastAnchorSeenAt;
    private string? _lastSessionIdentity;
    private bool _lastGenerationInProgress;

    public MainController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _secretStore = new SecretStore(_settingsStore);
        _recentHistoryStore = new RecentHistoryStore(_settingsStore);
        _settings = _settingsStore.Load();
        _optimizer = OptimizerProviderFactory.Create(_settings, _secretStore.Read());

        _overlay.OptimizeRequested += OnOptimizeRequested;
        _overlay.UndoRequested += OnUndoRequested;
        _overlay.CancelRequested += OnCancelRequested;
        _overlay.SettingsRequested += (_, _) => ShowSettings(showRecent: false);
        _overlay.RecentRequested += (_, _) => ShowSettings(showRecent: true);
        _overlay.ClearRecentRequested += OnClearRecentRequested;
        _overlay.SetRecentCount(_recentHistoryStore.Load().Count);
    }

    public void Start()
    {
        _ = Task.Run(TrackLoopAsync);
    }

    public void Dispose()
    {
        _trackerCts.Cancel();
        _optimizeCts?.Cancel();
        _settingsWindow?.Close();
        _overlay.Close();
        _transition.Close();
    }

    private async Task TrackLoopAsync()
    {
        while (!_trackerCts.IsCancellationRequested)
        {
            TrackerSnapshot snapshot;
            try
            {
                snapshot = CollectTrackerSnapshot();
            }
            catch (Exception ex)
            {
                WriteDiagnostic($"tracker=error type={ex.GetType().Name}");
                try { await Task.Delay(350, _trackerCts.Token); } catch (TaskCanceledException) { break; }
                continue;
            }

            try
            {
                await _dispatcher.InvokeAsync(() => ApplyTrackerSnapshot(snapshot));
                await Task.Delay(350, _trackerCts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                WriteDiagnostic($"tracker=apply-error type={ex.GetType().Name}");
                try { await Task.Delay(350, _trackerCts.Token); } catch (TaskCanceledException) { break; }
            }
        }
    }

    private sealed record TrackerSnapshot(
        ComposerTarget? Target,
        bool CanWrite,
        string? Draft,
        string? SessionIdentity,
        bool GenerationInProgress,
        IntPtr ForegroundWindow);

    private TrackerSnapshot CollectTrackerSnapshot()
    {
        var target = _windowTracker.TryFindComposer();
        if (target is null)
            return new TrackerSnapshot(null, false, null, null, false, IntPtr.Zero);

        var foreground = NativeWindowStyles.GetForegroundWindow();
        if (!_composerAdapter.CanWrite(target))
            return new TrackerSnapshot(target, false, null, null, false, foreground);

        var draft = _composerAdapter.ReadText(target);
        var sessionIdentity = _windowTracker.TryGetSessionIdentity(target.WindowHandle);
        var generationInProgress = _windowTracker.IsGenerationInProgress(target.WindowHandle);
        return new TrackerSnapshot(target, true, draft, sessionIdentity, generationInProgress, foreground);
    }

    private void ApplyTrackerSnapshot(TrackerSnapshot snapshot)
    {
        var target = snapshot.Target;
        if (target is null)
        {
            _overlay.Hide();
            WriteDiagnostic("target=none overlay=hidden");
            return;
        }

        if (!snapshot.CanWrite)
        {
            _overlay.Hide();
            WriteDiagnostic($"target=found writable=false bounds={target.Bounds}");
            return;
        }

        var sessionIdentity = snapshot.SessionIdentity;
        var windowChanged = _lastWindowHandle != IntPtr.Zero && _lastWindowHandle != target.WindowHandle;
        var sessionChanged = !string.IsNullOrWhiteSpace(_lastSessionIdentity)
                             && !string.IsNullOrWhiteSpace(sessionIdentity)
                             && !string.Equals(_lastSessionIdentity, sessionIdentity, StringComparison.Ordinal);

        if (windowChanged || sessionChanged)
        {
            _history.Reset();
            _overlay.SetUndoVisible(false, animate: false);
            _lastObservedDraft = snapshot.Draft ?? string.Empty;
            _pendingSentText = null;
            _lastPermissionAnchorBounds = null;
            WriteDiagnostic(sessionChanged
                ? $"session=changed undo=hidden identity={sessionIdentity}"
                : "window=changed undo=hidden");
        }

        _lastWindowHandle = target.WindowHandle;
        if (!string.IsNullOrWhiteSpace(sessionIdentity))
            _lastSessionIdentity = sessionIdentity;

        ApplyDraftSnapshot(snapshot.Draft, snapshot.GenerationInProgress);

        if (target.PermissionAnchorBounds is { } freshAnchor && !freshAnchor.IsEmpty)
        {
            _lastPermissionAnchorBounds = freshAnchor;
            _lastAnchorSeenAt = DateTimeOffset.Now;
        }

        if (!NativeWindowStyles.IsSameProcess(snapshot.ForegroundWindow, target.WindowHandle))
        {
            _overlay.Hide();
            WriteDiagnostic($"target=found writable=true foregroundProcess=false host=0x{target.WindowHandle.ToInt64():X} fg=0x{snapshot.ForegroundWindow.ToInt64():X} bounds={target.Bounds}");
            return;
        }

        var anchor = target.PermissionAnchorBounds;
        if ((anchor is null || anchor.Value.IsEmpty)
            && _lastPermissionAnchorBounds is { } cached
            && DateTimeOffset.Now - _lastAnchorSeenAt <= TimeSpan.FromSeconds(2))
        {
            anchor = cached;
        }

        _overlay.SetTargetWindow(target.WindowHandle);
        _overlay.PositionNearComposer(target.Bounds, anchor);
        if (!_overlay.IsVisible) _overlay.Show();
        WriteDiagnostic(anchor is null || anchor.Value.IsEmpty
            ? $"target=found writable=true foregroundProcess=true overlay=shown bounds={target.Bounds} anchor=fallback"
            : $"target=found writable=true foregroundProcess=true overlay=shown bounds={target.Bounds} anchor={anchor}");
    }

    private void ApplyDraftSnapshot(string? draft, bool generationInProgress)
    {
        var current = draft ?? string.Empty;
        var generationStarted = generationInProgress && !_lastGenerationInProgress;
        var draftCleared = !string.IsNullOrWhiteSpace(_lastObservedDraft)
                           && string.IsNullOrWhiteSpace(current);
        var sentBeforeNextDraftSample = string.IsNullOrWhiteSpace(current)
                                        && _history.CanUndo
                                        && generationInProgress;

        if (generationStarted && _history.CanUndo)
        {
            _history.Reset();
            _overlay.SetUndoVisible(false, animate: false);
            if (_busy) _optimizeCts?.Cancel();
            WriteDiagnostic("generation=started undo=hidden");
        }

        if (draftCleared || sentBeforeNextDraftSample)
        {
            _history.Reset();
            _overlay.SetUndoVisible(false, animate: false);
            if (_busy) _optimizeCts?.Cancel();
            _pendingSentText = string.IsNullOrWhiteSpace(_lastObservedDraft)
                ? null
                : _lastObservedDraft;
            _pendingSentExpiresAt = DateTimeOffset.Now.AddSeconds(3);
            WriteDiagnostic(sentBeforeNextDraftSample
                ? "draft=sent undo=hidden"
                : "draft=cleared undo=hidden");
        }

        if (_pendingSentText is not null)
        {
            if (_settings.HistoryEnabled
                && DateTimeOffset.Now <= _pendingSentExpiresAt
                && generationInProgress)
            {
                _recentHistoryStore.Add(_pendingSentText);
                _pendingSentText = null;
                UpdateRecentCount();
                WriteDiagnostic("recent=recorded");
            }
            else if (DateTimeOffset.Now > _pendingSentExpiresAt || !string.IsNullOrWhiteSpace(current))
            {
                _pendingSentText = null;
            }
        }

        _lastObservedDraft = current;
        _lastGenerationInProgress = generationInProgress;
    }

    private async void OnOptimizeRequested(object? sender, EventArgs e)
    {
        if (_busy) return;

        var target = _windowTracker.TryFindComposer();
        if (target is null || !_composerAdapter.CanWrite(target))
        {
            WriteDiagnostic("optimize=ignored reason=no-current-composer");
            _overlay.ShowHint("请先输入内容");
            return;
        }

        var currentText = _composerAdapter.ReadText(target);
        if (string.IsNullOrWhiteSpace(currentText))
        {
            WriteDiagnostic("optimize=ignored reason=empty");
            _overlay.ShowHint("请先输入内容");
            return;
        }

        _busy = true;
        _overlay.SetOptimizing(true);
        _optimizeCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        WriteDiagnostic($"optimize=started provider={_settings.Provider} model={_settings.Model} inputChars={currentText.Length}");

        try
        {
            var provider = _optimizer;
            var optimized = await provider.OptimizeAsync(currentText, _optimizeCts.Token);
            if (_optimizeCts.IsCancellationRequested
                || string.Equals(currentText, optimized, StringComparison.Ordinal))
            {
                var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
                WriteDiagnostic(_optimizeCts.IsCancellationRequested
                    ? $"optimize=cancelled elapsedMs={elapsedMs}"
                    : $"optimize=nochange elapsedMs={elapsedMs} outputChars={optimized.Length}");
                return;
            }

            var draftNow = _composerAdapter.ReadText(target);
            if (draftNow is null || string.IsNullOrWhiteSpace(draftNow))
            {
                if (draftNow is not null)
                {
                    _history.Reset();
                    _overlay.SetUndoVisible(false, animate: false);
                }

                var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
                WriteDiagnostic(draftNow is null
                    ? $"optimize=discarded reason=target-unavailable elapsedMs={elapsedMs}"
                    : $"optimize=discarded reason=draft-cleared elapsedMs={elapsedMs}");
                return;
            }

            var success = await _transition.PlayReplaceAsync(
                currentText,
                optimized,
                target.Bounds,
                () => _composerAdapter.WriteText(target, optimized),
                reverse: false);

            if (success)
            {
                _history.CommitOptimization(currentText, optimized);
                _lastObservedDraft = optimized;
                _overlay.SetUndoVisible(_history.CanUndo);
                _overlay.PositionNearComposer(target.Bounds, target.PermissionAnchorBounds);
                var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
                WriteDiagnostic($"optimize=success elapsedMs={elapsedMs} outputChars={optimized.Length}");
            }
            else
            {
                var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
                WriteDiagnostic($"optimize=failed type=WriteTextFailed elapsedMs={elapsedMs}");
            }
        }
        catch (OperationCanceledException)
        {
            var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
            WriteDiagnostic($"optimize=cancelled elapsedMs={elapsedMs}");
        }
        catch (Exception ex)
        {
            // Provider failure must leave the Codex draft untouched.
            var elapsedMs = Math.Max(0, (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds);
            WriteDiagnostic($"optimize=failed type={ex.GetType().Name} elapsedMs={elapsedMs}");
        }
        finally
        {
            _optimizeCts?.Dispose();
            _optimizeCts = null;
            _busy = false;
            _overlay.SetOptimizing(false);
        }
    }

    private async void OnUndoRequested(object? sender, EventArgs e)
    {
        if (_busy) return;

        var target = _windowTracker.TryFindComposer();
        if (target is null || !_composerAdapter.CanWrite(target)) return;

        var previous = _history.Undo();
        if (previous is null) return;

        var current = _composerAdapter.ReadText(target) ?? string.Empty;
        _busy = true;

        try
        {
            _ = await _transition.PlayReplaceAsync(
                current,
                previous,
                target.Bounds,
                () => _composerAdapter.WriteText(target, previous),
                reverse: true);
        }
        finally
        {
            _busy = false;
            _overlay.SetUndoVisible(_history.CanUndo);
            _overlay.PositionNearComposer(target.Bounds, target.PermissionAnchorBounds);
            _lastObservedDraft = previous;
            WriteDiagnostic("undo=success");
        }
    }

    private void OnCancelRequested(object? sender, EventArgs e) => _optimizeCts?.Cancel();

    private void ShowSettings(bool showRecent)
    {
        if (_settingsWindow is not null)
        {
            if (showRecent) _settingsWindow.ShowRecentTab();
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settingsStore, _secretStore, _recentHistoryStore);
        _settingsWindow = window;
        window.SettingsSaved += OnSettingsSaved;
        window.RestoreRecentRequested += OnRestoreRecentRequested;
        window.RecentHistoryChanged += (_, _) => UpdateRecentCount();
        window.Closed += (_, _) => _settingsWindow = null;
        if (showRecent) window.ShowRecentTab();
        window.Show();
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _optimizeCts?.Cancel();
        _settings = settings;
        _optimizer = OptimizerProviderFactory.Create(_settings, _secretStore.Read());
        WriteDiagnostic($"settings=saved provider={_settings.Provider} model={_settings.Model}");
    }

    private void OnRestoreRecentRequested(string text)
    {
        var target = _windowTracker.TryFindComposer();
        if (target is null || !_composerAdapter.CanWrite(target)) return;

        var current = _composerAdapter.ReadText(target) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, text, StringComparison.Ordinal)
            && MessageBox.Show("当前输入框已有内容，确定替换为这条最近发送记录？",
                "Codex Input Enhancer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_composerAdapter.WriteText(target, text))
        {
            _history.Reset();
            _overlay.SetUndoVisible(false, animate: false);
            _lastObservedDraft = text;
            WriteDiagnostic("recent=restored");
        }
    }

    private void OnClearRecentRequested(object? sender, EventArgs e)
    {
        if (_recentHistoryStore.Load().Count == 0) return;
        if (MessageBox.Show("确定清空最近发送记录？", "Codex Input Enhancer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _recentHistoryStore.Clear();
        UpdateRecentCount();
    }

    private void UpdateRecentCount()
    {
        var count = _recentHistoryStore.Load().Count;
        _overlay.SetRecentCount(count);
        _settingsWindow?.RefreshRecent();
    }

    private void WriteDiagnostic(string state)
    {
        lock (_diagnosticLock)
        {
            if (string.Equals(_lastDiagnosticState, state, StringComparison.Ordinal)) return;
            _lastDiagnosticState = state;

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "runtime-status.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {state}{Environment.NewLine}");
            }
            catch { }
        }
    }

}

