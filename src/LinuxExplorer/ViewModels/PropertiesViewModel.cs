using CommunityToolkit.Mvvm.ComponentModel;
using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.Utils;

namespace LinuxExplorer.ViewModels;

/// <summary>ViewModel for the file/directory properties dialog.</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    /// <summary>Gets or sets the file/directory name.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Gets or sets the full path.</summary>
    [ObservableProperty]
    private string _fullPath = string.Empty;

    /// <summary>Gets or sets the file type.</summary>
    [ObservableProperty]
    private string _fileType = string.Empty;

    /// <summary>Gets or sets the file size.</summary>
    [ObservableProperty]
    private string _fileSize = string.Empty;

    /// <summary>Gets or sets the inode number.</summary>
    [ObservableProperty]
    private string _inodeNumber = string.Empty;

    /// <summary>Gets or sets the permissions string.</summary>
    [ObservableProperty]
    private string _permissions = string.Empty;

    /// <summary>Gets or sets the octal permissions.</summary>
    [ObservableProperty]
    private string _octalPermissions = string.Empty;

    /// <summary>Gets or sets the owner UID.</summary>
    [ObservableProperty]
    private string _owner = string.Empty;

    /// <summary>Gets or sets the group GID.</summary>
    [ObservableProperty]
    private string _group = string.Empty;

    /// <summary>Gets or sets the last access time.</summary>
    [ObservableProperty]
    private string _accessTime = string.Empty;

    /// <summary>Gets or sets the change time.</summary>
    [ObservableProperty]
    private string _changeTime = string.Empty;

    /// <summary>Gets or sets the modification time.</summary>
    [ObservableProperty]
    private string _modifyTime = string.Empty;

    /// <summary>Gets or sets the hard link count.</summary>
    [ObservableProperty]
    private string _linkCount = string.Empty;

    /// <summary>Gets or sets the inode flags.</summary>
    [ObservableProperty]
    private string _inodeFlags = string.Empty;

    /// <summary>Creates a <see cref="PropertiesViewModel"/> from a <see cref="FileSystemItemViewModel"/>.</summary>
    public static PropertiesViewModel FromItem(FileSystemItemViewModel item)
    {
        var vm = new PropertiesViewModel
        {
            Name = item.Name,
            FullPath = item.FullPath,
            FileType = item.TypeName,
            InodeNumber = item.InodeNumber.ToString()
        };

        if (item.Inode is { } inode)
        {
            vm.FileSize = item.IsDirectory ? "-" : FormatFileSize(inode.Size);
            vm.Permissions = PermissionHelper.ToPermissionString(inode.Mode);
            vm.OctalPermissions = PermissionHelper.ToOctalString(inode.Mode);
            vm.Owner = inode.Uid.ToString();
            vm.Group = inode.Gid.ToString();
            vm.AccessTime = DateTimeHelper.Format(inode.ATime);
            vm.ChangeTime = DateTimeHelper.Format(inode.CTime);
            vm.ModifyTime = DateTimeHelper.Format(inode.MTime);
            vm.LinkCount = inode.LinksCount.ToString();
            vm.InodeFlags = $"0x{inode.Flags:X8}";
        }

        return vm;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes:N0} bytes ({bytes / 1_073_741_824.0:F2} GB)";
        if (bytes >= 1_048_576) return $"{bytes:N0} bytes ({bytes / 1_048_576.0:F2} MB)";
        if (bytes >= 1024) return $"{bytes:N0} bytes ({bytes / 1024.0:F2} KB)";
        return $"{bytes:N0} bytes";
    }
}
