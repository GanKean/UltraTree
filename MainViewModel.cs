using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;

namespace UltraTree;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Drives { get; } = new();
    public ObservableCollection<ResultRow> TopFolders { get; } = new();
    public ObservableCollection<ResultRow> TopFiles { get; } = new();

    private string? _selectedDrive;
    public string? SelectedDrive
    {
        get => _selectedDrive;
        set { _selectedDrive = value; OnPropertyChanged(); }
    }

    private string _statusText = "Idle.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
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
        if (string.IsNullOrWhiteSpace(SelectedDrive)) return;

        _cts = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        TopFolders.Clear();
        TopFiles.Clear();
        ProgressPercent = 0;
        StatusText = "Scanning…";

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
            _cts.Dispose();
            _cts = null;
            RaiseCanExecuteChanged();
        }
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
    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
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
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.##} {u[i]}";
    }
}
