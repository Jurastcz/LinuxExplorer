using System.Windows;
using LinuxExplorer.ViewModels;

namespace LinuxExplorer.Views;

/// <summary>Interaction logic for FormatDialog.xaml</summary>
public partial class FormatDialog : Window
{
    public FormatDialog()
    {
        InitializeComponent();
        Loaded += FormatDialog_Loaded;
    }

    private async void FormatDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FormatViewModel vm)
        {
            await Task.Run(() => vm.DiscoverDisks());
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
