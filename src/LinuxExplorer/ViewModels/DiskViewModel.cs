using CommunityToolkit.Mvvm.ComponentModel;

namespace LinuxExplorer.ViewModels;

/// <summary>ViewModel representing a physical disk.</summary>
public sealed partial class DiskViewModel : ObservableObject
{
    /// <summary>Gets the disk path (e.g., <c>\\.\PhysicalDrive0</c>).</summary>
    public string DiskPath { get; }

    /// <summary>Gets the disk model name or display label.</summary>
    public string ModelName { get; }

    /// <summary>Gets the partitions on this disk that contain ext2/3/4 filesystems.</summary>
    [ObservableProperty]
    private List<PartitionViewModel> _partitions = new();

    /// <summary>Gets a display-friendly name for the disk.</summary>
    public string DisplayName => string.IsNullOrEmpty(ModelName)
        ? DiskPath
        : $"{ModelName} ({DiskPath})";

    /// <summary>Initializes a new <see cref="DiskViewModel"/>.</summary>
    public DiskViewModel(string diskPath, string modelName = "")
    {
        DiskPath = diskPath;
        ModelName = modelName;
    }
}
