using LinuxExplorer.ExtFileSystem.IO;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for <see cref="BitmapHelper"/>.</summary>
public class BitmapHelperTests
{
    [Fact]
    public void IsSet_BitNotSet_ReturnsFalse()
    {
        var bitmap = new byte[] { 0x00, 0x00 };
        Assert.False(BitmapHelper.IsSet(bitmap, 0));
        Assert.False(BitmapHelper.IsSet(bitmap, 7));
        Assert.False(BitmapHelper.IsSet(bitmap, 8));
    }

    [Fact]
    public void IsSet_BitSet_ReturnsTrue()
    {
        var bitmap = new byte[] { 0b00000001, 0b10000000 };
        Assert.True(BitmapHelper.IsSet(bitmap, 0));   // bit 0 of byte 0
        Assert.True(BitmapHelper.IsSet(bitmap, 15));  // bit 7 of byte 1
    }

    [Fact]
    public void Set_SetsCorrectBit()
    {
        var bitmap = new byte[2];
        BitmapHelper.Set(bitmap, 3);
        Assert.True(BitmapHelper.IsSet(bitmap, 3));
        Assert.False(BitmapHelper.IsSet(bitmap, 2));
        Assert.False(BitmapHelper.IsSet(bitmap, 4));
    }

    [Fact]
    public void Clear_ClearsCorrectBit()
    {
        var bitmap = new byte[] { 0xFF, 0xFF };
        BitmapHelper.Clear(bitmap, 5);
        Assert.False(BitmapHelper.IsSet(bitmap, 5));
        Assert.True(BitmapHelper.IsSet(bitmap, 4));
        Assert.True(BitmapHelper.IsSet(bitmap, 6));
    }

    [Fact]
    public void CountFree_AllFree_ReturnsTotal()
    {
        var bitmap = new byte[4]; // 32 bits, all zero
        Assert.Equal(32, BitmapHelper.CountFree(bitmap, 32));
    }

    [Fact]
    public void CountFree_AllUsed_ReturnsZero()
    {
        var bitmap = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Equal(0, BitmapHelper.CountFree(bitmap, 32));
    }

    [Fact]
    public void CountFree_Mixed_ReturnsCorrectCount()
    {
        var bitmap = new byte[] { 0b10101010 }; // 4 free, 4 used
        Assert.Equal(4, BitmapHelper.CountFree(bitmap, 8));
    }

    [Fact]
    public void FindFirstFree_AllUsed_ReturnsMinusOne()
    {
        var bitmap = new byte[] { 0xFF, 0xFF };
        Assert.Equal(-1, BitmapHelper.FindFirstFree(bitmap, 16));
    }

    [Fact]
    public void FindFirstFree_FirstFreeAtBit5()
    {
        // Bits 0-4 set, bit 5 free
        var bitmap = new byte[] { 0b00011111 };
        Assert.Equal(5, BitmapHelper.FindFirstFree(bitmap, 8));
    }

    [Fact]
    public void FindFirstFree_FirstBitFree_ReturnsZero()
    {
        var bitmap = new byte[] { 0b11111110 };
        Assert.Equal(0, BitmapHelper.FindFirstFree(bitmap, 8));
    }

    [Fact]
    public void IsSet_OutOfBounds_ReturnsFalse()
    {
        var bitmap = new byte[1];
        Assert.False(BitmapHelper.IsSet(bitmap, 100));
    }

    [Fact]
    public void SetAndClear_RoundTrip_Works()
    {
        var bitmap = new byte[4];
        for (int i = 0; i < 32; i++)
            BitmapHelper.Set(bitmap, i);

        Assert.Equal(0, BitmapHelper.CountFree(bitmap, 32));

        for (int i = 0; i < 32; i++)
            BitmapHelper.Clear(bitmap, i);

        Assert.Equal(32, BitmapHelper.CountFree(bitmap, 32));
    }
}
