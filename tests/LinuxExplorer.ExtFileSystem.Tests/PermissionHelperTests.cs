using LinuxExplorer.ExtFileSystem.Utils;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for <see cref="PermissionHelper"/>.</summary>
public class PermissionHelperTests
{
    [Theory]
    [InlineData(0x81A4, "-rw-r--r--")] // Regular file 0644
    [InlineData(0x81FF, "-rwxrwxrwx")] // Regular file 0777
    [InlineData(0x8000, "----------")] // Regular file 0000
    [InlineData(0x41ED, "drwxr-xr-x")] // Directory 0755
    [InlineData(0x41C0, "drwx------")] // Directory 0700
    [InlineData(0xA1FF, "lrwxrwxrwx")] // Symlink 0777
    [InlineData(0x61B6, "brw-rw-rw-")] // Block device 0666
    [InlineData(0x21B6, "crw-rw-rw-")] // Char device 0666
    [InlineData(0x11B6, "prw-rw-rw-")] // FIFO 0666
    public void ToPermissionString_ReturnsCorrectString(ushort mode, string expected)
    {
        Assert.Equal(expected, PermissionHelper.ToPermissionString(mode));
    }

    [Theory]
    [InlineData(0x81A4, "0644")]
    [InlineData(0x81FF, "0777")]
    [InlineData(0x41ED, "0755")]
    [InlineData(0x8000, "0000")]
    [InlineData(0x81C0, "0700")]
    public void ToOctalString_ReturnsCorrectOctal(ushort mode, string expected)
    {
        Assert.Equal(expected, PermissionHelper.ToOctalString(mode));
    }

    [Theory]
    [InlineData(0x8000, "Regular File")]
    [InlineData(0x4000, "Directory")]
    [InlineData(0xA000, "Symbolic Link")]
    [InlineData(0x6000, "Block Device")]
    [InlineData(0x2000, "Character Device")]
    [InlineData(0x1000, "Named Pipe")]
    [InlineData(0xC000, "Socket")]
    public void GetFileTypeName_ReturnsCorrectName(ushort mode, string expected)
    {
        Assert.Equal(expected, PermissionHelper.GetFileTypeName(mode));
    }

    [Fact]
    public void ToPermissionString_SetUidBit_ShowsS()
    {
        // S_IFREG | S_ISUID | S_IXUSR | S_IRUSR → -r-S (wait – ISUID with execute = s, without = S)
        ushort mode = 0x8800 | 0x01A4; // S_IFREG | S_ISUID | 0644
        string result = PermissionHelper.ToPermissionString(mode);
        // With ISUID set and no owner execute bit → 'S' at position 3
        Assert.Equal('S', result[3]);
    }

    [Fact]
    public void ToPermissionString_SetUidWithExecute_ShowsLowerS()
    {
        ushort mode = 0x8840 | 0x01A4; // S_IFREG | S_ISUID | S_IXUSR | 0644
        string result = PermissionHelper.ToPermissionString(mode);
        Assert.Equal('s', result[3]);
    }

    [Fact]
    public void ToPermissionString_StickyBit_ShowsT()
    {
        // Directory with sticky bit and other execute
        ushort mode = 0x41ED | 0x0200; // drwxr-xr-t
        string result = PermissionHelper.ToPermissionString(mode);
        Assert.Equal('t', result[9]);
    }
}
