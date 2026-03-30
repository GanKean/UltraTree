<<<<<<< HEAD
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
=======
<<<<<<< HEAD
﻿using System.Windows;
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f

namespace UltraTree;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
<<<<<<< HEAD

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
}
=======
}
=======
﻿using System.Windows;

namespace UltraTree;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
>>>>>>> b78f4c63145062f6840ee5adb0fbc7f8b5fe4d52
>>>>>>> 81f3dc24198249e33d3e1fd3f5af8b5c15ee6f9f
