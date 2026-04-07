using System.Windows;

namespace LinuxExplorer.Views;

/// <summary>Interaction logic for PropertiesDialog.xaml</summary>
public partial class PropertiesDialog : Window
{
    /// <summary>Initializes the properties dialog.</summary>
    public PropertiesDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
