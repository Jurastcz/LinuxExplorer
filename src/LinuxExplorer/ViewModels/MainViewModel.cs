using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxExplorer.Models;
using LinuxExplorer.Services;
using Microsoft.Win32;

namespace LinuxExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ExtFileSystemService _service = new();
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "No filesystem open.";

    [ObservableProperty]
    private string _diskInfo = string.Empty;

    public ObservableCollection<DirectoryItemViewModel> DirectoryTree { get; } = new();
    public ObservableCollection<FileItemViewModel> CurrentFiles { get; } = new();
    public ObservableCollection<FileItemViewModel> SelectedFiles { get; } = new();

    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    [RelayCommand]
    private async Task OpenDiskImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Disk Image",
            Filter = "Disk Images (*.img;*.raw;*.bin)|*.img;*.raw;*.bin|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        await LoadWithOverlayAsync(async () =>
        {
            await Task.Run(() => _service.OpenDiskImage(dlg.FileName, readOnly: false));
            await InitializeTreeAsync();
        });
    }

    [RelayCommand]
    private async Task OpenPartitionAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Raw Partition",
            Filter = "All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        await LoadWithOverlayAsync(async () =>
        {
            await Task.Run(() => _service.OpenPartition(dlg.FileName, readOnly: false));
            await InitializeTreeAsync();
        });
    }

    [RelayCommand]
    private async Task NavigateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_service.IsOpen) return;

        var previous = CurrentPath;
        if (!string.IsNullOrEmpty(previous) && previous != path)
        {
            _backHistory.Push(previous);
            _forwardHistory.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        await LoadDirectoryAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task NavigateBackAsync()
    {
        if (_backHistory.Count == 0) return;
        var target = _backHistory.Pop();
        _forwardHistory.Push(CurrentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await LoadDirectoryAsync(target);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task NavigateForwardAsync()
    {
        if (_forwardHistory.Count == 0) return;
        var target = _forwardHistory.Pop();
        _backHistory.Push(CurrentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await LoadDirectoryAsync(target);
    }

    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        if (!_service.IsOpen || string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/") return;

        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd('/'))?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) parent = "/";
        await NavigateAsync(parent);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_service.IsOpen) return;
        await LoadWithOverlayAsync(async () => await LoadDirectoryAsync(CurrentPath, pushHistory: false));
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (!_service.IsOpen)
        {
            ShowError("No filesystem is open.");
            return;
        }

        var name = PromptInput("New Folder", "Enter folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(name)) return;

        var newPath = CurrentPath.TrimEnd('/') + "/" + name;

        await LoadWithOverlayAsync(async () =>
        {
            await Task.Run(() => _service.CreateDirectory(newPath));
            await LoadDirectoryAsync(CurrentPath, pushHistory: false);
        });
    }

    [RelayCommand]
    private async Task CopyToWindowsAsync()
    {
        if (SelectedFiles.Count == 0)
        {
            ShowError("No files selected.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Save to Windows",
            FileName = SelectedFiles[0].Name
        };

        if (SelectedFiles.Count == 1 && !SelectedFiles[0].IsDirectory)
        {
            if (dlg.ShowDialog() != true) return;

            await LoadWithOverlayAsync(async () =>
            {
                await Task.Run(() =>
                {
                    using var dest = File.Create(dlg.FileName);
                    _service.ReadFile(SelectedFiles[0].FullPath, dest);
                });
            });

            StatusText = $"Copied '{SelectedFiles[0].Name}' to '{dlg.FileName}'.";
        }
        else
        {
            // Multiple files: ask for a destination directory
            var folderDlg = new OpenFileDialog
            {
                Title = "Select destination folder (pick any file in the folder)",
                CheckFileExists = false,
                FileName = "Select folder"
            };

            if (folderDlg.ShowDialog() != true) return;

            var destDir = Path.GetDirectoryName(folderDlg.FileName) ?? string.Empty;

            await LoadWithOverlayAsync(async () =>
            {
                foreach (var file in SelectedFiles.Where(f => !f.IsDirectory))
                {
                    var destPath = Path.Combine(destDir, file.Name);
                    await Task.Run(() =>
                    {
                        using var dest = File.Create(destPath);
                        _service.ReadFile(file.FullPath, dest);
                    });
                }
            });

            StatusText = $"Copied {SelectedFiles.Count} file(s) to '{destDir}'.";
        }
    }

    [RelayCommand]
    private async Task CopyFromWindowsAsync()
    {
        if (!_service.IsOpen)
        {
            ShowError("No filesystem is open.");
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Copy file(s) from Windows",
            Multiselect = true,
            Filter = "All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        await LoadWithOverlayAsync(async () =>
        {
            foreach (var filePath in dlg.FileNames)
            {
                var destPath = CurrentPath.TrimEnd('/') + "/" + Path.GetFileName(filePath);
                await Task.Run(() =>
                {
                    using var src = File.OpenRead(filePath);
                    _service.WriteFile(destPath, src);
                });
            }

            await LoadDirectoryAsync(CurrentPath, pushHistory: false);
        });

        StatusText = $"Copied {dlg.FileNames.Length} file(s) into '{CurrentPath}'.";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedFiles.Count == 0)
        {
            ShowError("No items selected.");
            return;
        }

        var names = string.Join(", ", SelectedFiles.Select(f => f.Name));
        var result = MessageBox.Show(
            $"Are you sure you want to delete:\n{names}",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await LoadWithOverlayAsync(async () =>
        {
            foreach (var item in SelectedFiles.ToList())
            {
                await Task.Run(() => _service.DeleteEntry(item.FullPath, recursive: true));
            }

            await LoadDirectoryAsync(CurrentPath, pushHistory: false);
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task InitializeTreeAsync()
    {
        DirectoryTree.Clear();
        var root = new DirectoryItemViewModel("/", "/", _service, hasChildren: true);
        DirectoryTree.Add(root);

        UpdateDiskInfo();
        await LoadDirectoryAsync("/", pushHistory: false);
    }

    private async Task LoadDirectoryAsync(string path, bool pushHistory = true)
    {
        if (!_service.IsOpen) return;

        IsLoading = true;
        try
        {
            IEnumerable<FileSystemEntry> entries = Enumerable.Empty<FileSystemEntry>();
            await Task.Run(() => entries = _service.GetEntries(path).ToList());

            CurrentFiles.Clear();
            foreach (var entry in entries)
                CurrentFiles.Add(new FileItemViewModel(entry));

            CurrentPath = path;
            StatusText = $"{CurrentFiles.Count} item(s) in '{path}'.";
            UpdateDiskInfo();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to list directory '{path}':\n{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateDiskInfo()
    {
        if (!_service.IsOpen) { DiskInfo = string.Empty; return; }

        try
        {
            var (total, free, used) = _service.GetDiskInfo();
            DiskInfo = $"Total: {FormatSize(total)}  Free: {FormatSize(free)}  Used: {FormatSize(used)}";
        }
        catch
        {
            DiskInfo = string.Empty;
        }
    }

    private async Task LoadWithOverlayAsync(Func<Task> action)
    {
        IsLoading = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Linux Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string? PromptInput(string title, string prompt, string defaultValue = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });

        var tb = new System.Windows.Controls.TextBox { Text = defaultValue };
        tb.SelectAll();
        stack.Children.Add(tb);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string? result = null;
        var ok = new System.Windows.Controls.Button { Content = "OK", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => { result = tb.Text; win.DialogResult = true; };

        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 72, IsCancel = true };
        cancel.Click += (_, _) => { win.DialogResult = false; };

        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        stack.Children.Add(btnPanel);

        win.Content = stack;
        win.ShowDialog();
        return result;
    }

    private static string FormatSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        return bytes switch
        {
            < KB => $"{bytes} B",
            < MB => $"{bytes / (double)KB:F1} KB",
            < GB => $"{bytes / (double)MB:F1} MB",
            < TB => $"{bytes / (double)GB:F1} GB",
            _ => $"{bytes / (double)TB:F1} TB"
        };
    }
}
