using System.IO;
using DiscUtils;
using DiscUtils.Ext;
using LinuxExplorer.Models;

namespace LinuxExplorer.Services;

/// <summary>
/// Wraps DiscUtils.Ext to provide read access (and best-effort write access) to ext2/3/4 filesystems.
/// Note: DiscUtils.Ext 0.16.13 implements a read-only driver. Write operations will throw
/// <see cref="NotSupportedException"/>. The API surface is defined for future write-capable drivers.
/// </summary>
public class ExtFileSystemService : IDisposable
{
    private ExtFileSystem? _fileSystem;
    private Stream? _diskStream;
    private bool _isReadOnly;
    private bool _disposed;

    private const int CopyBufferSize = 4 * 1024 * 1024; // 4 MB

    static ExtFileSystemService()
    {
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
    }

    public bool IsOpen => _fileSystem != null;
    public bool IsReadOnly => _isReadOnly;

    /// <summary>
    /// Opens a disk image file (.img, .raw, etc.) containing an ext2/3/4 filesystem.
    /// </summary>
    public void OpenDiskImage(string imagePath, bool readOnly = false)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Disk image not found: {imagePath}", imagePath);

        Close();

        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var share = readOnly ? FileShare.Read : FileShare.None;
        _diskStream = new FileStream(imagePath, FileMode.Open, access, share);
        _isReadOnly = readOnly;
        _fileSystem = new ExtFileSystem(_diskStream);
    }

    /// <summary>
    /// Opens a raw partition device or block device path.
    /// </summary>
    public void OpenPartition(string partitionPath, bool readOnly = false)
    {
        Close();

        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var share = readOnly ? FileShare.Read : FileShare.None;
        _diskStream = new FileStream(partitionPath, FileMode.Open, access, share);
        _isReadOnly = readOnly;
        _fileSystem = new ExtFileSystem(_diskStream);
    }

    /// <summary>
    /// Lists files and directories in the given path. Pass "/" or "" for the root directory.
    /// </summary>
    public IEnumerable<FileSystemEntry> GetEntries(string path)
    {
        EnsureOpen();

        var discPath = ToDiscPath(path);
        var entries = new List<FileSystemEntry>();

        try
        {
            foreach (var dir in _fileSystem!.GetDirectories(discPath))
            {
                var info = _fileSystem.GetDirectoryInfo(dir);
                entries.Add(new FileSystemEntry
                {
                    Name = GetEntryName(dir),
                    FullPath = ToUserPath(dir),
                    IsDirectory = true,
                    Size = 0,
                    LastModified = info.LastWriteTimeUtc,
                    FileType = "Directory",
                    UnixInfo = TryGetUnixInfo(dir)
                });
            }

            foreach (var file in _fileSystem!.GetFiles(discPath))
            {
                var info = _fileSystem.GetFileInfo(file);
                entries.Add(new FileSystemEntry
                {
                    Name = GetEntryName(file),
                    FullPath = ToUserPath(file),
                    IsDirectory = false,
                    Size = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    FileType = GetFileType(file),
                    UnixInfo = TryGetUnixInfo(file)
                });
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Return empty if path doesn't exist
        }

        return entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name);
    }

    /// <summary>
    /// Reads a file from the ext filesystem into the provided destination stream.
    /// </summary>
    public void ReadFile(string extPath, Stream destination)
    {
        EnsureOpen();

        using var source = _fileSystem!.OpenFile(ToDiscPath(extPath), FileMode.Open, FileAccess.Read);
        source.CopyTo(destination, CopyBufferSize);
    }

    /// <summary>
    /// Writes a file from the provided source stream into the ext filesystem.
    /// Note: DiscUtils.Ext 0.16.13 is read-only; this will throw <see cref="NotSupportedException"/>.
    /// </summary>
    public void WriteFile(string extPath, Stream source)
    {
        EnsureOpen();
        EnsureWritable();

        using var destination = _fileSystem!.OpenFile(ToDiscPath(extPath), FileMode.Create, FileAccess.Write);
        source.CopyTo(destination, CopyBufferSize);
    }

    /// <summary>
    /// Deletes a file or directory (optionally recursive).
    /// Note: DiscUtils.Ext 0.16.13 is read-only; this will throw <see cref="NotSupportedException"/>.
    /// </summary>
    public void DeleteEntry(string path, bool recursive = false)
    {
        EnsureOpen();
        EnsureWritable();

        var discPath = ToDiscPath(path);

        if (_fileSystem!.DirectoryExists(discPath))
        {
            if (recursive)
                _fileSystem.DeleteDirectory(discPath, true);
            else
                _fileSystem.DeleteDirectory(discPath);
        }
        else if (_fileSystem.FileExists(discPath))
        {
            _fileSystem.DeleteFile(discPath);
        }
        else
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }
    }

    /// <summary>
    /// Creates a directory (and any required parent directories).
    /// Note: DiscUtils.Ext 0.16.13 is read-only; this will throw <see cref="NotSupportedException"/>.
    /// </summary>
    public void CreateDirectory(string path)
    {
        EnsureOpen();
        EnsureWritable();
        _fileSystem!.CreateDirectory(ToDiscPath(path));
    }

    /// <summary>
    /// Moves or renames an entry within the ext filesystem.
    /// Note: DiscUtils.Ext 0.16.13 is read-only; this will throw <see cref="NotSupportedException"/>.
    /// </summary>
    public void MoveEntry(string sourcePath, string destinationPath)
    {
        EnsureOpen();
        EnsureWritable();

        var discSrc = ToDiscPath(sourcePath);
        var discDst = ToDiscPath(destinationPath);

        if (_fileSystem!.DirectoryExists(discSrc))
            _fileSystem.MoveDirectory(discSrc, discDst);
        else if (_fileSystem.FileExists(discSrc))
            _fileSystem.MoveFile(discSrc, discDst);
        else
            throw new FileNotFoundException($"Source path not found: {sourcePath}");
    }

    /// <summary>
    /// Returns total, free, and used space on the filesystem.
    /// </summary>
    public (long Total, long Free, long Used) GetDiskInfo()
    {
        EnsureOpen();
        long total = _fileSystem!.Size;
        long free = _fileSystem.AvailableSpace;
        long used = Math.Max(0, total - free);
        return (total, free, used);
    }

    /// <summary>
    /// Closes the filesystem and releases the underlying stream.
    /// </summary>
    public void Close()
    {
        _fileSystem?.Dispose();
        _fileSystem = null;
        _diskStream?.Dispose();
        _diskStream = null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
                Close();
            _disposed = true;
        }
    }

    // -------------------------------------------------------------------------
    // Path helpers
    // DiscUtils.Ext uses "" for root and backslash separators internally.
    // The public API uses "/" (unix-style) for user-facing paths.
    // -------------------------------------------------------------------------

    /// <summary>Converts a user-facing "/" path to the DiscUtils internal backslash path.</summary>
    internal static string ToDiscPath(string userPath)
    {
        if (string.IsNullOrEmpty(userPath) || userPath == "/")
            return string.Empty;

        return userPath.Replace('/', '\\').TrimEnd('\\');
    }

    /// <summary>Converts a DiscUtils backslash path to a user-facing "/" path.</summary>
    internal static string ToUserPath(string discPath)
    {
        if (string.IsNullOrEmpty(discPath))
            return "/";
        return "/" + discPath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>Extracts the final name component from a DiscUtils backslash path.</summary>
    private static string GetEntryName(string discPath)
    {
        var trimmed = discPath.TrimEnd('\\');
        var lastSep = trimmed.LastIndexOf('\\');
        return lastSep < 0 ? trimmed : trimmed[(lastSep + 1)..];
    }

    private void EnsureOpen()
    {
        if (_fileSystem == null)
            throw new InvalidOperationException(
                "No filesystem is currently open. Use OpenDiskImage or OpenPartition first.");
    }

    private void EnsureWritable()
    {
        if (_isReadOnly)
            throw new InvalidOperationException("The filesystem was opened in read-only mode.");
    }

    private UnixFileSystemInfo? TryGetUnixInfo(string discPath)
    {
        try { return _fileSystem!.GetUnixFileInfo(discPath); }
        catch { return null; }
    }

    private static string GetFileType(string discPath)
    {
        var name = GetEntryName(discPath);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "Text Document",
            ".sh" => "Shell Script",
            ".py" => "Python Script",
            ".c" => "C Source File",
            ".h" => "C Header File",
            ".cpp" or ".cxx" => "C++ Source File",
            ".so" => "Shared Library",
            ".a" => "Static Library",
            ".gz" or ".tgz" => "GZip Archive",
            ".tar" => "Tar Archive",
            ".zip" => "ZIP Archive",
            ".conf" or ".cfg" => "Configuration File",
            ".log" => "Log File",
            ".xml" => "XML File",
            ".json" => "JSON File",
            ".md" => "Markdown File",
            "" => "File",
            _ => $"{ext.TrimStart('.').ToUpper()} File"
        };
    }
}
