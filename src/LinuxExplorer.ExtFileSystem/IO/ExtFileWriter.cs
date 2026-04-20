using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ExtFileSystem.IO;

/// <summary>
/// Writes files and directories to an ext2/3/4 filesystem.
/// Handles inode allocation, block allocation, directory entry creation, and data writing.
/// </summary>
public sealed class ExtFileWriter
{
    private readonly DiskStream _stream;
    private readonly Superblock _superblock;
    private readonly BlockGroupDescriptor[] _groupDescriptors;
    private readonly long _partitionOffset;
    private readonly BlockAllocator _blockAllocator;
    private readonly InodeAllocator _inodeAllocator;
    private readonly ExtFileReader _reader;

    /// <summary>Initializes a new <see cref="ExtFileWriter"/>.</summary>
    public ExtFileWriter(
        DiskStream stream,
        Superblock superblock,
        BlockGroupDescriptor[] groupDescriptors,
        long partitionOffset)
    {
        _stream = stream;
        _superblock = superblock;
        _groupDescriptors = groupDescriptors;
        _partitionOffset = partitionOffset;
        _blockAllocator = new BlockAllocator(stream, superblock, groupDescriptors, partitionOffset);
        _inodeAllocator = new InodeAllocator(stream, superblock, groupDescriptors, partitionOffset);
        _reader = new ExtFileReader(stream, superblock, partitionOffset);
    }

    /// <summary>
    /// Writes a new file with the given name and content to the parent directory.
    /// </summary>
    /// <param name="parentInodeNo">Inode number of the parent directory.</param>
    /// <param name="parentInode">Parsed inode of the parent directory.</param>
    /// <param name="name">File name.</param>
    /// <param name="data">File content.</param>
    /// <param name="permissions">Unix permissions (default: 0644).</param>
    /// <returns>The new inode number.</returns>
    public uint WriteFile(uint parentInodeNo, Inode parentInode, string name, byte[] data, ushort permissions = 0x81A4)
    {
        int blockSize = _superblock.BlockSize;
        int blocksNeeded = (data.Length + blockSize - 1) / blockSize;

        // Allocate inode
        uint newInodeNo = _inodeAllocator.AllocateInode();

        // Allocate data blocks
        List<uint> dataBlocks = blocksNeeded > 0 ? _blockAllocator.AllocateBlocks(blocksNeeded) : [];

        // Write data to blocks
        for (int i = 0; i < dataBlocks.Count; i++)
        {
            int srcOffset = i * blockSize;
            int toCopy = Math.Min(blockSize, data.Length - srcOffset);
            var blockData = new byte[blockSize];
            Buffer.BlockCopy(data, srcOffset, blockData, 0, toCopy);
            WriteBlock(dataBlocks[i], blockData);
        }

        // Build and write inode
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] inodeData;

        if (_superblock.HasExtents)
        {
            uint blocks512 = (uint)(blocksNeeded * (blockSize / 512));
            inodeData = BuildInodeDataWithExtents(permissions, (uint)data.Length, dataBlocks, now, blocks512);
        }
        else
        {
            int ptrsPerBlock = blockSize / 4;
            long maxBlocks = 12L + ptrsPerBlock + (long)ptrsPerBlock * ptrsPerBlock
                             + (long)ptrsPerBlock * ptrsPerBlock * ptrsPerBlock;
            if (blocksNeeded > maxBlocks)
                throw new NotSupportedException("File is too large for the filesystem.");

            uint[] iblock = BuildBlockPointers(dataBlocks, ptrsPerBlock);
            int totalAllocated512 = CountTotalAllocatedBlocks(blocksNeeded, ptrsPerBlock) * (blockSize / 512);
            inodeData = BuildInodeData(permissions, (uint)data.Length, iblock, now, (uint)totalAllocated512);
        }

        WriteInode(newInodeNo, inodeData);

        // Add directory entry in parent
        AddDirectoryEntry(parentInodeNo, parentInode, name, newInodeNo, FileType.RegularFile);

