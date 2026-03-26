using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace UltraTree;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void FoldersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FoldersGrid.SelectedItem is not ResultRow row)
            return;

        if (Directory.Exists(row.Path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{row.Path}\"",
                UseShellExecute = true
            });
        }
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is not ResultRow row)
            return;

        if (File.Exists(row.Path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{row.Path}\"",
                UseShellExecute = true
            });
        }
    }

    private void TreemapItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TreemapItem item)
            return;

        if (DataContext is not MainViewModel vm)
            return;

        var match = vm.TopFiles.FirstOrDefault(x => x.Path == item.Path);
        if (match is not null)
            vm.SelectedFile = match;
    }
}