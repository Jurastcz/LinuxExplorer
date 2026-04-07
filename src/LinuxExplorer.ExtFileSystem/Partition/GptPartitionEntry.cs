namespace LinuxExplorer.ExtFileSystem.Partition;

/// <summary>Represents a single GPT (GUID Partition Table) partition entry.</summary>
public sealed class GptPartitionEntry
{
    /// <summary>GPT partition type GUID for Linux filesystem data partitions.</summary>
    public const string LinuxFilesystemDataGuid = "0FC63DAF-8483-4772-8E79-3D69D8477DE4";

    /// <summary>Gets the partition type GUID.</summary>
    public Guid TypeGuid { get; init; }

    /// <summary>Gets the unique partition GUID.</summary>
    public Guid UniqueGuid { get; init; }

    /// <summary>Gets the starting LBA of the partition.</summary>
    public ulong StartLba { get; init; }

    /// <summary>Gets the ending LBA (inclusive) of the partition.</summary>
    public ulong EndLba { get; init; }

    /// <summary>Gets the partition name (UTF-16LE, up to 36 characters).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets whether this partition type indicates a Linux data filesystem.</summary>
    public bool IsLinuxFilesystem =>
        string.Equals(TypeGuid.ToString("D").ToUpperInvariant(), LinuxFilesystemDataGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a GPT partition entry from 128 bytes at the given offset within <paramref name="data"/>.
    /// </summary>
    public static GptPartitionEntry? Parse(byte[] data, int offset)
    {
        var typeBytes = new byte[16];
        Buffer.BlockCopy(data, offset, typeBytes, 0, 16);
        var typeGuid = new Guid(typeBytes);
        if (typeGuid == Guid.Empty) return null;

        var uniqueBytes = new byte[16];
        Buffer.BlockCopy(data, offset + 16, uniqueBytes, 0, 16);

        ulong startLba = BitConverter.ToUInt64(data, offset + 32);
        ulong endLba = BitConverter.ToUInt64(data, offset + 40);

        // Name: 72 bytes, UTF-16LE, null-terminated
        string name = System.Text.Encoding.Unicode.GetString(data, offset + 56, 72).TrimEnd('\0');

        return new GptPartitionEntry
        {
            TypeGuid = typeGuid,
            UniqueGuid = new Guid(uniqueBytes),
            StartLba = startLba,
            EndLba = endLba,
            Name = name
        };
    }
}
