using CommunityToolkit.Mvvm.ComponentModel;
using LinuxExplorer.ExtFileSystem.Navigation;
using LinuxExplorer.ExtFileSystem.Partition;

namespace LinuxExplorer.ViewModels;

/// <summary>ViewModel for a partition containing an ext2/3/4 filesystem.</summary>
public sealed partial class PartitionViewModel : ObservableObject
{
    private ExtFileSystemAccess? _filesystem;
    private readonly PartitionInfo _partitionInfo;

    /// <summary>Gets the partition info.</summary>
    public PartitionInfo PartitionInfo => _partitionInfo;

    /// <summary>Gets the display name for the partition.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the opened filesystem access (null if not yet opened).</summary>
    public ExtFileSystemAccess? Filesystem => _filesystem;

    /// <summary>Gets the root-level directories for the tree view.</summary>
    [ObservableProperty]
    private List<FileSystemItemViewModel> _directories = new();

    /// <summary>Gets the filesystem type string.</summary>
    [ObservableProperty]
    private string _filesystemType = "ext?";

    /// <summary>Gets the volume name.</summary>
    [ObservableProperty]
    private string _volumeName = string.Empty;

    /// <summary>Gets whether the partition has been successfully opened.</summary>
    [ObservableProperty]
    private bool _isOpened;

    /// <summary>Gets the free space in bytes.</summary>
    [ObservableProperty]
    private long _freeBytes;

    /// <summary>Initializes a new <see cref="PartitionViewModel"/>.</summary>
    public PartitionViewModel(PartitionInfo info)
    {
        _partitionInfo = info;
        DisplayName = $"Part {info.Index + 1} ({FormatSize(info.Size)})";
    }

    /// <summary>Opens the filesystem for access.</summary>
    public void Open(ExtFileSystemAccess filesystem)
    {
        _filesystem = filesystem;
        FilesystemType = filesystem.FilesystemType;
        VolumeName = filesystem.VolumeName;
        FreeBytes = filesystem.GetFreeBytes();
        IsOpened = true;

        DisplayName_Updated();
        LoadRootDirectories();
    }

    private void DisplayName_Updated() { /* triggers UI update via OnPropertyChanged */ }

    private void LoadRootDirectories()
    {
        if (_filesystem == null) return;
        try
        {
            var entries = _filesystem.ListDirectory("/");
            var dirs = entries
                .Where(e => e.FileType == ExtFileSystem.Ext.FileType.Directory && e.Name != "." && e.Name != "..")
                .Select(FileSystemItemViewModel.FromEntry)
                .OrderBy(e => e.Name)
                .ToList();
            Directories = dirs;
        }
        catch { /* ignore */ }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
