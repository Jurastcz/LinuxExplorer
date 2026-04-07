using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.IO;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.Navigation;

/// <summary>
/// Represents a filesystem entry (file or directory) returned during navigation.
/// </summary>
public sealed class FileSystemEntry
{
    /// <summary>Gets the inode number.</summary>
    public uint InodeNumber { get; init; }

    /// <summary>Gets the file name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the parsed inode.</summary>
    public Inode? Inode { get; init; }

    /// <summary>Gets the file type from the directory entry.</summary>
    public FileType FileType { get; init; }

    /// <summary>Gets the full path of this entry.</summary>
    public string FullPath { get; init; } = string.Empty;
}

/// <summary>
/// Navigates ext2/3/4 directory structures and resolves paths.
/// </summary>
public sealed class PathResolver
{
    private readonly DiskStream _stream;
    private readonly Superblock _superblock;
    private readonly BlockGroupDescriptor[] _groupDescriptors;
    private readonly long _partitionOffset;
    private readonly ExtFileReader _reader;

    /// <summary>Initializes a new <see cref="PathResolver"/>.</summary>
    public PathResolver(DiskStream stream, Superblock superblock, BlockGroupDescriptor[] groupDescriptors, long partitionOffset)
    {
        _stream = stream;
        _superblock = superblock;
        _groupDescriptors = groupDescriptors;
        _partitionOffset = partitionOffset;
        _reader = new ExtFileReader(stream, superblock, partitionOffset);
    }

    /// <summary>
    /// Reads and parses the inode with the given number.
    /// </summary>
    /// <param name="inodeNo">Inode number (1-based).</param>
    /// <returns>The parsed <see cref="Inode"/>.</returns>
    public Inode ReadInode(uint inodeNo)
    {
        uint groupIdx = (inodeNo - 1) / _superblock.InodesPerGroup;
        uint localIdx = (inodeNo - 1) % _superblock.InodesPerGroup;

        if (groupIdx >= _groupDescriptors.Length)
            throw new ArgumentOutOfRangeException(nameof(inodeNo), $"Inode {inodeNo} is out of range.");

        var bgd = _groupDescriptors[groupIdx];
        long inodeOffset = _partitionOffset + (long)bgd.InodeTable * _superblock.BlockSize + localIdx * _superblock.InodeSize;
        byte[] data = _stream.ReadAt(inodeOffset, _superblock.InodeSize);
        return Inode.Parse(data, 0, _superblock.InodeSize);
    }

    /// <summary>
    /// Lists all entries in the directory with the given inode number.
    /// </summary>
    public IReadOnlyList<FileSystemEntry> ListDirectory(uint dirInodeNo, string currentPath = "/")
    {
        var inode = ReadInode(dirInodeNo);
        if (!inode.IsDirectory)
            throw new InvalidOperationException($"Inode {dirInodeNo} is not a directory.");

        byte[] dirData = _reader.ReadDirectoryBlocks(inode);
        var entries = new List<FileSystemEntry>();

        foreach (var entry in DirectoryEntry.ParseAll(dirData))
        {
            string entryPath = currentPath.TrimEnd('/') + "/" + entry.Name;
            entries.Add(new FileSystemEntry
            {
                InodeNumber = entry.InodeNumber,
                Name = entry.Name,
                Inode = TryReadInode(entry.InodeNumber),
                FileType = entry.FileType,
                FullPath = entryPath
            });
        }

        return entries;
    }

    /// <summary>
    /// Resolves an absolute path like <c>/home/user/file.txt</c> to its inode number.
    /// </summary>
    /// <param name="path">Absolute Unix path starting with <c>/</c>.</param>
    /// <returns>The inode number of the resolved entry.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the path cannot be resolved.</exception>
    public uint ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return Inode.RootInodeNumber;

        string[] parts = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        uint currentInode = Inode.RootInodeNumber;

        foreach (string part in parts)
        {
            var inode = ReadInode(currentInode);
            if (!inode.IsDirectory)
                throw new FileNotFoundException($"Path component is not a directory: {part}");

            byte[] dirData = _reader.ReadDirectoryBlocks(inode);
            bool found = false;

            foreach (var entry in DirectoryEntry.ParseAll(dirData))
            {
                if (entry.Name == part)
                {
                    currentInode = entry.InodeNumber;
                    found = true;
                    break;
                }
            }

            if (!found)
                throw new FileNotFoundException($"Path not found: {path} (missing: {part})");
        }

        return currentInode;
    }

    private Inode? TryReadInode(uint inodeNo)
    {
        try { return ReadInode(inodeNo); }
        catch { return null; }
    }
}
