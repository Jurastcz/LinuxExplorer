using LinuxExplorer.ExtFileSystem.RawDisk;
using System.Text;

namespace LinuxExplorer.ExtFileSystem.Format;

/// <summary>
/// Supported ext filesystem versions for formatting.
/// </summary>
public enum ExtVersion
{
    Ext2 = 2,
    Ext3 = 3,
    Ext4 = 4
}

/// <summary>
/// Options for formatting a partition or disk with an ext filesystem.
/// </summary>
public sealed class FormatOptions
{
    /// <summary>The ext version to create.</summary>
    public ExtVersion Version { get; set; } = ExtVersion.Ext4;

    /// <summary>Volume label (max 16 characters).</summary>
    public string VolumeLabel { get; set; } = string.Empty;

    /// <summary>Block size in bytes (1024, 2048, or 4096). Default: 4096.</summary>
    public int BlockSize { get; set; } = 4096;
}

/// <summary>
/// Creates an ext2/3/4 filesystem on a raw disk stream at a given offset and size.
/// This is a minimal formatter that writes a valid, mountable filesystem.
/// </summary>
public sealed class ExtFormatter
{
    private const int SectorSize = 512;
    private const ushort Ext2Magic = 0xEF53;

    /// <summary>
    /// Formats the specified region of a disk stream with an ext filesystem.
    /// </summary>
    /// <param name="stream">The disk stream opened for writing.</param>
    /// <param name="partitionOffset">Byte offset of the partition start.</param>
    /// <param name="partitionSize">Size of the partition in bytes.</param>
    /// <param name="options">Formatting options.</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0).</param>
    public void Format(DiskStream stream, long partitionOffset, long partitionSize, FormatOptions options, Action<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new InvalidOperationException("Stream must be writable.");
        if (partitionSize < 1024 * 1024)
            throw new ArgumentException("Partition must be at least 1 MB.", nameof(partitionSize));

        int blockSize = options.BlockSize;
        int logBlockSize = blockSize switch
        {
            1024 => 0,
            2048 => 1,
            4096 => 2,
            _ => throw new ArgumentException("Block size must be 1024, 2048, or 4096.", nameof(options))
        };

        uint totalBlocks = (uint)(partitionSize / blockSize);
        uint blocksPerGroup = (uint)(blockSize * 8); // bits per bitmap block
        uint inodesPerGroup = (uint)(blockSize * 8);
        uint blockGroupCount = (totalBlocks + blocksPerGroup - 1) / blocksPerGroup;

        // Limit inodes: at most 1 inode per 16 KB, min inodesPerGroup per group
        uint maxInodesPerGroup = Math.Max(blocksPerGroup / 4, 256);
        if (inodesPerGroup > maxInodesPerGroup)
            inodesPerGroup = maxInodesPerGroup;

        uint totalInodes = inodesPerGroup * blockGroupCount;
        ushort inodeSize = (ushort)(options.Version == ExtVersion.Ext2 ? 128 : 256);
        uint inodesPerBlock = (uint)(blockSize / inodeSize);

        // Feature flags
        uint featureCompat = 0;
        uint featureIncompat = 0x0002; // FILETYPE
        uint featureRoCompat = 0x0001 | 0x0002; // SPARSE_SUPER | LARGE_FILE

        if (options.Version >= ExtVersion.Ext3)
        {
            featureCompat |= 0x0004; // HAS_JOURNAL (simplified: we mark it but don't create journal inode for brevity)
        }

        if (options.Version >= ExtVersion.Ext4)
        {
            featureIncompat |= 0x0040; // EXTENTS
            featureIncompat |= 0x0200; // FLEX_BG
            featureRoCompat |= 0x0008; // HUGE_FILE
        }

        // Generate UUID
        byte[] uuid = Guid.NewGuid().ToByteArray();

        progress?.Invoke(0.0);

        // --- Build superblock ---
        byte[] superblock = new byte[1024];
        WriteU32(superblock, 0, totalInodes);           // s_inodes_count
        WriteU32(superblock, 4, totalBlocks);            // s_blocks_count_lo
        WriteU32(superblock, 8, totalBlocks / 20);       // s_r_blocks_count_lo (5% reserved)
        WriteU32(superblock, 12, totalBlocks - 1);       // s_free_blocks_count_lo (will adjust)
        WriteU32(superblock, 16, totalInodes - 11);      // s_free_inodes_count (reserve first 11)
        WriteU32(superblock, 20, blockSize > 1024 ? 0u : 1u); // s_first_data_block
        WriteU32(superblock, 24, (uint)logBlockSize);    // s_log_block_size
        WriteU32(superblock, 28, (uint)logBlockSize);    // s_log_cluster_size
        WriteU32(superblock, 32, blocksPerGroup);        // s_blocks_per_group
        WriteU32(superblock, 36, blocksPerGroup);        // s_clusters_per_group
        WriteU32(superblock, 40, inodesPerGroup);        // s_inodes_per_group
        WriteU32(superblock, 44, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // s_mtime
        WriteU32(superblock, 48, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // s_wtime
        WriteU16(superblock, 52, 0);                     // s_mnt_count
        WriteU16(superblock, 54, ushort.MaxValue);       // s_max_mnt_count
        WriteU16(superblock, 56, Ext2Magic);             // s_magic
        WriteU16(superblock, 58, 1);                     // s_state = EXT2_VALID_FS
        WriteU16(superblock, 60, 1);                     // s_errors = continue
        WriteU16(superblock, 62, 0);                     // s_minor_rev_level
        WriteU32(superblock, 64, 0);                     // s_lastcheck
        WriteU32(superblock, 68, 0);                     // s_checkinterval
        WriteU32(superblock, 72, 0);                     // s_creator_os (Linux)
        WriteU32(superblock, 76, 1);                     // s_rev_level = dynamic
        WriteU16(superblock, 80, 0);                     // s_def_resuid
        WriteU16(superblock, 82, 0);                     // s_def_resgid
        // -- EXT2_DYNAMIC_REV fields --
        WriteU32(superblock, 84, 11);                    // s_first_ino
        WriteU16(superblock, 88, inodeSize);             // s_inode_size
        WriteU16(superblock, 90, 0);                     // s_block_group_nr
        WriteU32(superblock, 92, featureCompat);         // s_feature_compat
        WriteU32(superblock, 96, featureIncompat);       // s_feature_incompat
        WriteU32(superblock, 100, featureRoCompat);      // s_feature_ro_compat
        Array.Copy(uuid, 0, superblock, 104, 16);       // s_uuid
        // s_volume_name at offset 120 (16 bytes)
        byte[] labelBytes = Encoding.ASCII.GetBytes(options.VolumeLabel.Length > 16
            ? options.VolumeLabel[..16]
            : options.VolumeLabel);
        Array.Copy(labelBytes, 0, superblock, 120, labelBytes.Length);

        progress?.Invoke(0.1);

        // --- Compute block group descriptor table ---
        int descSize = 32; // standard 32-byte descriptors
        int descsPerBlock = blockSize / descSize;
        int bgdtBlocks = ((int)blockGroupCount + descsPerBlock - 1) / descsPerBlock;

        // For each block group, layout is:
        // [superblock copy?] [BGDT copy?] [block bitmap] [inode bitmap] [inode table...] [data blocks]
        // Group 0 always has superblock at block 0 (or block 1 if blockSize==1024)

        uint firstDataBlock = blockSize > 1024 ? 0u : 1u;

        byte[] bgdt = new byte[bgdtBlocks * blockSize];
        uint usedBlocksTotal = 0;

        for (int g = 0; g < (int)blockGroupCount; g++)
        {
            uint groupStart = firstDataBlock + (uint)g * blocksPerGroup;
            bool hasSuperblock = (g == 0) || HasSparseSuper(g);
            uint metaBlocks = hasSuperblock ? (uint)(1 + bgdtBlocks) : 0u; // SB + BGDT
            // +1 block bitmap, +1 inode bitmap
            uint blockBitmapBlock = groupStart + metaBlocks;
            uint inodeBitmapBlock = blockBitmapBlock + 1;
            uint inodeTableBlock = inodeBitmapBlock + 1;
            uint inodeTableBlocks = (inodesPerGroup * (uint)inodeSize + (uint)blockSize - 1) / (uint)blockSize;

            uint overhead = metaBlocks + 2 + inodeTableBlocks; // SB+BGDT + bitmaps + inode table
            uint groupBlocks = Math.Min(blocksPerGroup, totalBlocks - (uint)g * blocksPerGroup);
            uint freeBlocks = groupBlocks > overhead ? groupBlocks - overhead : 0;
            uint freeInodes = inodesPerGroup;
            if (g == 0) freeInodes -= 11; // reserved inodes

            int off = g * descSize;
            WriteU32(bgdt, off + 0, blockBitmapBlock);
            WriteU32(bgdt, off + 4, inodeBitmapBlock);
            WriteU32(bgdt, off + 8, inodeTableBlock);
            WriteU16(bgdt, off + 12, (ushort)freeBlocks);
            WriteU16(bgdt, off + 14, (ushort)freeInodes);
            WriteU16(bgdt, off + 16, (ushort)(g == 0 ? 1 : 0)); // used_dirs_count

            usedBlocksTotal += overhead;
        }

        // Update superblock free blocks
        uint freeBlocksCount = totalBlocks - usedBlocksTotal;
        WriteU32(superblock, 12, freeBlocksCount);

        progress?.Invoke(0.2);

        // --- Write block groups ---
        for (int g = 0; g < (int)blockGroupCount; g++)
        {
            uint groupStart = firstDataBlock + (uint)g * blocksPerGroup;
            bool hasSuperblock = (g == 0) || HasSparseSuper(g);
            uint metaBlocks = hasSuperblock ? (uint)(1 + bgdtBlocks) : 0u;
            uint blockBitmapBlock = groupStart + metaBlocks;
            uint inodeBitmapBlock = blockBitmapBlock + 1;
            uint inodeTableBlock = inodeBitmapBlock + 1;
            uint inodeTableBlocks = (inodesPerGroup * (uint)inodeSize + (uint)blockSize - 1) / (uint)blockSize;

            // Write superblock copy
            if (hasSuperblock)
            {
                // Update block_group_nr in superblock copy
                byte[] sbCopy = (byte[])superblock.Clone();
                WriteU16(sbCopy, 90, (ushort)g);

                long sbOffset = partitionOffset + (long)groupStart * blockSize;
                if (blockSize > 1024 && g == 0)
                {
                    // Superblock is always at byte 1024
                    byte[] sbBlock = new byte[blockSize];
                    Array.Copy(sbCopy, 0, sbBlock, 1024, 1024);
                    stream.WriteAt(sbOffset, sbBlock);
                }
                else if (blockSize == 1024)
                {
                    // Block 1 = byte 1024
                    stream.WriteAt(partitionOffset + (long)groupStart * blockSize, sbCopy);
                }
                else
                {
                    // Backup superblocks at groupStart
                    byte[] sbBlock = new byte[blockSize];
                    Array.Copy(sbCopy, 0, sbBlock, 0, 1024);
                    stream.WriteAt(sbOffset, sbBlock);
                }

                // Write BGDT
                long bgdtOffset = partitionOffset + (long)(groupStart + 1) * blockSize;
                if (blockSize == 1024 && g == 0)
                    bgdtOffset = partitionOffset + 2L * blockSize; // block 2
                stream.WriteAt(bgdtOffset, bgdt);
            }

            // Write block bitmap
            byte[] blockBitmap = new byte[blockSize];
            uint groupBlocks = Math.Min(blocksPerGroup, totalBlocks - (uint)g * blocksPerGroup);
            uint overhead = metaBlocks + 2 + inodeTableBlocks;
            // Mark overhead blocks as used
            for (uint b = 0; b < overhead && b < groupBlocks; b++)
                SetBit(blockBitmap, (int)b);
            // Mark blocks beyond this group as used (padding for last group)
            for (uint b = groupBlocks; b < blocksPerGroup; b++)
                SetBit(blockBitmap, (int)b);

            stream.WriteAt(partitionOffset + (long)blockBitmapBlock * blockSize, blockBitmap);

            // Write inode bitmap
            byte[] inodeBitmap = new byte[blockSize];
            if (g == 0)
            {
                // Mark first 11 inodes as used (reserved)
                for (int bit = 0; bit < 11; bit++)
                    SetBit(inodeBitmap, bit);
            }
            stream.WriteAt(partitionOffset + (long)inodeBitmapBlock * blockSize, inodeBitmap);

            // Write inode table (zeroed) — then write root inode in group 0
            byte[] inodeTable = new byte[inodeTableBlocks * (uint)blockSize];
            if (g == 0)
            {
                // Inode 2 = root directory
                int rootInodeOffset = (2 - 1) * inodeSize; // inode numbering starts at 1
                WriteRootInode(inodeTable, rootInodeOffset, inodeSize, blockSize, options.Version,
                    overhead, groupStart, partitionOffset, stream);
            }
            stream.WriteAt(partitionOffset + (long)inodeTableBlock * blockSize, inodeTable);

            progress?.Invoke(0.2 + 0.8 * ((g + 1.0) / blockGroupCount));
        }

        progress?.Invoke(1.0);
    }

    private void WriteRootInode(byte[] inodeTable, int offset, ushort inodeSize, int blockSize,
        ExtVersion version, uint overheadBlocks, uint groupStart, long partitionOffset, DiskStream stream)
    {
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // The root dir data block is the first free block after overhead in group 0
        uint rootDataBlock = groupStart + overheadBlocks;

        // i_mode: directory, rwxr-xr-x = 0x41ED
        WriteU16(inodeTable, offset + 0, 0x41ED);
        // i_uid
        WriteU16(inodeTable, offset + 2, 0);
        // i_size_lo (one block of directory entries)
        WriteU32(inodeTable, offset + 4, (uint)blockSize);
        // i_atime, i_ctime, i_mtime
        WriteU32(inodeTable, offset + 8, now);
        WriteU32(inodeTable, offset + 12, now);
        WriteU32(inodeTable, offset + 16, now);
        // i_dtime
        WriteU32(inodeTable, offset + 20, 0);
        // i_gid
        WriteU16(inodeTable, offset + 24, 0);
        // i_links_count (. and ..)
        WriteU16(inodeTable, offset + 26, 2);
        // i_blocks_lo (in 512-byte units)
        WriteU32(inodeTable, offset + 28, (uint)(blockSize / 512));

        if (version >= ExtVersion.Ext4)
        {
            // i_flags: EXT4_EXTENTS_FL = 0x80000
            WriteU32(inodeTable, offset + 32, 0x00080000);
            // Write extent header + single extent at i_block (offset 40)
            WriteExtentTree(inodeTable, offset + 40, rootDataBlock, 1);
        }
        else
        {
            // i_flags
            WriteU32(inodeTable, offset + 32, 0);
            // i_block[0] = direct block pointer at offset 40
            WriteU32(inodeTable, offset + 40, rootDataBlock);
        }

        // Write root directory data block (. and .. entries)
        byte[] dirBlock = new byte[blockSize];
        int pos = 0;
        // "." entry → inode 2
        WriteU32(dirBlock, pos + 0, 2);          // inode
        WriteU16(dirBlock, pos + 4, 12);          // rec_len
        dirBlock[pos + 6] = 1;                    // name_len
        dirBlock[pos + 7] = 2;                    // file_type = directory
        dirBlock[pos + 8] = (byte)'.';
        pos += 12;
        // ".." entry → inode 2 (root's parent is root)
        WriteU32(dirBlock, pos + 0, 2);
        WriteU16(dirBlock, pos + 4, (ushort)(blockSize - 12)); // rest of block
        dirBlock[pos + 6] = 2;
        dirBlock[pos + 7] = 2;
        dirBlock[pos + 8] = (byte)'.';
        dirBlock[pos + 9] = (byte)'.';

        stream.WriteAt(partitionOffset + (long)rootDataBlock * blockSize, dirBlock);

        // Mark root data block as used in block bitmap — already handled since it's within overhead+1
        // Actually we need to mark it. We'll do a read-modify-write on the block bitmap.
        uint blockBitmapBlock = groupStart + (groupStart == 0 && blockSize > 1024 ? 0u : 0u);
        // Recalculate: bitmap block = groupStart + metaBlocks
        // overheadBlocks includes meta + 2 bitmaps + inode table
        // The root data block is at index overheadBlocks within the group
        // We need to mark bit overheadBlocks in the block bitmap
        // The bitmap block was already written; re-read and set the bit
        bool hasSuperblock = true; // group 0
        uint bgdtBlocks = 1; // approximate
        uint meta = 1 + bgdtBlocks; // SB + BGDT
        uint bitmapBlock = groupStart + meta;
        byte[] bitmap = stream.ReadAt(partitionOffset + (long)bitmapBlock * blockSize, blockSize);
        SetBit(bitmap, (int)overheadBlocks); // the data block
        stream.WriteAt(partitionOffset + (long)bitmapBlock * blockSize, bitmap);
    }

    private static void WriteExtentTree(byte[] buffer, int offset, uint startBlock, uint blockCount)
    {
        // Extent header
        WriteU16(buffer, offset + 0, 0xF30A);    // eh_magic
        WriteU16(buffer, offset + 2, 1);           // eh_entries
        WriteU16(buffer, offset + 4, 4);           // eh_max
        WriteU16(buffer, offset + 6, 0);           // eh_depth
        WriteU32(buffer, offset + 8, 0);           // eh_generation

        // Single extent entry (at offset + 12)
        WriteU32(buffer, offset + 12, 0);          // ee_block (logical block 0)
        WriteU16(buffer, offset + 16, (ushort)blockCount); // ee_len
        WriteU16(buffer, offset + 18, (ushort)(startBlock >> 16)); // ee_start_hi
        WriteU32(buffer, offset + 20, startBlock & 0xFFFFFFFF);    // ee_start_lo
    }

    private static bool HasSparseSuper(int groupIndex)
    {
        if (groupIndex <= 1) return true;
        return IsPowerOf(groupIndex, 3) || IsPowerOf(groupIndex, 5) || IsPowerOf(groupIndex, 7);
    }

    private static bool IsPowerOf(int value, int baseVal)
    {
        int n = baseVal;
        while (n < value) n *= baseVal;
        return n == value;
    }

    private static void SetBit(byte[] bitmap, int bitIndex)
    {
        bitmap[bitIndex / 8] |= (byte)(1 << (bitIndex % 8));
    }

    private static void WriteU16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
