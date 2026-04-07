using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinuxExplorer.ViewModels;

namespace LinuxExplorer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Linux Explorer\nVersion 1.0\n\nA Windows Explorer-like application for reading and writing Linux ext2/3/4 partitions.\n\nPowered by DiscUtils.",
            "About Linux Explorer",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFiles.Count == 1)
        {
            var item = vm.SelectedFiles[0];
            if (item.IsDirectory)
            {
                vm.NavigateCommand.Execute(item.FullPath);
            }
        }
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ListView lv)
        {
            vm.SelectedFiles.Clear();
            foreach (FileItemViewModel item in lv.SelectedItems)
            {
                vm.SelectedFiles.Add(item);
            }
        }
    }
}
