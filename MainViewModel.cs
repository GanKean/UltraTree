<<<<<<< HEAD
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
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
=======
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f

namespace UltraTree;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Drives { get; } = new();
    public ObservableCollection<ResultRow> TopFolders { get; } = new();
    public ObservableCollection<ResultRow> TopFiles { get; } = new();

<<<<<<< HEAD
    private readonly ConcurrentDictionary<string, DirStats> _dirStatsCache =
        new(StringComparer.OrdinalIgnoreCase);

=======
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    private string? _selectedDrive;
    public string? SelectedDrive
    {
        get => _selectedDrive;
<<<<<<< HEAD
        set
        {
            _selectedDrive = value;
            OnPropertyChanged();
            UpdateDriveStats();
            OnPropertyChanged(nameof(SelectionText));
        }
=======
        set { _selectedDrive = value; OnPropertyChanged(); }
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    }

    private string _statusText = "Idle.";
    public string StatusText
    {
        get => _statusText;
<<<<<<< HEAD
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
=======
        set { _statusText = value; OnPropertyChanged(); }
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
<<<<<<< HEAD
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

    private ISeries[] _folderPieSeries = Array.Empty<ISeries>();
    public ISeries[] FolderPieSeries
    {
        get => _folderPieSeries;
        set
        {
            _folderPieSeries = value;
            OnPropertyChanged();
        }
    }

    private ISeries[] _filePieSeries = Array.Empty<ISeries>();
    public ISeries[] FilePieSeries
    {
        get => _filePieSeries;
        set
        {
            _filePieSeries = value;
            OnPropertyChanged();
        }
=======
        set { _progressPercent = value; OnPropertyChanged(); }
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
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
<<<<<<< HEAD
        if (string.IsNullOrWhiteSpace(SelectedDrive))
            return;
=======
        if (string.IsNullOrWhiteSpace(SelectedDrive)) return;
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f

        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        TopFolders.Clear();
        TopFiles.Clear();
<<<<<<< HEAD
        _dirStatsCache.Clear();

        SelectedFolder = null;
        SelectedFile = null;

        FolderPieSeries = Array.Empty<ISeries>();
        FilePieSeries = Array.Empty<ISeries>();

        ProgressPercent = 0;
        StatusText = "Scanning...";

        try
        {
            UpdateDriveStats();

=======
        ProgressPercent = 0;
        StatusText = "Scanning…";

        try
        {
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressPercent = p.Percent;
                StatusText = p.Message;
            });

            var result = await Task.Run(() =>
                NtfsMftScanner.ScanDrive(SelectedDrive, progress, _cts.Token), _cts.Token);

<<<<<<< HEAD
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

            BuildPieCharts();

            UpdateDriveStats();

=======
            foreach (var r in result.TopFolders)
                TopFolders.Add(r);

            foreach (var r in result.TopFiles)
                TopFiles.Add(r);

>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
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
<<<<<<< HEAD
            _cts?.Dispose();
=======
            _cts.Dispose();
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
            _cts = null;
            RaiseCanExecuteChanged();
        }
    }

<<<<<<< HEAD
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

    private void BuildPieCharts()
    {
        FolderPieSeries = BuildPieSeries(
            TopFolders.OrderByDescending(x => x.Bytes).Take(6).ToList(),
            includeOther: false);

        FilePieSeries = BuildPieSeries(
            TopFiles.Where(x => x.Bytes > 0 && !x.Path.Contains("$"))
                    .OrderByDescending(x => x.Bytes)
                    .Take(6)
                    .ToList(),
            includeOther: false);
    }

    private ISeries[] BuildPieSeries(List<ResultRow> rows, bool includeOther)
    {
        var palette = new[]
        {
            new SKColor(7, 43, 71),
            new SKColor(12, 76, 125),
            new SKColor(17, 71, 111),
            new SKColor(11, 58, 94),
            new SKColor(32, 96, 144),
            new SKColor(66, 133, 194),
            new SKColor(98, 155, 210)
        };

        var series = new List<ISeries>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var color = palette[i % palette.Length];

            series.Add(new PieSeries<double>
            {
                Values = new[] { (double)row.Bytes },
                Name = ShortName(row.Path),
                Fill = new SolidColorPaint(color),
                Stroke = new SolidColorPaint(new SKColor(12, 76, 125), 2),
                DataLabelsSize = 12,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                HoverPushout = 6
            });
        }

        if (includeOther && rows.Count > 0)
        {
            long shown = rows.Sum(x => x.Bytes);
            long total = rows.Sum(x => x.Bytes);
            long other = total - shown;

            if (other > 0)
            {
                series.Add(new PieSeries<double>
                {
                    Values = new[] { (double)other },
                    Name = "Other",
                    Fill = new SolidColorPaint(new SKColor(80, 80, 80)),
                    Stroke = new SolidColorPaint(new SKColor(12, 76, 125), 2),
                    DataLabelsSize = 12,
                    DataLabelsPosition = PolarLabelsPosition.Outer,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    HoverPushout = 6
                });
            }
        }

        return series.ToArray();
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

    private static string ShortName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        int i = path.LastIndexOf('\\');
        return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
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

=======
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    private void RaiseCanExecuteChanged()
    {
        (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
<<<<<<< HEAD

=======
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _can;
    private readonly Action<object?> _run;
<<<<<<< HEAD

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

=======
    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

<<<<<<< HEAD
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
=======
public sealed record ResultRow(string Path, long Bytes)
{
    public string SizeHuman => Utils.FormatBytes(Bytes);
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
}

public sealed record ScanProgress(double Percent, string Message);

<<<<<<< HEAD
public sealed record DirStats(long Bytes, long AllocatedBytes, int FileCount, int FolderCount);

=======
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
public static class Utils
{
    public static string FormatBytes(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB", "PB"];
        double b = bytes;
        int i = 0;
<<<<<<< HEAD

        while (b >= 1024 && i < u.Length - 1)
        {
            b /= 1024;
            i++;
        }

        return $"{b:0.##} {u[i]}";
    }
}
=======
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.##} {u[i]}";
    }
}
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
