using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.IO;

/// <summary>
/// Allocates new inodes on an ext2/3/4 filesystem by reading and modifying inode bitmaps.
/// </summary>
public sealed class InodeAllocator
{
    private readonly DiskStream _stream;
    private readonly Superblock _superblock;
    private readonly BlockGroupDescriptor[] _groupDescriptors;
    private readonly long _partitionOffset;

    /// <summary>Initializes a new <see cref="InodeAllocator"/>.</summary>
    public InodeAllocator(DiskStream stream, Superblock superblock, BlockGroupDescriptor[] groupDescriptors, long partitionOffset)
    {
        _stream = stream;
        _superblock = superblock;
        _groupDescriptors = groupDescriptors;
        _partitionOffset = partitionOffset;
    }

    /// <summary>
    /// Allocates a free inode and returns its inode number (1-based).
    /// </summary>
    /// <exception cref="IOException">Thrown when no free inodes are available.</exception>
    public uint AllocateInode()
    {
        for (int g = 0; g < _groupDescriptors.Length; g++)
        {
            var bgd = _groupDescriptors[g];
            if (bgd.FreeInodesCount == 0) continue;

            byte[] bitmap = ReadBitmap((long)bgd.InodeBitmap);

            for (int i = 0; i < (int)_superblock.InodesPerGroup; i++)
            {
                if (!BitmapHelper.IsSet(bitmap, i))
                {
                    BitmapHelper.Set(bitmap, i);
                    WriteBitmap((long)bgd.InodeBitmap, bitmap);
                    // Inode numbers are 1-based
                    uint inodeNo = (uint)(g * _superblock.InodesPerGroup + i + 1);
                    return inodeNo;
                }
            }
        }

        throw new IOException("No free inodes available.");
    }

    /// <summary>Frees the specified inode by clearing its bit in the inode bitmap.</summary>
    public void FreeInode(uint inodeNo)
    {
        uint groupIdx = (inodeNo - 1) / _superblock.InodesPerGroup;
        int bitIdx = (int)((inodeNo - 1) % _superblock.InodesPerGroup);

        var bgd = _groupDescriptors[groupIdx];
        byte[] bitmap = ReadBitmap((long)bgd.InodeBitmap);
        BitmapHelper.Clear(bitmap, bitIdx);
        WriteBitmap((long)bgd.InodeBitmap, bitmap);
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
