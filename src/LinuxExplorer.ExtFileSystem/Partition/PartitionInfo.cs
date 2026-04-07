namespace LinuxExplorer.ExtFileSystem.Partition;

/// <summary>Represents information about a single disk partition.</summary>
public sealed class PartitionInfo
{
    /// <summary>Gets the zero-based index of this partition on the disk.</summary>
    public int Index { get; init; }

    /// <summary>Gets the byte offset of the partition start from the disk beginning.</summary>
    public long StartOffset { get; init; }

    /// <summary>Gets the size of the partition in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Gets the partition type byte (MBR) or type GUID string (GPT).</summary>
    public string PartitionType { get; init; } = string.Empty;

    /// <summary>Gets whether this partition is likely to contain an ext2/3/4 filesystem.</summary>
    public bool IsExtFilesystem { get; init; }

    /// <summary>Gets the partition label/name if available (GPT only).</summary>
    public string? Label { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"Partition {Index}: offset={StartOffset}, size={Size}, type={PartitionType}, ext={IsExtFilesystem}";
}
