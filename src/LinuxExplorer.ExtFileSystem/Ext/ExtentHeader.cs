namespace LinuxExplorer.ExtFileSystem.Ext;

/// <summary>
/// The header of an ext4 extent tree node.
/// Stored at the beginning of each extent tree block (and in the inode i_block).
/// </summary>
public sealed class ExtentHeader
{
    /// <summary>Magic number for extent tree nodes (0xF30A).</summary>
    public const ushort ExtentMagic = 0xF30A;

    /// <summary>Magic number to validate the header.</summary>
    public ushort Magic { get; private set; }

    /// <summary>Number of valid entries following the header.</summary>
    public ushort Entries { get; private set; }

    /// <summary>Maximum capacity of entries following the header.</summary>
    public ushort Max { get; private set; }

    /// <summary>Depth of this extent tree node (0 = leaf node with Extent entries).</summary>
    public ushort Depth { get; private set; }

    /// <summary>Generation (not used in parsing).</summary>
    public uint Generation { get; private set; }

    /// <summary>Gets whether the magic number is valid.</summary>
    public bool IsValid => Magic == ExtentMagic;

    /// <summary>Parses an extent header from the given data at the specified offset.</summary>
    public static ExtentHeader Parse(byte[] data, int offset)
    {
        return new ExtentHeader
        {
            Magic = BitConverter.ToUInt16(data, offset + 0),
            Entries = BitConverter.ToUInt16(data, offset + 2),
            Max = BitConverter.ToUInt16(data, offset + 4),
            Depth = BitConverter.ToUInt16(data, offset + 6),
            Generation = BitConverter.ToUInt32(data, offset + 8)
        };
    }

    /// <summary>Size of the extent header structure in bytes.</summary>
    public const int Size = 12;
}

/// <summary>
/// An ext4 extent index node entry – points to a child node in the extent tree.
/// Present when <see cref="ExtentHeader.Depth"/> &gt; 0.
/// </summary>
public sealed class ExtentIndex
{
    /// <summary>First logical block covered by this index entry.</summary>
    public uint Block { get; private set; }

    /// <summary>Physical block of the child node (lo 32 bits).</summary>
    public uint LeafLo { get; private set; }

    /// <summary>Physical block of the child node (hi 16 bits).</summary>
    public ushort LeafHi { get; private set; }

    /// <summary>Gets the full 48-bit physical block number of the child node.</summary>
    public ulong Leaf => ((ulong)LeafHi << 32) | LeafLo;

    /// <summary>Parses an extent index entry from the given data at the specified offset.</summary>
    public static ExtentIndex Parse(byte[] data, int offset)
    {
        return new ExtentIndex
        {
            Block = BitConverter.ToUInt32(data, offset + 0),
            LeafLo = BitConverter.ToUInt32(data, offset + 4),
            LeafHi = BitConverter.ToUInt16(data, offset + 8)
        };
    }

    /// <summary>Size of an extent index entry in bytes.</summary>
    public const int Size = 12;
}

/// <summary>
/// An ext4 leaf extent entry – directly maps logical blocks to physical blocks.
/// Present when <see cref="ExtentHeader.Depth"/> == 0.
/// </summary>
public sealed class Extent
{
    /// <summary>First logical block number this extent covers.</summary>
    public uint Block { get; private set; }

    /// <summary>Length of this extent in blocks. High bit set means uninitialized.</summary>
    public ushort Len { get; private set; }

    /// <summary>Starting physical block (hi 16 bits).</summary>
    public ushort StartHi { get; private set; }

    /// <summary>Starting physical block (lo 32 bits).</summary>
    public uint StartLo { get; private set; }

    /// <summary>Gets the actual length in blocks (clears the uninitialized flag).</summary>
    public int Length => Len & 0x7FFF;

    /// <summary>Gets whether this extent is uninitialized (pre-allocated).</summary>
    public bool IsUninitialized => (Len & 0x8000) != 0;

    /// <summary>Gets the full 48-bit starting physical block number.</summary>
    public ulong Start => ((ulong)StartHi << 32) | StartLo;

    /// <summary>Parses an extent leaf entry from the given data at the specified offset.</summary>
    public static Extent Parse(byte[] data, int offset)
    {
        return new Extent
        {
            Block = BitConverter.ToUInt32(data, offset + 0),
            Len = BitConverter.ToUInt16(data, offset + 4),
            StartHi = BitConverter.ToUInt16(data, offset + 6),
            StartLo = BitConverter.ToUInt32(data, offset + 8)
        };
    }

    /// <summary>Size of an extent leaf entry in bytes.</summary>
    public const int Size = 12;
}
