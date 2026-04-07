namespace LinuxExplorer.ExtFileSystem.Ext;

/// <summary>
/// Represents a block group descriptor, supporting both 32-bit (ext2/ext3) and 64-bit (ext4) formats.
/// </summary>
public sealed class BlockGroupDescriptor
{
    /// <summary>Size of a 32-bit block group descriptor in bytes.</summary>
    public const int Size32 = 32;

    /// <summary>Block bitmap location (lo 32 bits).</summary>
    public uint BlockBitmapLo { get; private set; }

    /// <summary>Inode bitmap location (lo 32 bits).</summary>
    public uint InodeBitmapLo { get; private set; }

    /// <summary>Inode table location (lo 32 bits).</summary>
    public uint InodeTableLo { get; private set; }

    /// <summary>Free blocks count (lo 16 bits).</summary>
    public ushort FreeBlocksCountLo { get; private set; }

    /// <summary>Free inodes count (lo 16 bits).</summary>
    public ushort FreeInodesCountLo { get; private set; }

    /// <summary>Used directories count.</summary>
    public ushort UsedDirsCount { get; private set; }

    // 64-bit extensions (only valid when Superblock.Has64Bit is true and DescSize >= 64)
    /// <summary>Block bitmap location (hi 32 bits, ext4 64-bit).</summary>
    public uint BlockBitmapHi { get; private set; }

    /// <summary>Inode bitmap location (hi 32 bits, ext4 64-bit).</summary>
    public uint InodeBitmapHi { get; private set; }

    /// <summary>Inode table location (hi 32 bits, ext4 64-bit).</summary>
    public uint InodeTableHi { get; private set; }

    /// <summary>Free blocks count (hi 16 bits, ext4 64-bit).</summary>
    public ushort FreeBlocksCountHi { get; private set; }

    /// <summary>Free inodes count (hi 16 bits, ext4 64-bit).</summary>
    public ushort FreeInodesCountHi { get; private set; }

    /// <summary>Gets the full 64-bit block bitmap block number.</summary>
    public ulong BlockBitmap => ((ulong)BlockBitmapHi << 32) | BlockBitmapLo;

    /// <summary>Gets the full 64-bit inode bitmap block number.</summary>
    public ulong InodeBitmap => ((ulong)InodeBitmapHi << 32) | InodeBitmapLo;

    /// <summary>Gets the full 64-bit inode table block number.</summary>
    public ulong InodeTable => ((ulong)InodeTableHi << 32) | InodeTableLo;

    /// <summary>Gets the total free blocks count combining lo and hi parts.</summary>
    public uint FreeBlocksCount => (uint)FreeBlocksCountLo | ((uint)FreeBlocksCountHi << 16);

    /// <summary>Gets the total free inodes count combining lo and hi parts.</summary>
    public uint FreeInodesCount => (uint)FreeInodesCountLo | ((uint)FreeInodesCountHi << 16);

    /// <summary>
    /// Parses a block group descriptor from raw data.
    /// </summary>
    /// <param name="data">Raw byte array containing the descriptor data.</param>
    /// <param name="offset">Offset within <paramref name="data"/> where the descriptor starts.</param>
    /// <param name="descSize">Size of the descriptor (32 for ext2/3, 64+ for ext4 64-bit).</param>
    public static BlockGroupDescriptor Parse(byte[] data, int offset, int descSize = Size32)
    {
        var bgd = new BlockGroupDescriptor();
        bgd.BlockBitmapLo = BitConverter.ToUInt32(data, offset + 0);
        bgd.InodeBitmapLo = BitConverter.ToUInt32(data, offset + 4);
        bgd.InodeTableLo = BitConverter.ToUInt32(data, offset + 8);
        bgd.FreeBlocksCountLo = BitConverter.ToUInt16(data, offset + 12);
        bgd.FreeInodesCountLo = BitConverter.ToUInt16(data, offset + 14);
        bgd.UsedDirsCount = BitConverter.ToUInt16(data, offset + 16);

        if (descSize >= 64 && data.Length >= offset + 64)
        {
            bgd.BlockBitmapHi = BitConverter.ToUInt32(data, offset + 32);
            bgd.InodeBitmapHi = BitConverter.ToUInt32(data, offset + 36);
            bgd.InodeTableHi = BitConverter.ToUInt32(data, offset + 40);
            bgd.FreeBlocksCountHi = BitConverter.ToUInt16(data, offset + 44);
            bgd.FreeInodesCountHi = BitConverter.ToUInt16(data, offset + 46);
        }

        return bgd;
    }
}
