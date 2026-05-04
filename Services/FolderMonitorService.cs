using System.IO;

namespace Supertech.AutoUploadVideo.Services;

public sealed class FolderMonitorService : IDisposable
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;

    public event Func<string, Task>? FileReady;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public void Start(string folder)
    {
        Stop();
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

        _cts = new CancellationTokenSource();
        _watcher = new FileSystemWatcher(folder, "*.mp4")
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged;

        _ = Task.Run(() => ScanLoopAsync(folder, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_cts == null) return;
        _ = TryEmitWhenStableAsync(e.FullPath, _cts.Token);
    }

    private async Task ScanLoopAsync(string folder, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.mp4"))
            {
                _ = TryEmitWhenStableAsync(file, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ContinueWith(_ => { });
        }
    }

    private async Task TryEmitWhenStableAsync(string path, CancellationToken cancellationToken)
    {
        if (!_seen.Add(path)) return;
        if (!await WaitForStableFileAsync(path, cancellationToken))
        {
            _seen.Remove(path);
            return;
        }
        if (FileReady != null)
        {
            await FileReady.Invoke(path);
        }
    }

    private static async Task<bool> WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        long lastSize = -1;
        var stableCount = 0;
        for (var i = 0; i < 90 && !cancellationToken.IsCancellationRequested; i++)
        {
            if (!File.Exists(path))
            {
                await Task.Delay(1000, cancellationToken);
                continue;
            }
            var info = new FileInfo(path);
            if (info.Length > 0 && info.Length == lastSize && CanOpenExclusive(path))
            {
                stableCount++;
                if (stableCount >= 3) return true;
            }
            else
            {
                stableCount = 0;
                lastSize = info.Length;
            }
            await Task.Delay(1000, cancellationToken);
        }
        return false;
    }

    private static bool CanOpenExclusive(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}
