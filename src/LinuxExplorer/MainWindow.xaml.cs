using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinuxExplorer.ViewModels;

namespace LinuxExplorer;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>Initializes the main window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuFormatPartition_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.FormatDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Linux Explorer v1.0\n\nA Windows Explorer-like browser for ext2/ext3/ext4 partitions.\n\nBuilt with .NET 8 and WPF.",
            "About Linux Explorer",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void PathBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.NavigateToPathCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedItem is { } item)
        {
            await ViewModel.NavigateIntoAsync(item);
        }
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection is already bound via SelectedItem
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.HandleTreeSelectionChanged(e.NewValue);
    }
}
