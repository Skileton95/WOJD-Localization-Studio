using System.IO;

namespace WOJD.LocalizationStudio.Services;

public sealed class ExternalFileChangedEventArgs : EventArgs
{
    public ExternalFileChangedEventArgs(string path, DateTime lastWriteUtc, long length)
    {
        Path = path;
        LastWriteUtc = lastWriteUtc;
        Length = length;
    }

    public string Path { get; }
    public DateTime LastWriteUtc { get; }
    public long Length { get; }
}

public sealed class ExternalFileMonitorService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly object _sync = new();
    private string? _path;
    private DateTime _knownWriteUtc;
    private long _knownLength;
    private Timer? _debounce;
    private bool _disposed;

    public event EventHandler<ExternalFileChangedEventArgs>? Changed;

    public void Watch(string? path)
    {
        lock (_sync)
        {
            StopCore();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            _path = Path.GetFullPath(path);
            var info = new FileInfo(_path);
            _knownWriteUtc = info.LastWriteTimeUtc;
            _knownLength = info.Length;

            _watcher = new FileSystemWatcher(
                info.DirectoryName!,
                info.Name)
            {
                NotifyFilter = NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Changed += OnWatcherChanged;
            _watcher.Created += OnWatcherChanged;
            _watcher.Renamed += OnWatcherChanged;
        }
    }

    public void AcknowledgeCurrent()
    {
        lock (_sync)
        {
            if (_path is null || !File.Exists(_path))
                return;

            var info = new FileInfo(_path);
            _knownWriteUtc = info.LastWriteTimeUtc;
            _knownLength = info.Length;
        }
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        lock (_sync)
        {
            if (_disposed || _path is null)
                return;

            _debounce?.Dispose();
            _debounce = new Timer(_ => Probe(), null, 650, Timeout.Infinite);
        }
    }

    private void Probe()
    {
        ExternalFileChangedEventArgs? args = null;

        lock (_sync)
        {
            if (_disposed || _path is null || !File.Exists(_path))
                return;

            try
            {
                var info = new FileInfo(_path);
                if (info.LastWriteTimeUtc == _knownWriteUtc &&
                    info.Length == _knownLength)
                {
                    return;
                }

                args = new ExternalFileChangedEventArgs(
                    _path,
                    info.LastWriteTimeUtc,
                    info.Length);
            }
            catch
            {
                return;
            }
        }

        if (args is not null)
            Changed?.Invoke(this, args);
    }

    private void StopCore()
    {
        _debounce?.Dispose();
        _debounce = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Created -= OnWatcherChanged;
            _watcher.Renamed -= OnWatcherChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        _path = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopCore();
        }
    }
}
