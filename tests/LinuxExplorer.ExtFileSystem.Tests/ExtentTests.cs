using LinuxExplorer.ExtFileSystem.Ext;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for ext4 extent tree structures (<see cref="ExtentHeader"/>, <see cref="ExtentIndex"/>, <see cref="Extent"/>).</summary>
public class ExtentTests
{
    private static byte[] BuildExtentHeader(ushort magic, ushort entries, ushort max, ushort depth, uint generation = 0)
    {
        var data = new byte[ExtentHeader.Size];
        void WriteU16(int off, ushort v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)(v >> 8); }
        void WriteU32(int off, uint v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)((v >> 8) & 0xFF); data[off + 2] = (byte)((v >> 16) & 0xFF); data[off + 3] = (byte)((v >> 24) & 0xFF); }

        WriteU16(0, magic);
        WriteU16(2, entries);
        WriteU16(4, max);
        WriteU16(6, depth);
        WriteU32(8, generation);
        return data;
    }

    private static byte[] BuildExtentLeaf(uint logicalBlock, ushort len, ushort startHi, uint startLo)
    {
        var data = new byte[Extent.Size];
        void WriteU16(int off, ushort v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)(v >> 8); }
        void WriteU32(int off, uint v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)((v >> 8) & 0xFF); data[off + 2] = (byte)((v >> 16) & 0xFF); data[off + 3] = (byte)((v >> 24) & 0xFF); }

        WriteU32(0, logicalBlock);
        WriteU16(4, len);
        WriteU16(6, startHi);
        WriteU32(8, startLo);
        return data;
    }

    private static byte[] BuildExtentIndex(uint logicalBlock, uint leafLo, ushort leafHi)
    {
        var data = new byte[ExtentIndex.Size];
        void WriteU16(int off, ushort v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)(v >> 8); }
        void WriteU32(int off, uint v) { data[off] = (byte)(v & 0xFF); data[off + 1] = (byte)((v >> 8) & 0xFF); data[off + 2] = (byte)((v >> 16) & 0xFF); data[off + 3] = (byte)((v >> 24) & 0xFF); }

        WriteU32(0, logicalBlock);
        WriteU32(4, leafLo);
        WriteU16(8, leafHi);
        return data;
    }

    [Fact]
    public void ExtentHeader_Parse_ValidHeader()
    {
        var data = BuildExtentHeader(ExtentHeader.ExtentMagic, 3, 4, 0);
        var header = ExtentHeader.Parse(data, 0);

        Assert.True(header.IsValid);
        Assert.Equal(ExtentHeader.ExtentMagic, header.Magic);
        Assert.Equal(3, header.Entries);
        Assert.Equal(4, header.Max);
        Assert.Equal(0, header.Depth);
    }

    [Fact]
    public void ExtentHeader_Parse_InvalidMagic_IsNotValid()
    {
        var data = BuildExtentHeader(0xDEAD, 1, 4, 0);
        var header = ExtentHeader.Parse(data, 0);
        Assert.False(header.IsValid);
    }

    [Fact]
    public void ExtentHeader_Parse_DepthNonZero_IsInternalNode()
    {
        var data = BuildExtentHeader(ExtentHeader.ExtentMagic, 2, 4, 3);
        var header = ExtentHeader.Parse(data, 0);

        Assert.Equal(3, header.Depth);
    }

    [Fact]
    public void Extent_Parse_LeafEntry_CorrectValues()
    {
        // Logical block 0, length 8, physical start at block 1000
        var data = BuildExtentLeaf(0, 8, 0, 1000);
        var extent = Extent.Parse(data, 0);

        Assert.Equal(0u, extent.Block);
        Assert.Equal(8, extent.Length);
        Assert.Equal(1000uL, extent.Start);
        Assert.False(extent.IsUninitialized);
    }

    [Fact]
    public void Extent_Parse_UninitializedExtent_DetectedCorrectly()
    {
        // Set high bit of len to mark as uninitialized
        var data = BuildExtentLeaf(0, 0x8008, 0, 500);
        var extent = Extent.Parse(data, 0);

        Assert.True(extent.IsUninitialized);
        Assert.Equal(8, extent.Length); // Length without high bit
    }

    [Fact]
    public void Extent_Parse_HighPhysicalBlock_CalculatedCorrectly()
    {
        // Physical block = (hi << 32) | lo = (1 << 32) | 0 = 4294967296
        var data = BuildExtentLeaf(10, 4, 1, 0);
        var extent = Extent.Parse(data, 0);

        Assert.Equal(4294967296uL, extent.Start);
    }

    [Fact]
    public void ExtentIndex_Parse_IndexEntry_CorrectValues()
    {
        var data = BuildExtentIndex(0, 5000, 0);
        var idx = ExtentIndex.Parse(data, 0);

        Assert.Equal(0u, idx.Block);
        Assert.Equal(5000uL, idx.Leaf);
    }

    [Fact]
    public void ExtentIndex_Parse_HighLeafBlock_CalculatedCorrectly()
    {
        // Leaf = (2 << 32) | 100 = 8589934692
        var data = BuildExtentIndex(0, 100, 2);
        var idx = ExtentIndex.Parse(data, 0);

        Assert.Equal((2uL << 32) | 100uL, idx.Leaf);
    }
}
