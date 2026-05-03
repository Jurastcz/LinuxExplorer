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
    private sealed class ClipboardEntry
    {
        public required string SourceDirectory { get; init; }
        public required string Name { get; init; }
        public required bool IsDirectory { get; init; }
    }

    private readonly DiskDiscoveryService _diskDiscovery;
    private readonly FileOperationService _fileOps;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private PartitionViewModel? _currentPartition;
    private List<ClipboardEntry> _clipboardEntries = [];
    private bool _isCutClipboard;

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
    [NotifyPropertyChangedFor(nameof(HasSelectedItems))]
    private FileSystemItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItems))]
    private ObservableCollection<FileSystemItemViewModel> _selectedItems = [];

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

    /// <summary>Gets whether any item is selected.</summary>
    public bool HasSelectedItems => SelectedItems.Count > 0 || SelectedItem != null;

    /// <summary>Gets whether clipboard has items to paste.</summary>
    public bool HasClipboardItems => _clipboardEntries.Count > 0;

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
        if (_currentPartition?.Filesystem == null) return;

        var targets = GetSelectedTargets();
        if (targets.Count == 0) return;

        var files = targets.Where(t => !t.IsDirectory).ToList();
        bool skippedDirectories = files.Count != targets.Count;
        if (files.Count == 0)
        {
            System.Windows.MessageBox.Show("Directory copying is not supported yet.", "Not Supported",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (files.Count == 1)
        {
            var singleDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = files[0].Name,
                Title = "Save file to Windows"
            };

            if (singleDialog.ShowDialog() != true) return;

            IsLoading = true;
            StatusMessage = $"Copying {files[0].Name}...";
            try
            {
                await Task.Run(() =>
                {
                    byte[] data = _currentPartition.Filesystem.ReadFile(files[0].FullPath);
                    System.IO.File.WriteAllBytes(singleDialog.FileName, data);
                });
                StatusMessage = $"Copied {files[0].Name} successfully.";
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

            if (skippedDirectories)
            {
                System.Windows.MessageBox.Show("Directories were skipped. Only files were copied.", "Not Supported",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }

            return;
        }

        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select destination folder"
        };

        if (folderDialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusMessage = $"Copying {files.Count} files...";
        try
        {
            await Task.Run(() =>
            {
                foreach (var item in files)
                {
                    byte[] data = _currentPartition.Filesystem.ReadFile(item.FullPath);
                    string destinationPath = EnsureUniqueWindowsFilePath(folderDialog.FolderName, item.Name);
                    System.IO.File.WriteAllBytes(destinationPath, data);
                }
            });
            StatusMessage = $"Copied {files.Count} files successfully.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to copy files: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            StatusMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }

        if (skippedDirectories)
        {
            System.Windows.MessageBox.Show("Directories were skipped. Only files were copied.", "Not Supported",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = dialog.FileNames.Length == 1
                ? $"Writing {System.IO.Path.GetFileName(dialog.FileName)}..."
                : $"Writing {dialog.FileNames.Length} files...";
            try
            {
                await Task.Run(() =>
                {
                    foreach (var sourcePath in dialog.FileNames)
                    {
                        byte[] data = System.IO.File.ReadAllBytes(sourcePath);
                        string fileName = System.IO.Path.GetFileName(sourcePath);
                        string targetName = EnsureUniqueName(_currentPartition.Filesystem, CurrentPath, fileName);
                        _currentPartition.Filesystem.WriteFile(CurrentPath, targetName, data);
                    }
                });
                await Refresh();
                StatusMessage = dialog.FileNames.Length == 1
                    ? "File written successfully."
                    : $"{dialog.FileNames.Length} files written successfully.";
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
        if (_currentPartition?.Filesystem == null || !IsWritableFilesystem) return;

        var targets = GetSelectedTargets();
        if (targets.Count == 0) return;

        var result = System.Windows.MessageBox.Show(
            targets.Count == 1
                ? $"Are you sure you want to delete '{targets[0].Name}'?"
                : $"Are you sure you want to delete {targets.Count} selected items?",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var item in targets)
                {
                    _currentPartition.Filesystem.DeleteEntry(CurrentPath, item.Name);
                }
            });
            await Refresh();
            StatusMessage = "Delete completed.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to delete item(s): {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        var targets = GetSelectedTargets();
        if (targets.Count == 0) return;

        _clipboardEntries = targets
            .Select(i => new ClipboardEntry
            {
                SourceDirectory = CurrentPath,
                Name = i.Name,
                IsDirectory = i.IsDirectory
            })
            .ToList();
        _isCutClipboard = false;
        OnPropertyChanged(nameof(HasClipboardItems));
        StatusMessage = targets.Count == 1 ? $"Copied '{targets[0].Name}'." : $"Copied {targets.Count} items.";
    }

    [RelayCommand]
    private void Cut()
    {
        if (_currentPartition?.Filesystem == null || !IsWritableFilesystem) return;

        var targets = GetSelectedTargets();
        if (targets.Count == 0) return;

        _clipboardEntries = targets
            .Select(i => new ClipboardEntry
            {
                SourceDirectory = CurrentPath,
                Name = i.Name,
                IsDirectory = i.IsDirectory
            })
            .ToList();
        _isCutClipboard = true;
        OnPropertyChanged(nameof(HasClipboardItems));
        StatusMessage = targets.Count == 1 ? $"Cut '{targets[0].Name}'." : $"Cut {targets.Count} items.";
    }

    [RelayCommand]
    private async Task Paste()
    {
        if (_currentPartition?.Filesystem == null || !IsWritableFilesystem || _clipboardEntries.Count == 0) return;

        bool skippedDirectories = false;
        IsLoading = true;

        try
        {
            await Task.Run(() =>
            {
                foreach (var entry in _clipboardEntries)
                {
                    if (entry.IsDirectory)
                    {
                        skippedDirectories = true;
                        continue;
                    }

                    if (_isCutClipboard)
                    {
                        if (entry.SourceDirectory == CurrentPath)
                            continue;

                        string moveTargetName = EnsureUniqueName(_currentPartition.Filesystem, CurrentPath, entry.Name);
                        _currentPartition.Filesystem.MoveOrRenameEntry(entry.SourceDirectory, entry.Name, CurrentPath, moveTargetName);
                    }
                    else
                    {
                        string sourcePath = CombinePath(entry.SourceDirectory, entry.Name);
                        byte[] data = _currentPartition.Filesystem.ReadFile(sourcePath);

                        string copyTargetName = entry.SourceDirectory == CurrentPath
                            ? GenerateWindowsCopyName(_currentPartition.Filesystem, CurrentPath, entry.Name)
                            : EnsureUniqueName(_currentPartition.Filesystem, CurrentPath, entry.Name);

                        _currentPartition.Filesystem.WriteFile(CurrentPath, copyTargetName, data);
                    }
                }
            });

            if (_isCutClipboard)
            {
                _clipboardEntries = [];
                _isCutClipboard = false;
                OnPropertyChanged(nameof(HasClipboardItems));
            }

            await Refresh();
            StatusMessage = "Paste completed.";

            if (skippedDirectories)
            {
                System.Windows.MessageBox.Show("Copy/Cut for directories is not supported yet.", "Not Supported",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Paste failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (_currentPartition?.Filesystem == null || !IsWritableFilesystem) return;

        var targets = GetSelectedTargets();
        if (targets.Count != 1)
        {
            System.Windows.MessageBox.Show("Rename requires a single selected item.", "Rename",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var item = targets[0];
        string? newName = PromptForInput("Rename", "Enter new name:", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        try
        {
            await Task.Run(() =>
            {
                if (_currentPartition.Filesystem.EntryExists(CurrentPath, newName))
                    throw new IOException($"An entry named '{newName}' already exists.");

                _currentPartition.Filesystem.MoveOrRenameEntry(CurrentPath, item.Name, CurrentPath, newName);
            });

            await Refresh();
            StatusMessage = "Rename completed.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Rename failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
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

    public void UpdateSelectedItems(IEnumerable<FileSystemItemViewModel> items)
    {
        SelectedItems = new ObservableCollection<FileSystemItemViewModel>(items);
        if (SelectedItems.Count == 1)
            SelectedItem = SelectedItems[0];
        else if (SelectedItems.Count == 0)
            SelectedItem = null;

        OnPropertyChanged(nameof(HasSelectedItems));
    }

    private List<FileSystemItemViewModel> GetSelectedTargets()
    {
        if (SelectedItems.Count > 0)
            return SelectedItems.ToList();
        return SelectedItem != null ? [SelectedItem] : [];
    }

    private static string CombinePath(string parent, string name) =>
        parent == "/" ? $"/{name}" : $"{parent.TrimEnd('/')}/{name}";

    private static string EnsureUniqueName(LinuxExplorer.ExtFileSystem.Navigation.ExtFileSystemAccess fs, string parentPath, string baseName)
    {
        if (!fs.EntryExists(parentPath, baseName)) return baseName;

        string stem = System.IO.Path.GetFileNameWithoutExtension(baseName);
        string ext = System.IO.Path.GetExtension(baseName);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = string.IsNullOrEmpty(ext)
                ? $"{stem} ({i})"
                : $"{stem} ({i}){ext}";
            if (!fs.EntryExists(parentPath, candidate))
                return candidate;
        }

        throw new IOException("Cannot generate unique file name.");
    }

    private static string GenerateWindowsCopyName(LinuxExplorer.ExtFileSystem.Navigation.ExtFileSystemAccess fs, string parentPath, string originalName)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(originalName);
        string ext = System.IO.Path.GetExtension(originalName);

        string candidate = string.IsNullOrEmpty(ext)
            ? $"{stem} - Copy"
            : $"{stem} - Copy{ext}";
        if (!fs.EntryExists(parentPath, candidate))
            return candidate;

        for (int i = 2; i < 10_000; i++)
        {
            candidate = string.IsNullOrEmpty(ext)
                ? $"{stem} - Copy ({i})"
                : $"{stem} - Copy ({i}){ext}";
            if (!fs.EntryExists(parentPath, candidate))
                return candidate;
        }

        throw new IOException("Cannot generate copy name.");
    }

    private static string EnsureUniqueWindowsFilePath(string folderPath, string fileName)
    {
        string destinationPath = System.IO.Path.Combine(folderPath, fileName);
        if (!System.IO.File.Exists(destinationPath))
            return destinationPath;

        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string ext = System.IO.Path.GetExtension(fileName);

        for (int i = 2; i < 10_000; i++)
        {
            string candidateName = string.IsNullOrEmpty(ext)
                ? $"{stem} ({i})"
                : $"{stem} ({i}){ext}";
            destinationPath = System.IO.Path.Combine(folderPath, candidateName);
            if (!System.IO.File.Exists(destinationPath))
                return destinationPath;
        }

        throw new IOException("Cannot generate unique destination path on Windows.");
    }

    private void ShowProperties(FileSystemItemViewModel item)
    {
        var vm = PropertiesViewModel.FromItem(item);
        var dialog = new Views.PropertiesDialog { DataContext = vm };
        dialog.ShowDialog();
    }

    private static string? PromptForInput(string title, string prompt, string initialValue = "")
    {
        // Simple input dialog using app theme resources
        var window = new System.Windows.Window
        {
            Title = title,
            Width = 380,
            Height = 170,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Owner = System.Windows.Application.Current.MainWindow,
            Background = System.Windows.Application.Current.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.DimGray,
            Foreground = System.Windows.Application.Current.TryFindResource("PrimaryTextBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.White
        };

        var border = new System.Windows.Controls.Border
        {
            Margin = new System.Windows.Thickness(6),
            Padding = new System.Windows.Thickness(6),
            CornerRadius = new System.Windows.CornerRadius(6),
            Background = System.Windows.Application.Current.TryFindResource("SurfaceBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray,
            BorderBrush = System.Windows.Application.Current.TryFindResource("BorderBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.DarkGray,
            BorderThickness = new System.Windows.Thickness(1)
        };

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Margin = new System.Windows.Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Application.Current.TryFindResource("SecondaryTextBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gainsboro
        });

        var textBox = new System.Windows.Controls.TextBox
        {
            Margin = new System.Windows.Thickness(0, 0, 0, 10),
            Text = initialValue
        };
        if (System.Windows.Application.Current.TryFindResource("PathBarStyle") is System.Windows.Style pathBarStyle)
            textBox.Style = pathBarStyle;

        panel.Children.Add(textBox);

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 90,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        if (System.Windows.Application.Current.TryFindResource("PrimaryButtonStyle") is System.Windows.Style primaryButtonStyle)
            okBtn.Style = primaryButtonStyle;

        okBtn.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        panel.Children.Add(okBtn);

        border.Child = panel;
        window.Content = border;
        window.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

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
