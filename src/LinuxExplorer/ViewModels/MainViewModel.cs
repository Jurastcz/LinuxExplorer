using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxExplorer.Services;

namespace LinuxExplorer.ViewModels;

/// <summary>
/// Main ViewModel for the application window.
/// Manages disk enumeration, navigation, and file operations.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DiskDiscoveryService _diskDiscovery;
    private readonly FileOperationService _fileOps;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private PartitionViewModel? _currentPartition;

    /// <summary>Gets the list of discovered disks.</summary>
    [ObservableProperty]
    private ObservableCollection<DiskViewModel> _disks = new();

    /// <summary>Gets the currently displayed items.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(StatusItemCount))]
    private ObservableCollection<FileSystemItemViewModel> _currentItems = new();

    /// <summary>Gets or sets the selected item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    private FileSystemItemViewModel? _selectedItem;

    /// <summary>Gets or sets the current path.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusItemCount))]
    private string _currentPath = string.Empty;

    /// <summary>Gets or sets whether the application is loading.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets the status message.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Gets or sets whether large icon view is active.</summary>
    [ObservableProperty]
    private bool _isLargeIconView;

    /// <summary>Gets or sets whether details view is active.</summary>
    [ObservableProperty]
    private bool _isDetailsView = true;

    /// <summary>Gets whether any items are in the current view.</summary>
    public bool HasItems => CurrentItems.Count > 0;

    /// <summary>Gets whether an item is selected.</summary>
    public bool HasSelectedItem => SelectedItem != null;

    /// <summary>Gets whether a writable filesystem is active.</summary>
    public bool IsWritableFilesystem => _currentPartition?.Filesystem is { IsReadOnly: false };

    /// <summary>Gets whether any filesystem is currently active.</summary>
    public bool HasCurrentFilesystem => _currentPartition?.Filesystem != null;

    /// <summary>Gets the status bar item count text.</summary>
    public string StatusItemCount => CurrentItems.Count > 0
        ? $"{CurrentItems.Count} items"
        : HasCurrentFilesystem ? "0 items" : string.Empty;

    /// <summary>Gets the status bar free space text.</summary>
    public string StatusFreeSpace => _currentPartition?.FreeBytes is > 0
        ? $"Free: {FormatSize(_currentPartition.FreeBytes)}"
        : string.Empty;

    /// <summary>Gets the status bar filesystem type text.</summary>
    public string StatusFilesystemType => _currentPartition?.FilesystemType ?? string.Empty;

    /// <summary>Initializes a new <see cref="MainViewModel"/>.</summary>
    public MainViewModel()
    {
        _diskDiscovery = new DiskDiscoveryService();
        _fileOps = new FileOperationService();
    }

    /// <summary>Initializes disk discovery on startup.</summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Enumerating disks...";
        try
        {
            var disks = await Task.Run(() => _diskDiscovery.DiscoverDisks());
            Disks = new ObservableCollection<DiskViewModel>(disks);
            StatusMessage = Disks.Count == 0
                ? "No ext partitions found. Run as Administrator."
                : $"Found {Disks.Count} disk(s) with ext partitions.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Navigates into a directory or opens file properties.</summary>
    public async Task NavigateIntoAsync(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
            await LoadDirectoryAsync(item.FullPath);
        }
        else
        {
            ShowProperties(item);
        }
    }

    /// <summary>Handles selection changes in the TreeView.</summary>
    public async void HandleTreeSelectionChanged(object? item)
    {
        if (item is PartitionViewModel partition)
        {
            await OpenPartitionAsync(partition);
        }
        else if (item is FileSystemItemViewModel dirItem && dirItem.IsDirectory)
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
            await LoadDirectoryAsync(dirItem.FullPath);
        }
    }

    private async Task OpenPartitionAsync(PartitionViewModel partition)
    {
        if (!partition.IsOpened)
        {
            IsLoading = true;
            StatusMessage = "Opening partition...";
            try
            {
                await Task.Run(() => _diskDiscovery.OpenPartition(partition));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open partition: {ex.Message}";
                IsLoading = false;
                return;
            }
        }

        _currentPartition = partition;
        _backStack.Clear();
        _forwardStack.Clear();
        OnPropertyChanged(nameof(StatusFreeSpace));
        OnPropertyChanged(nameof(StatusFilesystemType));
        OnPropertyChanged(nameof(IsWritableFilesystem));
        OnPropertyChanged(nameof(HasCurrentFilesystem));
        await LoadDirectoryAsync("/");
    }

    private async Task LoadDirectoryAsync(string path)
    {
        if (_currentPartition?.Filesystem == null) return;

        IsLoading = true;
        try
        {
            var items = await Task.Run(() =>
            {
                var entries = _currentPartition.Filesystem.ListDirectory(path);
                return entries
                    .Where(e => e.Name != "." && e.Name != "..")
                    .Select(FileSystemItemViewModel.FromEntry)
                    .OrderBy(e => !e.IsDirectory)
                    .ThenBy(e => e.Name)
                    .ToList();
            });

            CurrentItems = new ObservableCollection<FileSystemItemViewModel>(items);
            CurrentPath = path;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error listing directory: {ex.Message}";
            CurrentItems = new ObservableCollection<FileSystemItemViewModel>();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(StatusItemCount));
        }
    }

    [RelayCommand]
    private async Task NavigateBack()
    {
        if (_backStack.TryPop(out var path))
        {
            _forwardStack.Push(CurrentPath);
            await LoadDirectoryAsync(path);
        }
    }

    [RelayCommand]
    private async Task NavigateForward()
    {
        if (_forwardStack.TryPop(out var path))
        {
            _backStack.Push(CurrentPath);
            await LoadDirectoryAsync(path);
        }
    }

    [RelayCommand]
    private async Task NavigateUp()
    {
        if (string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/") return;
        string parent = System.IO.Path.GetDirectoryName(CurrentPath.Replace('/', System.IO.Path.DirectorySeparatorChar))
            ?.Replace(System.IO.Path.DirectorySeparatorChar, '/') ?? "/";
        if (string.IsNullOrEmpty(parent)) parent = "/";
        _backStack.Push(CurrentPath);
        _forwardStack.Clear();
        await LoadDirectoryAsync(parent);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        // Save current state
        string savedPath = CurrentPath;
        var savedPartitionInfo = _currentPartition?.PartitionInfo;

        // Re-discover disks and partitions
        IsLoading = true;
        StatusMessage = "Refreshing disks and partitions...";
        try
        {
            var disks = await Task.Run(() => _diskDiscovery.DiscoverDisks());
            Disks = new ObservableCollection<DiskViewModel>(disks);

            // Try to find and reopen the same partition
            if (savedPartitionInfo != null)
            {
                foreach (var disk in Disks)
                {
                    var partition = disk.Partitions.FirstOrDefault(p =>
                        p.PartitionInfo.StartOffset == savedPartitionInfo.StartOffset &&
                        p.PartitionInfo.Size == savedPartitionInfo.Size);

                    if (partition != null)
                    {
                        await OpenPartitionAsync(partition);
                        if (!string.IsNullOrEmpty(savedPath))
                            await LoadDirectoryAsync(savedPath);
                        return;
                    }
                }
            }

            // If partition not found, just reload current directory
            if (!string.IsNullOrEmpty(CurrentPath) && _currentPartition != null)
            {
                await LoadDirectoryAsync(CurrentPath);
            }
            else
            {
                StatusMessage = Disks.Count == 0
                    ? "No ext partitions found. Run as Administrator."
                    : $"Found {Disks.Count} disk(s) with ext partitions.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NavigateToPath()
    {
        if (!string.IsNullOrWhiteSpace(CurrentPath))
            _ = LoadDirectoryAsync(CurrentPath);
    }

    [RelayCommand]
    private async Task CopyToWindows()
    {
        if (SelectedItem == null || _currentPartition?.Filesystem == null) return;
        if (SelectedItem.IsDirectory)
        {
            System.Windows.MessageBox.Show("Directory copying is not supported yet.", "Not Supported",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SelectedItem.Name,
            Title = "Save file to Windows"
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = $"Copying {SelectedItem.Name}...";
            try
            {
                await Task.Run(() =>
                {
                    byte[] data = _currentPartition.Filesystem.ReadFile(SelectedItem.FullPath);
                    System.IO.File.WriteAllBytes(dialog.FileName, data);
                });
                StatusMessage = $"Copied {SelectedItem.Name} successfully.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to copy file: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                StatusMessage = string.Empty;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task CopyFromWindows()
    {
        if (_currentPartition?.Filesystem == null || IsWritableFilesystem == false) return;

        var result = System.Windows.MessageBox.Show(
            "Writing to Linux partitions can cause data loss if done incorrectly.\n\nAre you sure you want to copy a file to this partition?",
            "Write Warning",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select file to copy to partition",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = $"Writing {System.IO.Path.GetFileName(dialog.FileName)}...";
            try
            {
                await Task.Run(() =>
                {
                    byte[] data = System.IO.File.ReadAllBytes(dialog.FileName);
                    string fileName = System.IO.Path.GetFileName(dialog.FileName);
                    _currentPartition.Filesystem.WriteFile(CurrentPath, fileName, data);
                });
                await Refresh();
                StatusMessage = "File written successfully.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to write file: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                StatusMessage = string.Empty;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedItem == null) return;
        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete '{SelectedItem.Name}'?",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;
        StatusMessage = "Delete operation is not yet implemented.";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NewFolder()
    {
        if (_currentPartition?.Filesystem == null || !IsWritableFilesystem) return;

        string? folderName = PromptForInput("New Folder", "Enter directory name:");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var result = System.Windows.MessageBox.Show(
            $"Create directory '{folderName}' on the ext partition?\n\nThis will modify the partition.",
            "Write Warning",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await Task.Run(() => _currentPartition.Filesystem.CreateDirectory(CurrentPath, folderName));
            await Refresh();
            StatusMessage = $"Created directory '{folderName}'.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to create directory: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Properties()
    {
        if (SelectedItem != null)
            ShowProperties(SelectedItem);
    }

    [RelayCommand]
    private void SelectAll()
    {
        // Handled by ListView
    }

    private void ShowProperties(FileSystemItemViewModel item)
    {
        var vm = PropertiesViewModel.FromItem(item);
        var dialog = new Views.PropertiesDialog { DataContext = vm };
        dialog.ShowDialog();
    }

    private static string? PromptForInput(string title, string prompt)
    {
        // Simple input dialog using an InputBox-style approach
        var window = new System.Windows.Window
        {
            Title = title,
            Width = 320,
            Height = 140,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Owner = System.Windows.Application.Current.MainWindow
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Margin = new System.Windows.Thickness(0, 0, 0, 6) });
        var textBox = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(0, 0, 0, 10) };
        panel.Children.Add(textBox);
        var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 75, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        okBtn.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        panel.Children.Add(okBtn);
        window.Content = panel;
        textBox.Focus();

        return window.ShowDialog() == true ? textBox.Text : null;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
