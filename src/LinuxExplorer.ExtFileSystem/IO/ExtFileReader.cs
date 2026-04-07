using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.IO;

/// <summary>
/// Reads file content from an ext2/3/4 filesystem by resolving inode block mappings.
/// Supports direct blocks, single/double/triple indirect blocks, and ext4 extent trees.
/// </summary>
public sealed class ExtFileReader
{
    private readonly DiskStream _stream;
    private readonly Superblock _superblock;
    private readonly long _partitionOffset;

    /// <summary>
    /// Initializes a new <see cref="ExtFileReader"/>.
    /// </summary>
    /// <param name="stream">Disk stream for the containing disk.</param>
    /// <param name="superblock">Parsed superblock for this filesystem.</param>
    /// <param name="partitionOffset">Byte offset of the partition from disk start.</param>
    public ExtFileReader(DiskStream stream, Superblock superblock, long partitionOffset)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _superblock = superblock ?? throw new ArgumentNullException(nameof(superblock));
        _partitionOffset = partitionOffset;
    }

    /// <summary>
    /// Reads all data for the given inode and returns it as a byte array.
    /// </summary>
    public byte[] ReadFile(Inode inode)
    {
        ArgumentNullException.ThrowIfNull(inode);
        if (!inode.IsRegularFile && !inode.IsSymlink)
            throw new InvalidOperationException("Inode is not a regular file or symlink.");

        long size = inode.Size;
        if (size == 0) return [];

        var result = new byte[size];
        long written = 0;

        if (inode.UsesExtents)
        {
            ReadExtents(inode, result, ref written);
        }
        else
        {
            ReadIndirect(inode, result, ref written);
        }

        return result;
    }

    /// <summary>
    /// Reads the block data for a directory inode into a stream for directory entry parsing.
    /// </summary>
    public byte[] ReadDirectoryBlocks(Inode inode)
    {
        ArgumentNullException.ThrowIfNull(inode);
        if (!inode.IsDirectory)
            throw new InvalidOperationException("Inode is not a directory.");

        // Use file size for directories
        long size = inode.Size;
        if (size == 0) return [];

        var result = new byte[size];
        long written = 0;

        if (inode.UsesExtents)
            ReadExtents(inode, result, ref written);
        else
            ReadIndirect(inode, result, ref written);

        return result;
    }

    private void ReadExtents(Inode inode, byte[] dest, ref long written)
    {
        // Extent tree root is in inode.BlockRaw (60 bytes)
        TraverseExtentTree(inode.BlockRaw, 0, dest, ref written);
    }

    private void TraverseExtentTree(byte[] nodeData, int offset, byte[] dest, ref long written)
    {
        var header = ExtentHeader.Parse(nodeData, offset);
        if (!header.IsValid) return;

        int entriesOffset = offset + ExtentHeader.Size;

        if (header.Depth == 0)
        {
            // Leaf node – read extents
            for (int i = 0; i < header.Entries && written < dest.Length; i++)
            {
                var extent = Extent.Parse(nodeData, entriesOffset + i * Extent.Size);
                if (extent.IsUninitialized) continue;

                for (int b = 0; b < extent.Length && written < dest.Length; b++)
                {
                    long physBlock = (long)(extent.Start + (ulong)b);
                    int toCopy = (int)Math.Min(_superblock.BlockSize, dest.Length - written);
                    byte[] blockData = ReadBlock(physBlock);
                    int actual = Math.Min(toCopy, blockData.Length);
                    Buffer.BlockCopy(blockData, 0, dest, (int)written, actual);
                    written += actual;
                }
            }
        }
        else
        {
            // Internal node – follow index entries
            for (int i = 0; i < header.Entries; i++)
            {
                var idx = ExtentIndex.Parse(nodeData, entriesOffset + i * ExtentIndex.Size);
                byte[] childData = ReadBlock((long)idx.Leaf);
                TraverseExtentTree(childData, 0, dest, ref written);
                if (written >= dest.Length) break;
            }
        }
    }

    private void ReadIndirect(Inode inode, byte[] dest, ref long written)
    {
        int blockSize = _superblock.BlockSize;
        int ptrsPerBlock = blockSize / 4;

        // 12 direct blocks
        for (int i = 0; i < 12 && written < dest.Length; i++)
        {
            if (inode.Block[i] == 0) continue;
            CopyBlock(inode.Block[i], dest, ref written);
        }

        // Single indirect
        if (inode.Block[12] != 0 && written < dest.Length)
        {
            byte[] indBlock = ReadBlock(inode.Block[12]);
            for (int i = 0; i < ptrsPerBlock && written < dest.Length; i++)
            {
                uint blockNo = BitConverter.ToUInt32(indBlock, i * 4);
                if (blockNo == 0) continue;
                CopyBlock(blockNo, dest, ref written);
            }
        }

        // Double indirect
        if (inode.Block[13] != 0 && written < dest.Length)
        {
            byte[] dblBlock = ReadBlock(inode.Block[13]);
            for (int i = 0; i < ptrsPerBlock && written < dest.Length; i++)
            {
                uint indPtr = BitConverter.ToUInt32(dblBlock, i * 4);
                if (indPtr == 0) continue;
                byte[] indBlock = ReadBlock(indPtr);
                for (int j = 0; j < ptrsPerBlock && written < dest.Length; j++)
                {
                    uint blockNo = BitConverter.ToUInt32(indBlock, j * 4);
                    if (blockNo == 0) continue;
                    CopyBlock(blockNo, dest, ref written);
                }
            }
        }

        // Triple indirect
        if (inode.Block[14] != 0 && written < dest.Length)
        {
            byte[] triBlock = ReadBlock(inode.Block[14]);
            for (int i = 0; i < ptrsPerBlock && written < dest.Length; i++)
            {
                uint dblPtr = BitConverter.ToUInt32(triBlock, i * 4);
                if (dblPtr == 0) continue;
                byte[] dblBlock = ReadBlock(dblPtr);
                for (int j = 0; j < ptrsPerBlock && written < dest.Length; j++)
                {
                    uint indPtr = BitConverter.ToUInt32(dblBlock, j * 4);
                    if (indPtr == 0) continue;
                    byte[] indBlock = ReadBlock(indPtr);
                    for (int k = 0; k < ptrsPerBlock && written < dest.Length; k++)
                    {
                        uint blockNo = BitConverter.ToUInt32(indBlock, k * 4);
                        if (blockNo == 0) continue;
                        CopyBlock(blockNo, dest, ref written);
                    }
                }
            }
        }
    }

    private void CopyBlock(uint blockNo, byte[] dest, ref long written)
    {
        byte[] data = ReadBlock(blockNo);
        int toCopy = (int)Math.Min(data.Length, dest.Length - written);
        Buffer.BlockCopy(data, 0, dest, (int)written, toCopy);
        written += toCopy;
    }

    /// <summary>Reads a single filesystem block by its block number.</summary>
    public byte[] ReadBlock(long blockNo)
    {
        long offset = _partitionOffset + blockNo * _superblock.BlockSize;
        return _stream.ReadAt(offset, _superblock.BlockSize);
    }
}
