namespace LinuxExplorer.ExtFileSystem.Ext;

/// <summary>
/// Represents and parses the ext2/3/4 superblock structure.
/// The superblock is located at offset 1024 from the beginning of the partition.
/// </summary>
public sealed class Superblock
{
    /// <summary>The well-known ext2 magic number (0xEF53).</summary>
    public const ushort Ext2Magic = 0xEF53;

    /// <summary>Superblock offset from partition start (1024 bytes).</summary>
    public const int SuperblockOffset = 1024;

    /// <summary>Superblock size in bytes (1024 bytes).</summary>
    public const int SuperblockSize = 1024;

    // ---- Core fields ----
    /// <summary>Total number of inodes.</summary>
    public uint InodesCount { get; private set; }

    /// <summary>Total number of blocks (lo 32 bits).</summary>
    public uint BlocksCountLo { get; private set; }

    /// <summary>Reserved blocks count (lo 32 bits).</summary>
    public uint RBlocksCountLo { get; private set; }

    /// <summary>Free blocks count (lo 32 bits).</summary>
    public uint FreeBlocksCountLo { get; private set; }

    /// <summary>Free inodes count.</summary>
    public uint FreeInodesCount { get; private set; }

    /// <summary>First data block (0 for filesystems with block size > 1024, 1 for 1024-byte blocks).</summary>
    public uint FirstDataBlock { get; private set; }

    /// <summary>Block size expressed as a power of 2: block_size = 1024 &lt;&lt; s_log_block_size.</summary>
    public uint LogBlockSize { get; private set; }

    /// <summary>Blocks per group.</summary>
    public uint BlocksPerGroup { get; private set; }

    /// <summary>Inodes per group.</summary>
    public uint InodesPerGroup { get; private set; }

    /// <summary>Mount time (Unix timestamp).</summary>
    public uint MountTime { get; private set; }

    /// <summary>Write time (Unix timestamp).</summary>
    public uint WriteTime { get; private set; }

    /// <summary>Magic number – must equal <see cref="Ext2Magic"/>.</summary>
    public ushort Magic { get; private set; }

    /// <summary>Filesystem state (1 = clean, 2 = errors).</summary>
    public ushort State { get; private set; }

    /// <summary>Inode size (EXT2 default: 128 bytes, EXT4 typically 256 bytes).</summary>
    public ushort InodeSize { get; private set; }

    /// <summary>Block group number of this superblock.</summary>
    public ushort BlockGroupNr { get; private set; }

    /// <summary>Compatible feature set flags.</summary>
    public uint FeatureCompat { get; private set; }

    /// <summary>Incompatible feature set flags.</summary>
    public uint FeatureIncompat { get; private set; }

    /// <summary>Read-only compatible feature set flags.</summary>
    public uint FeatureRoCompat { get; private set; }

    /// <summary>Volume UUID.</summary>
    public byte[] Uuid { get; private set; } = new byte[16];

    /// <summary>Volume name (null-terminated, 16 bytes).</summary>
    public string VolumeName { get; private set; } = string.Empty;

    /// <summary>Size of group descriptor – used for 64-bit block groups.</summary>
    public ushort DescSize { get; private set; }

    /// <summary>Number of block groups.</summary>
    public uint BlockGroupCount { get; private set; }

    // ---- Computed properties ----

    /// <summary>Gets the actual block size in bytes.</summary>
    public int BlockSize => 1024 << (int)LogBlockSize;

    /// <summary>Gets whether the magic number is valid.</summary>
    public bool IsValid => Magic == Ext2Magic;

    // Feature flags
    private const uint CompatDirIndex = 0x0020;
    private const uint IncompatFiletype = 0x0002;
    private const uint IncompatExtents = 0x0040;
    private const uint IncompatFlexBg = 0x0200;
    private const uint IncompatBitmap64bit = 0x1000;
    private const uint RoCompatSparseSuper = 0x0001;
    private const uint RoCompatLargeFile = 0x0002;
    private const uint RoCompatHugeFile = 0x0008;
    private const uint RoCompatMetaBg = 0x0010;
    private const uint RoCompatBgUseMetaChecksum = 0x0400;
    private const uint Incompat64bit = 0x0080;

