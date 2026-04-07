using LinuxExplorer.ExtFileSystem.Ext;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for <see cref="Superblock"/> parsing.</summary>
public class SuperblockTests
{
    /// <summary>Creates a minimal valid superblock byte array with ext4 characteristics.</summary>
    private static byte[] CreateValidSuperblock(
        uint inodesCount = 65536,
        uint blocksCount = 262144,
        uint freeBlocks = 200000,
        uint freeInodes = 60000,
        uint logBlockSize = 2,       // 4096 bytes
        uint blocksPerGroup = 32768,
        uint inodesPerGroup = 8192,
        ushort inodeSize = 256,
        uint featureIncompat = 0x0040 | 0x0002, // EXTENTS | FILETYPE
        uint featureCompat = 0x0004              // HAS_JOURNAL
    )
    {
        var data = new byte[1024];

        void WriteU32(int offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
        void WriteU16(int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)(value >> 8);
        }

        WriteU32(0, inodesCount);
        WriteU32(4, blocksCount);
        WriteU32(12, freeBlocks);
        WriteU32(16, freeInodes);
        WriteU32(20, 0); // first data block
        WriteU32(24, logBlockSize);
        WriteU32(32, blocksPerGroup);
        WriteU32(40, inodesPerGroup);
        WriteU16(56, Superblock.Ext2Magic);
        WriteU16(58, 1); // state = clean
        WriteU16(88, inodeSize);
        WriteU32(92, featureCompat);
        WriteU32(96, featureIncompat);
        WriteU32(100, 0);

        // Volume name "TestVol\0"
        byte[] name = System.Text.Encoding.ASCII.GetBytes("TestVol");
        Buffer.BlockCopy(name, 0, data, 120, name.Length);

        return data;
    }

    [Fact]
    public void Parse_ValidSuperblock_ReturnsParsedFields()
    {
        byte[] data = CreateValidSuperblock();
        var sb = Superblock.Parse(data);

        Assert.True(sb.IsValid);
        Assert.Equal(Superblock.Ext2Magic, sb.Magic);
        Assert.Equal(65536u, sb.InodesCount);
        Assert.Equal(262144u, sb.BlocksCountLo);
        Assert.Equal(200000u, sb.FreeBlocksCountLo);
        Assert.Equal(60000u, sb.FreeInodesCount);
        Assert.Equal(32768u, sb.BlocksPerGroup);
        Assert.Equal(8192u, sb.InodesPerGroup);
        Assert.Equal(256, sb.InodeSize);
        Assert.Equal(4096, sb.BlockSize);
    }

    [Fact]
    public void Parse_InvalidMagic_IsNotValid()
    {
        byte[] data = CreateValidSuperblock();
        data[56] = 0x00; // corrupt magic
        data[57] = 0x00;

        var sb = Superblock.Parse(data);
        Assert.False(sb.IsValid);
    }

    [Fact]
    public void Parse_Ext4Features_DetectedCorrectly()
    {
        byte[] data = CreateValidSuperblock(
            featureIncompat: 0x0040 | 0x0002, // EXTENTS | FILETYPE
            featureCompat: 0x0004              // HAS_JOURNAL
        );
        var sb = Superblock.Parse(data);

        Assert.True(sb.HasExtents, "Should detect ext4 extents.");
        Assert.True(sb.HasFileType, "Should detect file type in dir entries.");
        Assert.Equal("ext4", sb.FilesystemType);
    }

    [Fact]
    public void Parse_Ext3Features_DetectedCorrectly()
    {
        byte[] data = CreateValidSuperblock(
            featureIncompat: 0x0002,  // FILETYPE only
            featureCompat: 0x0004    // HAS_JOURNAL
        );
        var sb = Superblock.Parse(data);

        Assert.False(sb.HasExtents);
        Assert.Equal("ext3", sb.FilesystemType);
    }

    [Fact]
    public void Parse_Ext2Features_DetectedCorrectly()
    {
        byte[] data = CreateValidSuperblock(
            featureIncompat: 0x0002, // FILETYPE only
            featureCompat: 0        // no journal
        );
        var sb = Superblock.Parse(data);

        Assert.False(sb.HasExtents);
        Assert.Equal("ext2", sb.FilesystemType);
    }

    [Fact]
    public void Parse_BlockSize_CalculatedCorrectly()
    {
        // log_block_size = 0 → 1024
        var sb0 = Superblock.Parse(CreateValidSuperblock(logBlockSize: 0));
        Assert.Equal(1024, sb0.BlockSize);

        // log_block_size = 1 → 2048
        var sb1 = Superblock.Parse(CreateValidSuperblock(logBlockSize: 1));
        Assert.Equal(2048, sb1.BlockSize);

        // log_block_size = 2 → 4096
        var sb2 = Superblock.Parse(CreateValidSuperblock(logBlockSize: 2));
        Assert.Equal(4096, sb2.BlockSize);
    }

    [Fact]
    public void Parse_VolumeName_ParsedCorrectly()
    {
        byte[] data = CreateValidSuperblock();
        var sb = Superblock.Parse(data);
        Assert.Equal("TestVol", sb.VolumeName);
    }

    [Fact]
    public void Parse_ShortData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Superblock.Parse(new byte[100]));
    }

    [Fact]
    public void Parse_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Superblock.Parse(null!));
    }
}
