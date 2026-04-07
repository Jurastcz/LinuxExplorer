using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.IO;

/// <summary>
/// Allocates new blocks on an ext2/3/4 filesystem by reading and modifying block bitmaps.
/// </summary>
public sealed class BlockAllocator
{
    private readonly DiskStream _stream;
    private readonly Superblock _superblock;
    private readonly BlockGroupDescriptor[] _groupDescriptors;
    private readonly long _partitionOffset;

    /// <summary>
    /// Initializes a new <see cref="BlockAllocator"/>.
    /// </summary>
    public BlockAllocator(DiskStream stream, Superblock superblock, BlockGroupDescriptor[] groupDescriptors, long partitionOffset)
    {
        _stream = stream;
        _superblock = superblock;
        _groupDescriptors = groupDescriptors;
        _partitionOffset = partitionOffset;
    }

    /// <summary>
    /// Allocates a contiguous run of <paramref name="count"/> blocks.
    /// Returns the block numbers of allocated blocks.
    /// </summary>
    /// <exception cref="IOException">Thrown when not enough free blocks are available.</exception>
    public List<uint> AllocateBlocks(int count)
    {
        var allocated = new List<uint>();

        for (int g = 0; g < _groupDescriptors.Length && allocated.Count < count; g++)
        {
            var bgd = _groupDescriptors[g];
            if (bgd.FreeBlocksCount == 0) continue;

            byte[] bitmap = ReadBitmap((long)bgd.BlockBitmap);
            uint groupBase = (uint)(_superblock.FirstDataBlock + (uint)g * _superblock.BlocksPerGroup);

            for (int b = 0; b < (int)_superblock.BlocksPerGroup && allocated.Count < count; b++)
            {
                if (!BitmapHelper.IsSet(bitmap, b))
                {
                    BitmapHelper.Set(bitmap, b);
                    uint blockNo = groupBase + (uint)b;
                    allocated.Add(blockNo);
                }
            }

            WriteBitmap((long)bgd.BlockBitmap, bitmap);
        }

        if (allocated.Count < count)
            throw new IOException($"Not enough free blocks. Needed {count}, found {allocated.Count}.");

        return allocated;
    }

    /// <summary>Frees the specified block by clearing its bit in the block bitmap.</summary>
    public void FreeBlock(uint blockNo)
    {
        uint groupIdx = (blockNo - _superblock.FirstDataBlock) / _superblock.BlocksPerGroup;
        int bitIdx = (int)((blockNo - _superblock.FirstDataBlock) % _superblock.BlocksPerGroup);

        var bgd = _groupDescriptors[groupIdx];
        byte[] bitmap = ReadBitmap((long)bgd.BlockBitmap);
        BitmapHelper.Clear(bitmap, bitIdx);
        WriteBitmap((long)bgd.BlockBitmap, bitmap);
    }

    private byte[] ReadBitmap(long bitmapBlock)
    {
        long offset = _partitionOffset + bitmapBlock * _superblock.BlockSize;
        return _stream.ReadAt(offset, _superblock.BlockSize);
    }

    private void WriteBitmap(long bitmapBlock, byte[] bitmap)
    {
        long offset = _partitionOffset + bitmapBlock * _superblock.BlockSize;
        _stream.WriteAt(offset, bitmap);
    }
}
