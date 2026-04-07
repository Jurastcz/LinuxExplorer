namespace LinuxExplorer.ExtFileSystem.Partition;

/// <summary>Represents a single MBR (Master Boot Record) partition entry.</summary>
public sealed class MbrPartitionEntry
{
    /// <summary>MBR partition type byte for Linux ext2/3/4 (0x83).</summary>
    public const byte LinuxType = 0x83;

    /// <summary>Gets whether the partition is marked as bootable.</summary>
    public bool Bootable { get; init; }

    /// <summary>Gets the partition type byte.</summary>
    public byte Type { get; init; }

    /// <summary>Gets the LBA start sector (28-bit).</summary>
    public uint LbaStart { get; init; }

    /// <summary>Gets the LBA sector count.</summary>
    public uint LbaCount { get; init; }

    /// <summary>Gets whether this partition type indicates a Linux ext filesystem.</summary>
    public bool IsLinuxExt => Type == LinuxType;

    /// <summary>
    /// Parses an MBR partition entry from 16 bytes at the given offset within <paramref name="data"/>.
    /// </summary>
    public static MbrPartitionEntry Parse(byte[] data, int offset) =>
        new()
        {
            Bootable = data[offset] == 0x80,
            Type = data[offset + 4],
            LbaStart = BitConverter.ToUInt32(data, offset + 8),
            LbaCount = BitConverter.ToUInt32(data, offset + 12)
        };
}