        return newInodeNo;
    }

    /// <summary>
    /// Creates a new directory within the parent directory.
    /// </summary>
    /// <param name="parentInodeNo">Inode number of the parent directory.</param>
    /// <param name="parentInode">Parsed inode of the parent directory.</param>
    /// <param name="name">Directory name.</param>
    /// <param name="permissions">Unix permissions (default: 0755).</param>
    /// <returns>The new directory inode number.</returns>
    public uint CreateDirectory(uint parentInodeNo, Inode parentInode, string name, ushort permissions = 0x41ED)
    {
        int blockSize = _superblock.BlockSize;
        uint newInodeNo = _inodeAllocator.AllocateInode();
        List<uint> blocks = _blockAllocator.AllocateBlocks(1);

        // Build initial directory block with . and .. entries
        byte[] dirBlock = BuildInitialDirectoryBlock(newInodeNo, parentInodeNo, blockSize);
        WriteBlock(blocks[0], dirBlock);

        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] inodeData;

        if (_superblock.HasExtents)
        {
            inodeData = BuildInodeDataWithExtents(permissions, (uint)blockSize, blocks, now, (uint)(blockSize / 512));
        }
        else
        {
            uint[] dirIblock = new uint[15];
            dirIblock[0] = blocks[0];
            inodeData = BuildInodeData(permissions, (uint)blockSize, dirIblock, now, (uint)(blockSize / 512));
        }

        WriteInode(newInodeNo, inodeData);

        // Add entry in parent directory
        AddDirectoryEntry(parentInodeNo, parentInode, name, newInodeNo, FileType.Directory);

        return newInodeNo;
    }

    private void AddDirectoryEntry(uint parentInodeNo, Inode parentInode, string name, uint inodeNo, FileType fileType)
    {
        byte[] dirData = _reader.ReadDirectoryBlocks(parentInode);

        byte nameLen = (byte)System.Text.Encoding.UTF8.GetByteCount(name);
        int minEntrySize = 8 + nameLen;
        int alignedEntrySize = (minEntrySize + 3) & ~3;

        // Find space in existing directory blocks
        int offset = 0;
        while (offset + DirectoryEntry.MinEntrySize <= dirData.Length)
        {
            ushort recLen = BitConverter.ToUInt16(dirData, offset + 4);
            byte existingNameLen = dirData[offset + 6];
            uint existingInode = BitConverter.ToUInt32(dirData, offset);

            int actualSize = existingInode == 0 ? 0 : (8 + ((existingNameLen + 3) & ~3));
            int freeSpace = recLen - actualSize;

            if (freeSpace >= alignedEntrySize)
            {
                // Shrink existing entry and insert new one
                if (existingInode != 0)
                {
                    // Shrink rec_len of existing entry
                    ushort newRecLen = (ushort)((8 + existingNameLen + 3) & ~3);
                    dirData[offset + 4] = (byte)(newRecLen & 0xFF);
                    dirData[offset + 5] = (byte)(newRecLen >> 8);
                    offset += newRecLen;
                    recLen = (ushort)(recLen - newRecLen);
                }

                // Write new entry
                WriteDirectoryEntryToBuffer(dirData, offset, inodeNo, (ushort)recLen, nameLen, fileType, name);
                break;
            }

            if (recLen == 0) break;
            offset += recLen;
        }

        // Write back the modified directory data
        WriteDirectoryBlocksBack(parentInode, dirData, parentInodeNo);
    }

    private static void WriteDirectoryEntryToBuffer(byte[] buf, int offset, uint inodeNo, ushort recLen, byte nameLen, FileType fileType, string name)
    {
        // inode
        buf[offset + 0] = (byte)(inodeNo & 0xFF);
        buf[offset + 1] = (byte)((inodeNo >> 8) & 0xFF);
        buf[offset + 2] = (byte)((inodeNo >> 16) & 0xFF);
        buf[offset + 3] = (byte)((inodeNo >> 24) & 0xFF);
        // rec_len
        buf[offset + 4] = (byte)(recLen & 0xFF);
        buf[offset + 5] = (byte)(recLen >> 8);
        // name_len
        buf[offset + 6] = nameLen;
        // file_type
        buf[offset + 7] = (byte)fileType;
        // name
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        Buffer.BlockCopy(nameBytes, 0, buf, offset + 8, Math.Min(nameBytes.Length, nameLen));
    }

    private void WriteDirectoryBlocksBack(Inode dirInode, byte[] data, uint inodeNo)
    {
        int blockSize = _superblock.BlockSize;

        if (dirInode.UsesExtents)
        {
            // Resolve physical blocks from the extent tree
            var physBlocks = ResolvePhysicalBlocks(dirInode);
            for (int i = 0; i < physBlocks.Count && i * blockSize < data.Length; i++)
            {
                int srcOff = i * blockSize;
                int toCopy = Math.Min(blockSize, data.Length - srcOff);
                var blk = new byte[blockSize];
                Buffer.BlockCopy(data, srcOff, blk, 0, toCopy);
                WriteBlock((uint)physBlocks[i], blk);
            }
        }
        else
        {
            for (int i = 0; i < dirInode.Block.Length && i * blockSize < data.Length; i++)
            {
                if (dirInode.Block[i] == 0) break;
                int srcOff = i * blockSize;
                int toCopy = Math.Min(blockSize, data.Length - srcOff);
                var blk = new byte[blockSize];
                Buffer.BlockCopy(data, srcOff, blk, 0, toCopy);
                WriteBlock(dirInode.Block[i], blk);
            }
        }
    }

    private static byte[] BuildInitialDirectoryBlock(uint selfInodeNo, uint parentInodeNo, int blockSize)
    {
        var data = new byte[blockSize];
        // Entry "." : inode=self, rec_len=12, name_len=1, type=DIR, name="."
        data[0] = (byte)(selfInodeNo & 0xFF);
        data[1] = (byte)((selfInodeNo >> 8) & 0xFF);
        data[2] = (byte)((selfInodeNo >> 16) & 0xFF);
        data[3] = (byte)((selfInodeNo >> 24) & 0xFF);
        data[4] = 12; data[5] = 0;      // rec_len = 12
        data[6] = 1;                     // name_len
        data[7] = (byte)FileType.Directory;
        data[8] = (byte)'.';

        // Entry ".." : inode=parent, rec_len=block_size-12, name_len=2, type=DIR, name=".."
        int off2 = 12;
        ushort recLen2 = (ushort)(blockSize - 12);
        data[off2 + 0] = (byte)(parentInodeNo & 0xFF);
        data[off2 + 1] = (byte)((parentInodeNo >> 8) & 0xFF);
        data[off2 + 2] = (byte)((parentInodeNo >> 16) & 0xFF);
        data[off2 + 3] = (byte)((parentInodeNo >> 24) & 0xFF);
        data[off2 + 4] = (byte)(recLen2 & 0xFF);
        data[off2 + 5] = (byte)(recLen2 >> 8);
        data[off2 + 6] = 2;               // name_len
        data[off2 + 7] = (byte)FileType.Directory;
        data[off2 + 8] = (byte)'.';
        data[off2 + 9] = (byte)'.';
        return data;
    }

    private static byte[] BuildInodeData(ushort mode, uint size, uint[] iblock, uint timestamp, uint blocks512)
    {
        var data = new byte[128];
        // i_mode
        data[0] = (byte)(mode & 0xFF);
        data[1] = (byte)(mode >> 8);
        // i_uid lo
        data[2] = 0; data[3] = 0;
        // i_size
        data[4] = (byte)(size & 0xFF);
        data[5] = (byte)((size >> 8) & 0xFF);
        data[6] = (byte)((size >> 16) & 0xFF);
        data[7] = (byte)((size >> 24) & 0xFF);
        // i_atime, i_ctime, i_mtime, i_dtime
        WriteUInt32(data, 8, timestamp);
        WriteUInt32(data, 12, timestamp);
        WriteUInt32(data, 16, timestamp);
        WriteUInt32(data, 20, 0);
        // i_gid lo
        data[24] = 0; data[25] = 0;
        // i_links_count = 1
        data[26] = 1; data[27] = 0;
        // i_blocks (in 512-byte sectors)
        WriteUInt32(data, 28, blocks512);
        // i_flags = 0
        WriteUInt32(data, 32, 0);
        // i_block[15]
        for (int i = 0; i < Math.Min(iblock.Length, 15); i++)
            WriteUInt32(data, 40 + i * 4, iblock[i]);

        return data;
    }

    private void WriteBlock(uint blockNo, byte[] data)
    {
        long offset = _partitionOffset + (long)blockNo * _superblock.BlockSize;
        _stream.WriteAt(offset, data);
    }

    private void WriteInode(uint inodeNo, byte[] inodeData)
    {
        // Determine which block group
        uint groupIdx = (inodeNo - 1) / _superblock.InodesPerGroup;
        uint localIdx = (inodeNo - 1) % _superblock.InodesPerGroup;
        var bgd = _groupDescriptors[groupIdx];
        long inodeTableOffset = _partitionOffset + (long)bgd.InodeTable * _superblock.BlockSize;
        long inodeOffset = inodeTableOffset + localIdx * _superblock.InodeSize;
        _stream.WriteAt(inodeOffset, inodeData);
    }

    /// <summary>
    /// Builds the 15-entry i_block array, writing indirect blocks to disk as needed.
    /// i_block[0..11] = direct, [12] = single indirect, [13] = double indirect, [14] = triple indirect.
    /// </summary>
    private uint[] BuildBlockPointers(List<uint> dataBlocks, int ptrsPerBlock)
    {
        uint[] iblock = new uint[15];
        int idx = 0;
        int total = dataBlocks.Count;

        // Direct blocks (0..11)
        for (int i = 0; i < 12 && idx < total; i++, idx++)
            iblock[i] = dataBlocks[idx];

        if (idx >= total) return iblock;

        // Single indirect block
        int singleCount = Math.Min(total - idx, ptrsPerBlock);
        iblock[12] = WriteIndirectBlock(dataBlocks, idx, singleCount, ptrsPerBlock);
        idx += singleCount;

        if (idx >= total) return iblock;

        // Double indirect block
        int doubleMax = ptrsPerBlock * ptrsPerBlock;
        int doubleCount = Math.Min(total - idx, doubleMax);
        iblock[13] = WriteDoubleIndirectBlock(dataBlocks, idx, doubleCount, ptrsPerBlock);
        idx += doubleCount;

        if (idx >= total) return iblock;

        // Triple indirect block
        int tripleMax = ptrsPerBlock * ptrsPerBlock * ptrsPerBlock;
        int tripleCount = Math.Min(total - idx, tripleMax);
        iblock[14] = WriteTripleIndirectBlock(dataBlocks, idx, tripleCount, ptrsPerBlock);

        return iblock;
    }

    private uint WriteIndirectBlock(List<uint> dataBlocks, int startIdx, int count, int ptrsPerBlock)
    {
        uint indirectBlockNo = _blockAllocator.AllocateBlocks(1)[0];
        var buf = new byte[_superblock.BlockSize];
        for (int i = 0; i < count; i++)
            WriteUInt32(buf, i * 4, dataBlocks[startIdx + i]);
        WriteBlock(indirectBlockNo, buf);
        return indirectBlockNo;
    }

    private uint WriteDoubleIndirectBlock(List<uint> dataBlocks, int startIdx, int count, int ptrsPerBlock)
    {
        uint dblBlockNo = _blockAllocator.AllocateBlocks(1)[0];
        var buf = new byte[_superblock.BlockSize];
        int remaining = count;
        int offset = startIdx;

        for (int i = 0; i < ptrsPerBlock && remaining > 0; i++)
        {
            int chunk = Math.Min(remaining, ptrsPerBlock);
            uint singleNo = WriteIndirectBlock(dataBlocks, offset, chunk, ptrsPerBlock);
            WriteUInt32(buf, i * 4, singleNo);
            offset += chunk;
            remaining -= chunk;
        }

        WriteBlock(dblBlockNo, buf);
        return dblBlockNo;
    }

    private uint WriteTripleIndirectBlock(List<uint> dataBlocks, int startIdx, int count, int ptrsPerBlock)
    {
        uint triBlockNo = _blockAllocator.AllocateBlocks(1)[0];
        var buf = new byte[_superblock.BlockSize];
        int remaining = count;
        int offset = startIdx;
        int doubleMax = ptrsPerBlock * ptrsPerBlock;

        for (int i = 0; i < ptrsPerBlock && remaining > 0; i++)
        {
            int chunk = Math.Min(remaining, doubleMax);
            uint dblNo = WriteDoubleIndirectBlock(dataBlocks, offset, chunk, ptrsPerBlock);
            WriteUInt32(buf, i * 4, dblNo);
            offset += chunk;
            remaining -= chunk;
        }

        WriteBlock(triBlockNo, buf);
        return triBlockNo;
    }

    /// <summary>Counts total allocated blocks including indirect metadata blocks.</summary>
    private static int CountTotalAllocatedBlocks(int dataBlocks, int ptrsPerBlock)
    {
        int total = dataBlocks;
        int remaining = dataBlocks - 12;
        if (remaining <= 0) return total;

        // Single indirect
        int singleCount = Math.Min(remaining, ptrsPerBlock);
        total += 1; // the indirect block itself
        remaining -= singleCount;
        if (remaining <= 0) return total;

        // Double indirect
        int doubleMax = ptrsPerBlock * ptrsPerBlock;
        int doubleCount = Math.Min(remaining, doubleMax);
        int singleBlocks = (doubleCount + ptrsPerBlock - 1) / ptrsPerBlock;
        total += 1 + singleBlocks; // double indirect block + single indirect blocks
        remaining -= doubleCount;
        if (remaining <= 0) return total;

        // Triple indirect
        int tripleCount = remaining;
        int dblBlocks = (tripleCount + doubleMax - 1) / doubleMax;
        int singleInTriple = (tripleCount + ptrsPerBlock - 1) / ptrsPerBlock;
        total += 1 + dblBlocks + singleInTriple;

        return total;
    }

    /// <summary>
    /// Builds inode data with an ext4 extent tree in the i_block area.
    /// For most files, all extents fit in the root node (up to 4 extents, depth=0).
    /// </summary>
    private byte[] BuildInodeDataWithExtents(ushort mode, uint size, List<uint> dataBlocks, uint timestamp, uint blocks512)
    {
        // Build extent runs (merge contiguous blocks)
        var extents = BuildExtentRuns(dataBlocks);

        const int maxRootExtents = 4; // (60 - 12) / 12
        if (extents.Count > maxRootExtents)
            throw new NotSupportedException(
                $"File requires {extents.Count} extents but only {maxRootExtents} fit in the inode root. " +
                "Multi-level extent trees for writing are not yet supported.");

        var data = new byte[128];
        // i_mode
        data[0] = (byte)(mode & 0xFF);
        data[1] = (byte)(mode >> 8);
        // i_uid lo
        data[2] = 0; data[3] = 0;
        // i_size
        WriteUInt32(data, 4, size);
        // i_atime, i_ctime, i_mtime, i_dtime
        WriteUInt32(data, 8, timestamp);
        WriteUInt32(data, 12, timestamp);
        WriteUInt32(data, 16, timestamp);
        WriteUInt32(data, 20, 0);
        // i_gid lo
        data[24] = 0; data[25] = 0;
        // i_links_count = 1
        data[26] = 1; data[27] = 0;
        // i_blocks (in 512-byte sectors)
        WriteUInt32(data, 28, blocks512);
        // i_flags = EXT4_EXTENTS_FL
        WriteUInt32(data, 32, Inode.ExtentFlag);

        // i_block area (60 bytes at offset 40): extent tree root
        // Extent header (12 bytes)
        WriteUInt16(data, 40, ExtentHeader.ExtentMagic);        // eh_magic
        WriteUInt16(data, 42, (ushort)extents.Count);           // eh_entries
        WriteUInt16(data, 44, maxRootExtents);                  // eh_max
        WriteUInt16(data, 46, 0);                               // eh_depth = 0 (leaf)
        WriteUInt32(data, 48, 0);                               // eh_generation

        // Extent entries (12 bytes each, starting at offset 52)
        for (int i = 0; i < extents.Count; i++)
        {
            int off = 52 + i * 12;
            var (logicalBlock, length, physBlock) = extents[i];
            WriteUInt32(data, off + 0, logicalBlock);           // ee_block
            WriteUInt16(data, off + 4, (ushort)length);         // ee_len
            WriteUInt16(data, off + 6, (ushort)(physBlock >> 32)); // ee_start_hi
            WriteUInt32(data, off + 8, (uint)(physBlock & 0xFFFFFFFF)); // ee_start_lo
        }

        return data;
    }

    /// <summary>
    /// Merges contiguous allocated blocks into extent runs.
    /// Returns list of (logicalBlock, length, physicalStartBlock).
    /// </summary>
    private static List<(uint logicalBlock, int length, ulong physBlock)> BuildExtentRuns(List<uint> blocks)
    {
        var extents = new List<(uint logicalBlock, int length, ulong physBlock)>();
        if (blocks.Count == 0) return extents;

        uint runStart = blocks[0];
        int runLen = 1;
        uint logicalStart = 0;

        for (int i = 1; i < blocks.Count; i++)
        {
            // ext4 extent max length is 32768 (15 bits)
            if (blocks[i] == runStart + (uint)runLen && runLen < 32768)
            {
                runLen++;
            }
            else
            {
                extents.Add((logicalStart, runLen, runStart));
                logicalStart = (uint)i;
                runStart = blocks[i];
                runLen = 1;
            }
        }
        extents.Add((logicalStart, runLen, runStart));
        return extents;
    }

    /// <summary>
    /// Resolves all physical block numbers from an inode's extent tree (in logical order).
    /// </summary>
    private List<long> ResolvePhysicalBlocks(Inode inode)
    {
        var blocks = new List<long>();
        ResolveExtentNode(inode.BlockRaw, 0, blocks);
        return blocks;
    }

    private void ResolveExtentNode(byte[] nodeData, int offset, List<long> blocks)
    {
        var header = ExtentHeader.Parse(nodeData, offset);
        if (!header.IsValid) return;

        int entriesOffset = offset + ExtentHeader.Size;

        if (header.Depth == 0)
        {
            for (int i = 0; i < header.Entries; i++)
            {
                var extent = Extent.Parse(nodeData, entriesOffset + i * Extent.Size);
                for (int b = 0; b < extent.Length; b++)
                    blocks.Add((long)(extent.Start + (ulong)b));
            }
        }
        else
        {
            for (int i = 0; i < header.Entries; i++)
            {
                var idx = ExtentIndex.Parse(nodeData, entriesOffset + i * ExtentIndex.Size);
                long physBlock = (long)idx.Leaf;
                long blockOffset = _partitionOffset + physBlock * _superblock.BlockSize;
                byte[] childData = new byte[_superblock.BlockSize];
                Buffer.BlockCopy(_stream.ReadAt(blockOffset, _superblock.BlockSize), 0, childData, 0, _superblock.BlockSize);
                ResolveExtentNode(childData, 0, blocks);
            }
        }
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
