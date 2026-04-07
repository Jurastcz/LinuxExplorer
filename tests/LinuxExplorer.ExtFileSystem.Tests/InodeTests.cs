using LinuxExplorer.ExtFileSystem.Ext;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for <see cref="Inode"/> parsing.</summary>
public class InodeTests
{
    private static byte[] CreateInodeData(
        ushort mode = 0x81A4,   // regular file, 0644
        uint sizeLo = 12345,
        uint atime = 1000000,
        uint ctime = 1000001,
        uint mtime = 1000002,
        uint flags = 0,
        uint[] blocks = null!)
    {
        var data = new byte[256];
        void WriteU16(int off, ushort v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)(v >> 8); }
        void WriteU32(int off, uint v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)((v >> 8) & 0xFF); data[off + 2] = (byte)((v >> 16) & 0xFF); data[off + 3] = (byte)((v >> 24) & 0xFF); }

        WriteU16(0, mode);
        WriteU32(4, sizeLo);
        WriteU32(8, atime);
        WriteU32(12, ctime);
        WriteU32(16, mtime);
        WriteU16(26, 1); // links_count
        WriteU32(32, flags);

        if (blocks != null)
        {
            for (int i = 0; i < Math.Min(blocks.Length, 15); i++)
                WriteU32(40 + i * 4, blocks[i]);
        }

        return data;
    }

    [Fact]
    public void Parse_RegularFile_IsRegularFileTrue()
    {
        var data = CreateInodeData(mode: 0x81A4); // S_IFREG | 0644
        var inode = Inode.Parse(data, 0, 256);

        Assert.True(inode.IsRegularFile);
        Assert.False(inode.IsDirectory);
        Assert.False(inode.IsSymlink);
    }

    [Fact]
    public void Parse_Directory_IsDirectoryTrue()
    {
        var data = CreateInodeData(mode: 0x41ED); // S_IFDIR | 0755
        var inode = Inode.Parse(data, 0, 256);

        Assert.True(inode.IsDirectory);
        Assert.False(inode.IsRegularFile);
    }

    [Fact]
    public void Parse_Symlink_IsSymlinkTrue()
    {
        var data = CreateInodeData(mode: 0xA1FF); // S_IFLNK | 0777
        var inode = Inode.Parse(data, 0, 256);

        Assert.True(inode.IsSymlink);
        Assert.False(inode.IsRegularFile);
    }

    [Fact]
    public void Parse_SizeLo_ParsedCorrectly()
    {
        var data = CreateInodeData(sizeLo: 98765);
        var inode = Inode.Parse(data, 0, 256);
        Assert.Equal(98765L, inode.Size);
    }

    [Fact]
    public void Parse_Timestamps_ParsedCorrectly()
    {
        var data = CreateInodeData(atime: 1609459200, ctime: 1609459300, mtime: 1609459400);
        var inode = Inode.Parse(data, 0, 256);

        Assert.Equal(1609459200u, inode.ATime);
        Assert.Equal(1609459300u, inode.CTime);
        Assert.Equal(1609459400u, inode.MTime);
    }

    [Fact]
    public void Parse_BlockPointers_ParsedCorrectly()
    {
        var blocks = new uint[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 9999, 0, 0 };
        var data = CreateInodeData(blocks: blocks);
        var inode = Inode.Parse(data, 0, 256);

        Assert.Equal(100u, inode.Block[0]);
        Assert.Equal(200u, inode.Block[1]);
        Assert.Equal(1200u, inode.Block[11]);
        Assert.Equal(9999u, inode.Block[12]); // single indirect
    }

    [Fact]
    public void Parse_ExtentFlag_UsesExtentsTrue()
    {
        var data = CreateInodeData(flags: Inode.ExtentFlag);
        var inode = Inode.Parse(data, 0, 256);
        Assert.True(inode.UsesExtents);
    }

    [Fact]
    public void Parse_NoExtentFlag_UsesExtentsFalse()
    {
        var data = CreateInodeData(flags: 0);
        var inode = Inode.Parse(data, 0, 256);
        Assert.False(inode.UsesExtents);
    }

    [Fact]
    public void Parse_Permissions_ExtractedCorrectly()
    {
        // 0x81A4 = S_IFREG | 0644
        var data = CreateInodeData(mode: 0x81A4);
        var inode = Inode.Parse(data, 0, 256);
        Assert.Equal(0x1A4, inode.Permissions); // 0644 = 420 decimal = 0x1A4
    }
}
