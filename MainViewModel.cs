using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace UltraTree;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Drives { get; } = new();
    public ObservableCollection<ResultRow> TopFolders { get; } = new();
    public ObservableCollection<ResultRow> TopFiles { get; } = new();
    public ObservableCollection<TreemapItem> TreemapItems { get; } = new();

    private readonly ConcurrentDictionary<string, DirStats> _dirStatsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _selectedDrive;
    public string? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            _selectedDrive = value;
            OnPropertyChanged();
            UpdateDriveStats();
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    private string _statusText = "Idle.";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    private ResultRow? _selectedFolder;
    public ResultRow? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            _selectedFolder = value;
            if (value is not null)
                SelectedFile = null;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    private ResultRow? _selectedFile;
    public ResultRow? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            if (value is not null)
                _selectedFolder = null;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFolder));
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    public string SelectionText
    {
        get
        {
            if (SelectedFile is not null) return SelectedFile.Path;
            if (SelectedFolder is not null) return SelectedFolder.Path;
            return SelectedDrive ?? "--";
        }
    }

    private string _totalSpaceText = "--";
    public string TotalSpaceText
    {
        get => _totalSpaceText;
        set
        {
            _totalSpaceText = value;
            OnPropertyChanged();
        }
    }

    private string _spaceUsedText = "--";
    public string SpaceUsedText
    {
        get => _spaceUsedText;
        set
        {
            _spaceUsedText = value;
            OnPropertyChanged();
        }
    }

    private string _spaceFreeText = "--";
    public string SpaceFreeText
    {
        get => _spaceFreeText;
        set
        {
            _spaceFreeText = value;
            OnPropertyChanged();
        }
    }

    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }

    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => x.Name.TrimEnd('\\')))
            Drives.Add(d);

        SelectedDrive = Drives.FirstOrDefault();

        ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => _cts is null);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => _cts is not null);
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDrive))
            return;

        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        TopFolders.Clear();
        TopFiles.Clear();
        TreemapItems.Clear();
        _dirStatsCache.Clear();

        SelectedFolder = null;
        SelectedFile = null;

        ProgressPercent = 0;
        StatusText = "Scanning...";

        try
        {
            UpdateDriveStats();

            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressPercent = p.Percent;
                StatusText = p.Message;
            });

            var result = await Task.Run(() =>
                NtfsMftScanner.ScanDrive(SelectedDrive, progress, _cts.Token), _cts.Token);

            long driveBytes = result.TotalBytes;
            uint clusterSize = GetClusterSize(SelectedDrive);

            var enrichedFolders = await Task.Run(() =>
                result.TopFolders
                    .Select(r => EnrichFolderRow(r, driveBytes, clusterSize))
                    .ToList(), _cts.Token);

            var enrichedFiles = await Task.Run(() =>
                result.TopFiles
                    .Select(r => EnrichFileRow(r, driveBytes, clusterSize))
                    .ToList(), _cts.Token);

            foreach (var r in enrichedFolders)
                TopFolders.Add(r);

            foreach (var r in enrichedFiles)
                TopFiles.Add(r);

            BuildTreemap(1200, 220);

            UpdateDriveStats();

            StatusText = $"Done. Files: {result.FileCount:n0}  Bytes: {Utils.FormatBytes(result.TotalBytes)}";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

    private ResultRow EnrichFolderRow(ResultRow row, long driveBytes, uint clusterSize)
    {
        var stats = GetDirectoryStatsSafe(row.Path, clusterSize);

        return new ResultRow(row.Path, row.Bytes)
        {
            DisplayName = row.Path,
            AllocatedBytes = stats.AllocatedBytes,
            PercentOfParent = driveBytes > 0 ? (double)row.Bytes / driveBytes * 100.0 : 0,
            PercentOfDrive = driveBytes > 0 ? (double)row.Bytes / driveBytes * 100.0 : 0,
            ItemCount = stats.FileCount + stats.FolderCount,
            FileCount = stats.FileCount,
            FolderCount = stats.FolderCount,
            Modified = SafeGetDirectoryModified(row.Path)
        };
    }

    private ResultRow EnrichFileRow(ResultRow row, long driveBytes, uint clusterSize)
    {
        DateTime modified = DateTime.MinValue;

        try
        {
            modified = File.GetLastWriteTime(row.Path);
        }
        catch
        {
        }

        return new ResultRow(row.Path, row.Bytes)
        {
            DisplayName = row.Path,
            AllocatedBytes = RoundUpToCluster(row.Bytes, clusterSize),
            PercentOfParent = driveBytes > 0 ? (double)row.Bytes / driveBytes * 100.0 : 0,
            PercentOfDrive = driveBytes > 0 ? (double)row.Bytes / driveBytes * 100.0 : 0,
            ItemCount = 1,
            FileCount = 1,
            FolderCount = 0,
            Modified = modified
        };
    }

    private DirStats GetDirectoryStatsSafe(string path, uint clusterSize)
    {
        if (_dirStatsCache.TryGetValue(path, out var cached))
            return cached;

        long bytes = 0;
        long allocatedBytes = 0;
        int fileCount = 0;
        int folderCount = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(file);
                    bytes += fi.Length;
                    allocatedBytes += RoundUpToCluster(fi.Length, clusterSize);
                    fileCount++;
                }
                catch
                {
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                folderCount++;
            }
        }
        catch
        {
        }

        var stats = new DirStats(bytes, allocatedBytes, fileCount, folderCount);
        _dirStatsCache[path] = stats;
        return stats;
    }

    private void BuildTreemap(double totalWidth, double totalHeight)
    {
        TreemapItems.Clear();

        var items = TopFiles
            .Where(x => x.Bytes > 0 && !x.Path.Contains("$"))
            .OrderByDescending(x => x.Bytes)
            .Take(100)
            .ToList();

        long totalBytes = items.Sum(x => x.Bytes);
        if (totalBytes <= 0)
            return;

        double x = 0;
        double y = 0;
        double rowHeight = totalHeight / 4.0;
        int itemsPerRow = Math.Max(1, items.Count / 4);

        var random = new Random(42);
        int count = 0;

        foreach (var item in items)
        {
            double width = totalWidth * item.Bytes / totalBytes;
            if (width < 3)
                width = 3;

            byte r = (byte)random.Next(80, 200);
            byte g = (byte)random.Next(80, 200);
            byte b = (byte)random.Next(80, 200);

            TreemapItems.Add(new TreemapItem
            {
                Path = item.Path,
                Bytes = item.Bytes,
                X = x,
                Y = y,
                Width = width,
                Height = rowHeight,
                Fill = new SolidColorBrush(Color.FromRgb(r, g, b))
            });

            x += width;
            count++;

            if (count >= itemsPerRow)
            {
                x = 0;
                y += rowHeight;
                count = 0;
            }
        }

        OnPropertyChanged(nameof(TreemapItems));
    }

    private void UpdateDriveStats()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedDrive))
            {
                TotalSpaceText = "--";
                SpaceUsedText = "--";
                SpaceFreeText = "--";
                return;
            }

            var drive = new DriveInfo(SelectedDrive);
            if (!drive.IsReady)
            {
                TotalSpaceText = "--";
                SpaceUsedText = "--";
                SpaceFreeText = "--";
                return;
            }

            long total = drive.TotalSize;
            long free = drive.AvailableFreeSpace;
            long used = total - free;

            TotalSpaceText = Utils.FormatBytes(total);
            SpaceUsedText = $"{Utils.FormatBytes(used)}  ({(total > 0 ? used * 100.0 / total : 0):0.0}%)";
            SpaceFreeText = $"{Utils.FormatBytes(free)}  ({(total > 0 ? free * 100.0 / total : 0):0.0}%)";
        }
        catch
        {
            TotalSpaceText = "--";
            SpaceUsedText = "--";
            SpaceFreeText = "--";
        }
    }

    private static DateTime SafeGetDirectoryModified(string path)
    {
        try
        {
            return Directory.GetLastWriteTime(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static long RoundUpToCluster(long bytes, uint clusterSize)
    {
        if (bytes <= 0 || clusterSize == 0)
            return bytes;

        long cluster = clusterSize;
        return ((bytes + cluster - 1) / cluster) * cluster;
    }

    private static uint GetClusterSize(string driveRoot)
    {
        try
        {
            if (!driveRoot.EndsWith("\\")) driveRoot += "\\";

            if (GetDiskFreeSpace(driveRoot, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _))
                return sectorsPerCluster * bytesPerSector;
        }
        catch
        {
        }

        return 4096;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetDiskFreeSpace(
        string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);

    private void RaiseCanExecuteChanged()
    {
        (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _can;
    private readonly Action<object?> _run;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ResultRow
{
    public ResultRow(string path, long bytes)
    {
        Path = path;
        Bytes = bytes;
        DisplayName = path;
    }

    public string Path { get; set; }
    public string DisplayName { get; set; }
    public long Bytes { get; set; }
    public long AllocatedBytes { get; set; }
    public double PercentOfParent { get; set; }
    public double PercentOfDrive { get; set; }
    public int ItemCount { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public DateTime Modified { get; set; }

    public string SizeHuman => Utils.FormatBytes(Bytes);
    public string AllocatedHuman => Utils.FormatBytes(AllocatedBytes);
    public string PercentOfParentText => $"{PercentOfParent:0.0}%";
    public string PercentText => $"{PercentOfDrive:0.0}%";
    public string ItemCountText => ItemCount.ToString("n0");
    public string FileCountText => FileCount.ToString("n0");
    public string FolderCountText => FolderCount.ToString("n0");
    public string ModifiedText => Modified == DateTime.MinValue ? "" : Modified.ToString("yyyy-MM-dd HH:mm");
}

public sealed record ScanProgress(double Percent, string Message);

public sealed record DirStats(long Bytes, long AllocatedBytes, int FileCount, int FolderCount);

public static class Utils
{
    public static string FormatBytes(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB", "PB"];
        double b = bytes;
        int i = 0;

        while (b >= 1024 && i < u.Length - 1)
        {
            b /= 1024;
            i++;
        }

        return $"{b:0.##} {u[i]}";
    }
}