using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using LinuxExplorer.Helpers;
using LinuxExplorer.Models;

namespace LinuxExplorer.ViewModels;

/// <summary>
/// Wraps a <see cref="FileSystemEntry"/> for display in the file list view.
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    private readonly FileSystemEntry _entry;

    public FileItemViewModel(FileSystemEntry entry)
    {
        _entry = entry;
    }

    public string Name => _entry.Name;
    public string FullPath => _entry.FullPath;
    public bool IsDirectory => _entry.IsDirectory;
    public long Size => _entry.Size;
    public DateTime LastModified => _entry.LastModified;
    public string FileType => _entry.FileType;

    /// <summary>Human-readable file size (e.g. "4.2 MB").</summary>
    public string SizeDisplay => IsDirectory
        ? string.Empty
        : FileSystemHelper.FormatSize(_entry.Size);

    /// <summary>Icon character for the entry type.</summary>
    public string Icon => IsDirectory ? "[DIR]" : GetFileIcon(_entry.Name);

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".log" or ".md" => "[TXT]",
            ".sh" or ".py" or ".c" or ".h" or ".cpp" or ".cs" => "[SRC]",
            ".gz" or ".tgz" or ".tar" or ".zip" or ".bz2" or ".xz" => "[ZIP]",
            ".so" or ".a" => "[LIB]",
            ".conf" or ".cfg" or ".xml" or ".json" or ".ini" => "[CFG]",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "[IMG]",
            ".mp3" or ".wav" or ".ogg" or ".flac" => "[AUD]",
            ".mp4" or ".avi" or ".mkv" or ".mov" => "[VID]",
            _ => "[FILE]"
        };
    }
}
