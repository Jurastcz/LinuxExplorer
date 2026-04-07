namespace LinuxExplorer.ExtFileSystem.Ext;

/// <summary>
/// Represents an ext2/3/4 inode structure.
/// </summary>
public sealed class Inode
{
    /// <summary>Root directory inode number (always 2).</summary>
    public const uint RootInodeNumber = 2;

    // i_mode flags
    private const ushort S_IFIFO = 0x1000;
    private const ushort S_IFCHR = 0x2000;
    private const ushort S_IFDIR = 0x4000;
    private const ushort S_IFBLK = 0x6000;
    private const ushort S_IFREG = 0x8000;
    private const ushort S_IFLNK = 0xA000;
    private const ushort S_IFSOCK = 0xC000;
    private const ushort S_IFMT = 0xF000;

    // i_flags
    /// <summary>Flag indicating ext4 extent tree is used (EXT4_EXTENTS_FL).</summary>
    public const uint ExtentFlag = 0x00080000;

    /// <summary>File mode (type + permissions).</summary>
    public ushort Mode { get; private set; }

    /// <summary>Owner user ID (lo 16 bits).</summary>
    public ushort UidLo { get; private set; }

    /// <summary>File size in bytes (lo 32 bits).</summary>
    public uint SizeLo { get; private set; }

    /// <summary>Last access time (Unix timestamp).</summary>
    public uint ATime { get; private set; }

    /// <summary>Inode change time (Unix timestamp).</summary>
    public uint CTime { get; private set; }

    /// <summary>Last modification time (Unix timestamp).</summary>
    public uint MTime { get; private set; }

    /// <summary>Deletion time (Unix timestamp).</summary>
    public uint DTime { get; private set; }

    /// <summary>Owner group ID (lo 16 bits).</summary>
    public ushort GidLo { get; private set; }

    /// <summary>Number of hard links.</summary>
    public ushort LinksCount { get; private set; }

    /// <summary>Blocks count (lo 32 bits, in 512-byte units).</summary>
    public uint BlocksLo { get; private set; }

    /// <summary>Inode flags.</summary>
    public uint Flags { get; private set; }

    /// <summary>
    /// Block array: 15 entries (12 direct, 1 indirect, 1 double indirect, 1 triple indirect).
    /// For ext4 with extents, this holds the extent tree root.
    /// </summary>
    public uint[] Block { get; private set; } = new uint[15];

    /// <summary>Raw 60-byte i_block data (for extent tree parsing).</summary>
    public byte[] BlockRaw { get; private set; } = new byte[60];

    /// <summary>File size (hi 32 bits, for regular files in ext4).</summary>
    public uint SizeHigh { get; private set; }

    /// <summary>Owner UID (hi 16 bits).</summary>
    public ushort UidHi { get; private set; }

    /// <summary>Owner GID (hi 16 bits).</summary>
    public ushort GidHi { get; private set; }

    // ---- Computed ----

    /// <summary>Gets the full 64-bit file size.</summary>
    public long Size => ((long)SizeHigh << 32) | SizeLo;

    /// <summary>Gets the full UID.</summary>
    public uint Uid => ((uint)UidHi << 16) | UidLo;

    /// <summary>Gets the full GID.</summary>
    public uint Gid => ((uint)GidHi << 16) | GidLo;

    /// <summary>Gets whether the extent tree is used (ext4).</summary>
    public bool UsesExtents => (Flags & ExtentFlag) != 0;

    /// <summary>Gets whether this inode represents a regular file.</summary>
    public bool IsRegularFile => (Mode & S_IFMT) == S_IFREG;

    /// <summary>Gets whether this inode represents a directory.</summary>
    public bool IsDirectory => (Mode & S_IFMT) == S_IFDIR;

    /// <summary>Gets whether this inode represents a symbolic link.</summary>
    public bool IsSymlink => (Mode & S_IFMT) == S_IFLNK;

    /// <summary>Gets the Unix permission bits (lower 12 bits of mode).</summary>
    public ushort Permissions => (ushort)(Mode & 0x0FFF);

    /// <summary>
    /// Parses an inode from the given byte array.
    /// </summary>
    /// <param name="data">Raw byte data.</param>
    /// <param name="offset">Offset within data.</param>
    /// <param name="inodeSize">Inode size in bytes (from superblock).</param>
    public static Inode Parse(byte[] data, int offset, int inodeSize = 128)
    {
        var inode = new Inode();
        inode.Mode = BitConverter.ToUInt16(data, offset + 0);
        inode.UidLo = BitConverter.ToUInt16(data, offset + 2);
        inode.SizeLo = BitConverter.ToUInt32(data, offset + 4);
        inode.ATime = BitConverter.ToUInt32(data, offset + 8);
        inode.CTime = BitConverter.ToUInt32(data, offset + 12);
        inode.MTime = BitConverter.ToUInt32(data, offset + 16);
        inode.DTime = BitConverter.ToUInt32(data, offset + 20);
        inode.GidLo = BitConverter.ToUInt16(data, offset + 24);
        inode.LinksCount = BitConverter.ToUInt16(data, offset + 26);
        inode.BlocksLo = BitConverter.ToUInt32(data, offset + 28);
        inode.Flags = BitConverter.ToUInt32(data, offset + 32);

        // i_block[15] starts at offset 40, 60 bytes total
        Buffer.BlockCopy(data, offset + 40, inode.BlockRaw, 0, 60);
        for (int i = 0; i < 15; i++)
            inode.Block[i] = BitConverter.ToUInt32(data, offset + 40 + i * 4);

        if (inodeSize >= 128)
        {
            inode.SizeHigh = BitConverter.ToUInt32(data, offset + 108);
        }

        if (inodeSize >= 128 && data.Length >= offset + 122)
        {
            // i_uid_high, i_gid_high at 116, 118 (extra inode fields for EXT4)
            if (data.Length >= offset + 122)
            {
                inode.UidHi = BitConverter.ToUInt16(data, offset + 116);
                inode.GidHi = BitConverter.ToUInt16(data, offset + 118);
            }
        }

        return inode;
    }
}
