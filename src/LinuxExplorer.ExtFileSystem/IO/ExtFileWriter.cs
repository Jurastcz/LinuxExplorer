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
        if (_superblock.HasExtents)
            throw new NotSupportedException("Writing to ext4 filesystems with extents is not yet supported. Use ext2/ext3 (non-extent) formatted partitions.");

        int blockSize = _superblock.BlockSize;
        int blocksNeeded = (data.Length + blockSize - 1) / blockSize;
        if (blocksNeeded > 12)
            throw new NotSupportedException("Files requiring indirect blocks are not supported in this version.");

        // Allocate inode
        uint newInodeNo = _inodeAllocator.AllocateInode();

        // Allocate data blocks
        List<uint> blocks = blocksNeeded > 0 ? _blockAllocator.AllocateBlocks(blocksNeeded) : [];

        // Write data to blocks
        for (int i = 0; i < blocks.Count; i++)
        {
            int srcOffset = i * blockSize;
            int toCopy = Math.Min(blockSize, data.Length - srcOffset);
            var blockData = new byte[blockSize];
            Buffer.BlockCopy(data, srcOffset, blockData, 0, toCopy);
            WriteBlock(blocks[i], blockData);
        }

        // Build and write inode
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] inodeData = BuildInodeData(permissions, (uint)data.Length, blocks, now);
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
        if (_superblock.HasExtents)
            throw new NotSupportedException("Writing to ext4 filesystems with extents is not yet supported.");

        int blockSize = _superblock.BlockSize;
        uint newInodeNo = _inodeAllocator.AllocateInode();
        List<uint> blocks = _blockAllocator.AllocateBlocks(1);

        // Build initial directory block with . and .. entries
        byte[] dirBlock = BuildInitialDirectoryBlock(newInodeNo, parentInodeNo, blockSize);
        WriteBlock(blocks[0], dirBlock);

        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] inodeData = BuildInodeData(permissions, (uint)blockSize, blocks, now);
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

    private static byte[] BuildInodeData(ushort mode, uint size, List<uint> blocks, uint timestamp)
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
        uint blockCount512 = (uint)(blocks.Count * (128 << (int)0)); // Will be computed per superblock in real impl
        WriteUInt32(data, 28, blockCount512);
        // i_flags = 0
        WriteUInt32(data, 32, 0);
        // i_block[15]
        for (int i = 0; i < Math.Min(blocks.Count, 12); i++)
            WriteUInt32(data, 40 + i * 4, blocks[i]);

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

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
