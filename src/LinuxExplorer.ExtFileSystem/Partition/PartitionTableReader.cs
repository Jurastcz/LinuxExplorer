using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.RawDisk;
using System.Reflection.PortableExecutable;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
    /// If no partition table is found, checks for a "superfloppy" ext filesystem
    /// covering the entire disk (common with USB drives).
    /// </summary>
    public IReadOnlyList<PartitionInfo> ReadPartitions()
    {
        byte[] sector0 = _stream.ReadAt(0, SectorSize);
        byte[] sector1 = _stream.ReadAt(SectorSize, SectorSize);

        ulong sig = BitConverter.ToUInt64(sector1, 0);
        List<PartitionInfo> partitions = new List<PartitionInfo>();

        if (sig == GptSignature)
        {
            partitions = ReadGptPartitions(sector1).ToList();
        }
        else
        {
            partitions = ReadMbrPartitions(sector0).ToList();
        }

        // Jeśli nie znaleziono żadnej partycji ext, spróbuj superfloppy
        if (!partitions.Any(p => p.IsExtFilesystem))
        {
            var superfloppy = TryReadSuperfloppyExt();
            if (superfloppy.Count > 0)
                return superfloppy;
        }

        return partitions;
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

            long startOffset = (long)entry.LbaStart * SectorSize;
            bool isExt = IsExtFilesystemAtOffset(startOffset);
            string? label = isExt ? TryReadExtVolumeLabel(startOffset) : null;

            partitions.Add(new PartitionInfo
            {
                Index = idx++,
                StartOffset = startOffset,
                Size = (long)entry.LbaCount * SectorSize,
                PartitionType = $"0x{entry.Type:X2}",
                IsExtFilesystem = isExt,
                Label = label
            });
        }

        return partitions;
    }

    private List<PartitionInfo> ReadGptPartitions(byte[] gptHeader)
    {
        uint partEntrySize = BitConverter.ToUInt32(gptHeader, 84);
        ulong partEntryLba = BitConverter.ToUInt64(gptHeader, 72);
        uint numPartitions = BitConverter.ToUInt32(gptHeader, 80);

        partEntrySize = partEntrySize == 0 ? 128u : partEntrySize;
        if (numPartitions > 128) numPartitions = 128;

        var partitions = new List<PartitionInfo>();
        long tableOffset = (long)partEntryLba * SectorSize;
        int tableSize = (int)(numPartitions * partEntrySize);
        int alignedSize = ((tableSize + SectorSize - 1) / SectorSize) * SectorSize;
        byte[] table = _stream.ReadAt(tableOffset, alignedSize);

        int idx = 0;
        for (int i = 0; i < (int)numPartitions; i++)
        {
            int offset = i * (int)partEntrySize;
            var entry = GptPartitionEntry.Parse(table, offset);
            if (entry == null) continue;

            long startOffset = (long)entry.StartLba * SectorSize;
            bool isExt = IsExtFilesystemAtOffset(startOffset);

            // Prefer ext volume label over GPT partition name
            string? label = entry.Name;
            if (isExt)
            {
                string? extLabel = TryReadExtVolumeLabel(startOffset);
                if (!string.IsNullOrEmpty(extLabel))
                    label = extLabel;
            }

            partitions.Add(new PartitionInfo
            {
                Index = idx++,
                StartOffset = startOffset,
                Size = (long)(entry.EndLba - entry.StartLba + 1) * SectorSize,
                PartitionType = entry.TypeGuid.ToString("D").ToUpperInvariant(),
                IsExtFilesystem = isExt,
                Label = label
            });
        }
        return partitions;
    }

    /// <summary>
    /// Checks if there is an ext2/3/4 filesystem at the given offset by reading the superblock and verifying the magic number.
    /// </summary>
    /// <param name="startOffset">The offset at which to check for the filesystem.</param>
    /// <returns>True if an ext2/3/4 filesystem is found, otherwise false.</returns>
    private bool IsExtFilesystemAtOffset(long startOffset)
    {
        try
        {
            byte[] data = _stream.ReadAt(startOffset + Superblock.SuperblockOffset, Superblock.SuperblockSize);
            ushort magic = BitConverter.ToUInt16(data, 56);
            return magic == Superblock.Ext2Magic;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tries to read the volume label from an ext2/3/4 filesystem at the given offset.
    /// </summary>
    /// <param name="startOffset">The offset at which to read the superblock.</param>
    /// <returns>The volume label if found and non-empty, otherwise null.</returns>
    private string? TryReadExtVolumeLabel(long startOffset)
    {
        try
        {
            byte[] data = _stream.ReadAt(startOffset + Superblock.SuperblockOffset, Superblock.SuperblockSize);
            ushort magic = BitConverter.ToUInt16(data, 56);
            if (magic != Superblock.Ext2Magic)
                return null;

            var sb = Superblock.Parse(data);
            return string.IsNullOrEmpty(sb.VolumeName) ? null : sb.VolumeName;
        }
        catch
        {
            return null;
        }
    }
    /// <summary>
    /// Checks whether the disk is a "superfloppy" – an ext2/3/4 filesystem
    /// that covers the entire disk without a partition table.
    /// </summary>
    private List<PartitionInfo> TryReadSuperfloppyExt()
    {
        try
        {
            // The ext superblock starts at offset 1024; read enough for magic check
            int readSize = ((Superblock.SuperblockOffset + Superblock.SuperblockSize + SectorSize - 1) / SectorSize) * SectorSize;
            byte[] data = _stream.ReadAt(0, readSize);

            // Magic number is at offset 56 within the superblock (absolute offset 1024 + 56 = 1080)
            ushort magic = BitConverter.ToUInt16(data, Superblock.SuperblockOffset + 56);
            if (magic != Superblock.Ext2Magic)
                return [];

            var sb = Superblock.Parse(data.AsSpan(Superblock.SuperblockOffset, Superblock.SuperblockSize).ToArray());
            long totalSize = (long)sb.BlocksCountLo * sb.BlockSize;

            return
            [
                new PartitionInfo
                {
                    Index = 0,
                    StartOffset = 0,
                    Size = totalSize,
                    PartitionType = "superfloppy",
                    IsExtFilesystem = true,
                    Label = string.IsNullOrEmpty(sb.VolumeName) ? null : sb.VolumeName
                }
            ];
        }
        catch
        {
            return [];
        }
    }
}
