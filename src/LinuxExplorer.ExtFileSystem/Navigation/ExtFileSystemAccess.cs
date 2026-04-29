using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.IO;
using LinuxExplorer.ExtFileSystem.Partition;
using LinuxExplorer.ExtFileSystem.RawDisk;
using LinuxExplorer.ExtFileSystem.Utils;

namespace LinuxExplorer.ExtFileSystem.Navigation;

/// <summary>
/// Main facade for accessing an ext2/3/4 filesystem on a partition.
/// Provides high-level operations for navigating, reading, and writing files.
/// </summary>
public sealed class ExtFileSystemAccess : IDisposable
{
    private readonly DiskStream _stream;
    private readonly long _partitionOffset;
    private bool _disposed;

    /// <summary>Gets the parsed superblock.</summary>
    public Superblock Superblock { get; }

    /// <summary>Gets the block group descriptors.</summary>
    public BlockGroupDescriptor[] GroupDescriptors { get; }

    /// <summary>Gets the filesystem type string (ext2/ext3/ext4).</summary>
    public string FilesystemType => Superblock.FilesystemType;

    /// <summary>Gets the volume name.</summary>
    public string VolumeName => Superblock.VolumeName;

    /// <summary>Gets the path resolver for navigating directories.</summary>
    public PathResolver PathResolver { get; }

    /// <summary>Gets the file reader.</summary>
    public ExtFileReader FileReader { get; }

    /// <summary>Gets the file writer (only available when not read-only).</summary>
    public ExtFileWriter? FileWriter { get; }

    /// <summary>Gets whether the filesystem was opened read-only.</summary>
    public bool IsReadOnly { get; }

    private ExtFileSystemAccess(DiskStream stream, long partitionOffset, bool readOnly)
    {
        _stream = stream;
        _partitionOffset = partitionOffset;
        IsReadOnly = readOnly;

        // Read superblock
        byte[] sbData = stream.ReadAt(partitionOffset + Superblock.SuperblockOffset, Superblock.SuperblockSize);
        Superblock = Superblock.Parse(sbData);

        if (!Superblock.IsValid)
            throw new InvalidDataException($"Invalid ext filesystem: bad magic number at partition offset {partitionOffset}.");

        // Read block group descriptors
        int blockSize = Superblock.BlockSize;
        long bgdTableOffset;

        // BGD table is in the block after the superblock (block 1 for 1K blocks, block 2 for >1K)
        if (blockSize == 1024)
            bgdTableOffset = partitionOffset + 2 * blockSize; // block 2
        else
            bgdTableOffset = partitionOffset + blockSize; // block 1

        int descSize = Superblock.EffectiveDescSize;
        int groupCount = (int)Superblock.BlockGroupCount;
        if (groupCount == 0) groupCount = 1;

        int tableBytes = groupCount * descSize;
        int alignedTableBytes = ((tableBytes + blockSize - 1) / blockSize) * blockSize;
        byte[] bgdData = stream.ReadAt(bgdTableOffset, alignedTableBytes);

        GroupDescriptors = new BlockGroupDescriptor[groupCount];
        for (int i = 0; i < groupCount; i++)
            GroupDescriptors[i] = BlockGroupDescriptor.Parse(bgdData, i * descSize, descSize);

        PathResolver = new PathResolver(stream, Superblock, GroupDescriptors, partitionOffset);
        FileReader = new ExtFileReader(stream, Superblock, partitionOffset);

        if (!readOnly)
            FileWriter = new ExtFileWriter(stream, Superblock, GroupDescriptors, partitionOffset);
    }

    /// <summary>
    /// Opens an ext filesystem at the given partition offset.
    /// </summary>
    /// <param name="stream">Disk stream for the containing disk.</param>
    /// <param name="partitionOffset">Byte offset of the partition within the disk.</param>
    /// <param name="readOnly">Open in read-only mode.</param>
    public static ExtFileSystemAccess Open(DiskStream stream, long partitionOffset, bool readOnly = true) =>
        new(stream, partitionOffset, readOnly);

