using CommunityToolkit.Mvvm.ComponentModel;
using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.Navigation;
using LinuxExplorer.ExtFileSystem.Utils;

namespace LinuxExplorer.ViewModels;

/// <summary>
/// ViewModel representing a single file or directory entry in the file list.
/// </summary>
public sealed partial class FileSystemItemViewModel : ObservableObject
{
    private const string FolderIcon = "\uE8B7";
    private const string FileIcon = "\uE7C3";
    private const string SymlinkIcon = "\uE71B";
    private const string DeviceIcon = "\uE9A0";
    private const string UnknownIcon = "\uE9CE";

    /// <summary>Gets the file name.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Gets the inode number.</summary>
    public uint InodeNumber { get; init; }

    /// <summary>Gets the file type.</summary>
    public FileType FileType { get; init; }

    /// <summary>Gets the underlying inode data.</summary>
    public Inode? Inode { get; init; }

    /// <summary>Gets the full path of this entry.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>Gets the file size in bytes.</summary>
    public long Size => Inode?.Size ?? 0;

    /// <summary>Gets the human-readable file type name.</summary>
    public string TypeName => FileType switch
    {
        FileType.Directory => "Directory",
        FileType.RegularFile => GetFileExtensionDescription(),
        FileType.Symlink => "Symbolic Link",
        FileType.BlockDevice => "Block Device",
        FileType.CharDevice => "Character Device",
        FileType.Fifo => "Named Pipe",
        FileType.Socket => "Socket",
        _ => "Unknown"
    };

    /// <summary>Gets the last modification date as a formatted string.</summary>
    public string DateModified => Inode != null
        ? DateTimeHelper.Format(Inode.MTime)
        : string.Empty;

    /// <summary>Gets the Unix permission string (e.g., <c>-rw-r--r--</c>).</summary>
    public string Permissions => Inode != null
        ? PermissionHelper.ToPermissionString(Inode.Mode)
        : string.Empty;

    /// <summary>Gets the owner:group display string.</summary>
    public string OwnerGroup => Inode != null
        ? $"{Inode.Uid}:{Inode.Gid}"
        : string.Empty;

    /// <summary>Gets whether this item is a directory.</summary>
    public bool IsDirectory => FileType == FileType.Directory;

    /// <summary>Gets the Segoe MDL2 icon character for this entry.</summary>
    public string Icon => FileType switch
    {
        FileType.Directory => FolderIcon,
        FileType.Symlink => SymlinkIcon,
        FileType.BlockDevice or FileType.CharDevice => DeviceIcon,
        FileType.RegularFile => FileIcon,
        _ => UnknownIcon
    };

    /// <summary>Gets the icon foreground color as a string resource key.</summary>
    public System.Windows.Media.Brush IconColor => FileType == FileType.Directory
        ? System.Windows.Application.Current.TryFindResource("FolderIconBrush") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gold
        : System.Windows.Application.Current.TryFindResource("FileIconBrush") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.SteelBlue;

    /// <summary>Lazy-loaded children (directories only) for tree view.</summary>
    public List<FileSystemItemViewModel> Children { get; } = new();

    private string GetFileExtensionDescription()
    {
        int dot = Name.LastIndexOf('.');
        if (dot >= 0 && dot < Name.Length - 1)
        {
            string ext = Name[(dot + 1)..].ToUpperInvariant();
            return $"{ext} File";
        }
        return "File";
    }

    /// <summary>Creates a <see cref="FileSystemItemViewModel"/> from a <see cref="FileSystemEntry"/>.</summary>
    public static FileSystemItemViewModel FromEntry(FileSystemEntry entry) => new()
    {
        Name = entry.Name,
        InodeNumber = entry.InodeNumber,
        FileType = entry.FileType,
        Inode = entry.Inode,
        FullPath = entry.FullPath
    };
}
