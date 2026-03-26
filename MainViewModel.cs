using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

    private string? _selectedDrive;
    public string? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            _selectedDrive = value;
            OnPropertyChanged();
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDetailsText));
        }
    }

    private ResultRow? _selectedFile;
    public ResultRow? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDetailsText));
        }
    }

    public string SelectedDetailsText
    {
        get
        {
            var item = SelectedFile ?? SelectedFolder;
            if (item is null)
                return "Select a file or folder.";

            return $"{item.Path}  •  {item.SizeHuman}";
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
        SelectedFolder = null;
        SelectedFile = null;

        ProgressPercent = 0;
        StatusText = "Scanning...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressPercent = p.Percent;
                StatusText = p.Message;
            });

            var result = await Task.Run(() =>
                NtfsMftScanner.ScanDrive(SelectedDrive, progress, _cts.Token), _cts.Token);

            foreach (var r in result.TopFolders)
                TopFolders.Add(r);

            foreach (var r in result.TopFiles)
                TopFiles.Add(r);

            BuildTreemap(1200, 220);

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

    private void BuildTreemap(double totalWidth, double totalHeight)
    {
        TreemapItems.Clear();

        var items = TopFiles
            .Where(x => x.Bytes > 0)
            .OrderByDescending(x => x.Bytes)
            .Take(80)
            .ToList();

        long totalBytes = items.Sum(x => x.Bytes);
        if (totalBytes <= 0)
            return;

        double x = 0;
        var random = new Random(42);

        foreach (var item in items)
        {
            double width = totalWidth * item.Bytes / totalBytes;

            if (width < 2)
                width = 2;

            byte r = (byte)random.Next(70, 210);
            byte g = (byte)random.Next(70, 210);
            byte b = (byte)random.Next(70, 210);

            TreemapItems.Add(new TreemapItem
            {
                Path = item.Path,
                Bytes = item.Bytes,
                X = x,
                Y = 0,
                Width = width,
                Height = totalHeight,
                Fill = new SolidColorBrush(Color.FromRgb(r, g, b))
            });

            x += width;
        }

        OnPropertyChanged(nameof(TreemapItems));
    }

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

public sealed record ResultRow(string Path, long Bytes)
{
    public string SizeHuman => Utils.FormatBytes(Bytes);
}

public sealed record ScanProgress(double Percent, string Message);

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