    /// <summary>
    /// Lists the contents of a directory path.
    /// </summary>
    /// <param name="path">Absolute Unix path (e.g., <c>/home/user</c>).</param>
    public IReadOnlyList<FileSystemEntry> ListDirectory(string path = "/")
    {
        uint inodeNo = PathResolver.ResolvePath(path);
        return PathResolver.ListDirectory(inodeNo, path);
    }

    /// <summary>
    /// Reads the content of a file at the given path.
    /// </summary>
    /// <param name="path">Absolute Unix path to the file.</param>
    public byte[] ReadFile(string path)
    {
        uint inodeNo = PathResolver.ResolvePath(path);
        Inode inode = PathResolver.ReadInode(inodeNo);
        return FileReader.ReadFile(inode);
    }

    /// <summary>
    /// Writes a new file to the filesystem.
    /// </summary>
    /// <param name="parentPath">Absolute path of the parent directory.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="data">File content.</param>
    public uint WriteFile(string parentPath, string fileName, byte[] data)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");
        uint parentInodeNo = PathResolver.ResolvePath(parentPath);
        Inode parentInode = PathResolver.ReadInode(parentInodeNo);
        return FileWriter.WriteFile(parentInodeNo, parentInode, fileName, data);
    }

    /// <summary>
    /// Creates a new directory on the filesystem.
    /// </summary>
    /// <param name="parentPath">Absolute path of the parent directory.</param>
    /// <param name="dirName">Directory name.</param>
    public uint CreateDirectory(string parentPath, string dirName)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");
        uint parentInodeNo = PathResolver.ResolvePath(parentPath);
        Inode parentInode = PathResolver.ReadInode(parentInodeNo);
        return FileWriter.CreateDirectory(parentInodeNo, parentInode, dirName);
    }

    /// <summary>
    /// Deletes an entry in a directory.
    /// This basic implementation removes only the directory entry.
    /// </summary>
    public bool DeleteEntry(string parentPath, string name)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");
        uint parentInodeNo = PathResolver.ResolvePath(parentPath);
        Inode parentInode = PathResolver.ReadInode(parentInodeNo);
        return FileWriter.DeleteEntry(parentInodeNo, parentInode, name);
    }

    /// <summary>
    /// Renames or moves an entry between directories.
    /// </summary>
    public bool MoveOrRenameEntry(string sourceParentPath, string sourceName, string targetParentPath, string targetName)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");

        uint sourceParentInodeNo = PathResolver.ResolvePath(sourceParentPath);
        Inode sourceParentInode = PathResolver.ReadInode(sourceParentInodeNo);
        uint targetParentInodeNo = PathResolver.ResolvePath(targetParentPath);
        Inode targetParentInode = PathResolver.ReadInode(targetParentInodeNo);

        return FileWriter.MoveOrRenameEntry(
            sourceParentInodeNo,
            sourceParentInode,
            sourceName,
            targetParentInodeNo,
            targetParentInode,
            targetName);
    }

    /// <summary>
    /// Checks whether an entry exists in the given directory.
    /// </summary>
    public bool EntryExists(string parentPath, string name)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");
        uint parentInodeNo = PathResolver.ResolvePath(parentPath);
        Inode parentInode = PathResolver.ReadInode(parentInodeNo);
        return FileWriter.EntryExists(parentInode, name);
    }

    /// <summary>
    /// Returns entry type for a name in a directory, if present.
    /// </summary>
    public FileType? GetEntryFileType(string parentPath, string name)
    {
        if (FileWriter == null) throw new InvalidOperationException("Filesystem is read-only.");
        uint parentInodeNo = PathResolver.ResolvePath(parentPath);
        Inode parentInode = PathResolver.ReadInode(parentInodeNo);
        return FileWriter.GetEntryFileType(parentInode, name);
    }

    /// <summary>
    /// Gets formatted free space information for the filesystem.
    /// </summary>
    public long GetFreeBytes() =>
        (long)Superblock.FreeBlocksCountLo * Superblock.BlockSize;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _stream.Dispose();
            _disposed = true;
        }
    }
}