    /// <summary>Gets whether extents are supported (ext4 feature).</summary>
    public bool HasExtents => (FeatureIncompat & IncompatExtents) != 0;

    /// <summary>Gets whether 64-bit block group descriptors are used.</summary>
    public bool Has64Bit => (FeatureIncompat & Incompat64bit) != 0;

    /// <summary>Gets whether filetype is stored in directory entries.</summary>
    public bool HasFileType => (FeatureIncompat & IncompatFiletype) != 0;

    /// <summary>Gets the effective descriptor size (32 if not 64-bit, s_desc_size otherwise).</summary>
    public int EffectiveDescSize => Has64Bit && DescSize >= 64 ? DescSize : 32;

    /// <summary>
    /// Gets a human-readable filesystem type string (ext2/ext3/ext4).
    /// </summary>
    public string FilesystemType
    {
        get
        {
            if (HasExtents) return "ext4";
            if ((FeatureCompat & 0x0004) != 0) return "ext3"; // HAS_JOURNAL
            return "ext2";
        }
    }

    /// <summary>
    /// Parses a superblock from the given byte array.
    /// </summary>
    /// <param name="data">Byte array of at least 1024 bytes containing the raw superblock.</param>
    /// <returns>A parsed <see cref="Superblock"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when data is too short.</exception>
    public static Superblock Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < SuperblockSize)
            throw new ArgumentException($"Superblock data must be at least {SuperblockSize} bytes.", nameof(data));

        var sb = new Superblock();
        sb.InodesCount = BitConverter.ToUInt32(data, 0);
        sb.BlocksCountLo = BitConverter.ToUInt32(data, 4);
        sb.RBlocksCountLo = BitConverter.ToUInt32(data, 8);
        sb.FreeBlocksCountLo = BitConverter.ToUInt32(data, 12);
        sb.FreeInodesCount = BitConverter.ToUInt32(data, 16);
        sb.FirstDataBlock = BitConverter.ToUInt32(data, 20);
        sb.LogBlockSize = BitConverter.ToUInt32(data, 24);
        sb.BlocksPerGroup = BitConverter.ToUInt32(data, 32);
        sb.InodesPerGroup = BitConverter.ToUInt32(data, 40);
        sb.MountTime = BitConverter.ToUInt32(data, 44);
        sb.WriteTime = BitConverter.ToUInt32(data, 48);
        sb.Magic = BitConverter.ToUInt16(data, 56);
        sb.State = BitConverter.ToUInt16(data, 58);
        sb.InodeSize = (data.Length >= 90) ? BitConverter.ToUInt16(data, 88) : (ushort)128;
        if (sb.InodeSize < 128) sb.InodeSize = 128;
        sb.BlockGroupNr = (data.Length >= 92) ? BitConverter.ToUInt16(data, 90) : (ushort)0;
        sb.FeatureCompat = (data.Length >= 96) ? BitConverter.ToUInt32(data, 92) : 0;
        sb.FeatureIncompat = (data.Length >= 100) ? BitConverter.ToUInt32(data, 96) : 0;
        sb.FeatureRoCompat = (data.Length >= 104) ? BitConverter.ToUInt32(data, 100) : 0;

        if (data.Length >= 120)
            Buffer.BlockCopy(data, 104, sb.Uuid, 0, 16);

        if (data.Length >= 136)
        {
            // Volume name: 16 bytes ASCII null-terminated
            int nameEnd = Array.IndexOf(data, (byte)0, 120, 16);
            int nameLen = nameEnd >= 0 ? nameEnd - 120 : 16;
            sb.VolumeName = System.Text.Encoding.ASCII.GetString(data, 120, nameLen);
        }

        // s_desc_size at offset 0xFE (254) in the superblock
        sb.DescSize = (data.Length >= 256) ? BitConverter.ToUInt16(data, 254) : (ushort)0;

        // Calculate block group count
        if (sb.BlocksPerGroup > 0 && sb.BlocksCountLo > 0)
        {
            sb.BlockGroupCount = (sb.BlocksCountLo + sb.BlocksPerGroup - 1) / sb.BlocksPerGroup;
        }

        return sb;
    }
}
