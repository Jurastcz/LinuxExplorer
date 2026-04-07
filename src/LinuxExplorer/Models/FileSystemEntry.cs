using DiscUtils;
using DiscUtils.Ext;

namespace LinuxExplorer.Models;

/// <summary>
/// Represents a file or directory entry in the ext filesystem.
/// </summary>
public class FileSystemEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string FileType { get; set; } = string.Empty;
    public UnixFileSystemInfo? UnixInfo { get; set; }
}
