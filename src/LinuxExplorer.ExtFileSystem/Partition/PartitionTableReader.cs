using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.Partition;

/// <summary>
/// Reads partition tables (MBR and GPT) from a raw disk and enumerates partitions.
/// </summary>
public sealed class PartitionTableReader
{
    private const int SectorSize = 512;
    private const ulong GptSignature = 0x5452415020494645UL; // "EFI PART"

    private readonly DiskStream _stream;

    /// <summary>Initializes a new <see cref="PartitionTableReader"/> for the given disk stream.</summary>
    public PartitionTableReader(DiskStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Reads the partition table and returns all found partitions.
    /// Tries GPT first, then falls back to MBR.
    /// </summary>
    public IReadOnlyList<PartitionInfo> ReadPartitions()
    {
        // Read first two sectors
        byte[] sector0 = _stream.ReadAt(0, SectorSize);
        byte[] sector1 = _stream.ReadAt(SectorSize, SectorSize);

        // Check for GPT signature in sector 1
        ulong sig = BitConverter.ToUInt64(sector1, 0);
        if (sig == GptSignature)
            return ReadGptPartitions(sector1);

        // Fall back to MBR
        return ReadMbrPartitions(sector0);
    }

    private List<PartitionInfo> ReadMbrPartitions(byte[] mbr)
    {
        // Validate MBR boot signature
        if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            return [];

        var partitions = new List<PartitionInfo>();
        int idx = 0;

        for (int i = 0; i < 4; i++)
        {
            var entry = MbrPartitionEntry.Parse(mbr, 446 + i * 16);
            if (entry.LbaCount == 0) continue;

            partitions.Add(new PartitionInfo
            {
                Index = idx++,
                StartOffset = (long)entry.LbaStart * SectorSize,
                Size = (long)entry.LbaCount * SectorSize,
                PartitionType = $"0x{entry.Type:X2}",
                IsExtFilesystem = entry.IsLinuxExt
            });
        }

        return partitions;
    }

    private List<PartitionInfo> ReadGptPartitions(byte[] gptHeader)
    {
        // Parse GPT header
        uint partEntrySize = BitConverter.ToUInt32(gptHeader, 84);
        ulong partEntryLba = BitConverter.ToUInt64(gptHeader, 72);
        uint numPartitions = BitConverter.ToUInt32(gptHeader, 80);

        partEntrySize = partEntrySize == 0 ? 128u : partEntrySize;
        if (numPartitions > 128) numPartitions = 128;

        var partitions = new List<PartitionInfo>();
        long tableOffset = (long)partEntryLba * SectorSize;
        int tableSize = (int)(numPartitions * partEntrySize);
        // Align to sector
        int alignedSize = ((tableSize + SectorSize - 1) / SectorSize) * SectorSize;
        byte[] table = _stream.ReadAt(tableOffset, alignedSize);

        int idx = 0;
        for (int i = 0; i < (int)numPartitions; i++)
        {
            int offset = i * (int)partEntrySize;
            var entry = GptPartitionEntry.Parse(table, offset);
            if (entry == null) continue;

            partitions.Add(new PartitionInfo
            {
                Index = idx++,
                StartOffset = (long)entry.StartLba * SectorSize,
                Size = (long)(entry.EndLba - entry.StartLba + 1) * SectorSize,
                PartitionType = entry.TypeGuid.ToString("D").ToUpperInvariant(),
                IsExtFilesystem = entry.IsLinuxFilesystem,
                Label = entry.Name
            });
        }

        return partitions;
    }
}
