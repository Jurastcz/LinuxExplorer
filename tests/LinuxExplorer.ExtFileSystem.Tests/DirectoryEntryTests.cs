using LinuxExplorer.ExtFileSystem.Ext;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>Tests for <see cref="DirectoryEntry"/> parsing.</summary>
public class DirectoryEntryTests
{
    private static byte[] CreateDirectoryEntryData(uint inode, ushort recLen, byte nameLen, FileType fileType, string name)
    {
        var data = new byte[recLen];
        data[0] = (byte)(inode & 0xFF);
        data[1] = (byte)((inode >> 8) & 0xFF);
        data[2] = (byte)((inode >> 16) & 0xFF);
        data[3] = (byte)((inode >> 24) & 0xFF);
        data[4] = (byte)(recLen & 0xFF);
        data[5] = (byte)(recLen >> 8);
        data[6] = nameLen;
        data[7] = (byte)fileType;
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        Buffer.BlockCopy(nameBytes, 0, data, 8, Math.Min(nameBytes.Length, nameLen));
        return data;
    }

    private static byte[] BuildDirectoryBlock(params (uint inode, string name, FileType type, ushort recLen)[] entries)
    {
        var data = new byte[4096];
        int offset = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var (inodeNo, name, type, recLen) = entries[i];
            ushort actualRecLen = recLen == 0 ? (ushort)(8 + ((name.Length + 3) & ~3)) : recLen;
            data[offset] = (byte)(inodeNo & 0xFF);
            data[offset + 1] = (byte)((inodeNo >> 8) & 0xFF);
            data[offset + 2] = (byte)((inodeNo >> 16) & 0xFF);
            data[offset + 3] = (byte)((inodeNo >> 24) & 0xFF);
            data[offset + 4] = (byte)(actualRecLen & 0xFF);
            data[offset + 5] = (byte)(actualRecLen >> 8);
            data[offset + 6] = (byte)name.Length;
            data[offset + 7] = (byte)type;
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            Buffer.BlockCopy(nameBytes, 0, data, offset + 8, nameBytes.Length);
            offset += actualRecLen;
        }
        return data;
    }

    [Fact]
    public void Parse_ValidEntry_ParsesAllFields()
    {
        var data = CreateDirectoryEntryData(42, 20, 5, FileType.RegularFile, "hello");
        var entry = DirectoryEntry.Parse(data, 0);

        Assert.NotNull(entry);
        Assert.Equal(42u, entry!.InodeNumber);
        Assert.Equal(20, entry.RecLen);
        Assert.Equal(5, entry.NameLen);
        Assert.Equal(FileType.RegularFile, entry.FileType);
        Assert.Equal("hello", entry.Name);
        Assert.True(entry.IsValid);
    }

    [Fact]
    public void Parse_DotEntry_IsDot()
    {
        var data = CreateDirectoryEntryData(2, 12, 1, FileType.Directory, ".");
        var entry = DirectoryEntry.Parse(data, 0);

        Assert.NotNull(entry);
        Assert.True(entry!.IsDot);
        Assert.False(entry.IsDotDot);
    }

    [Fact]
    public void Parse_DotDotEntry_IsDotDot()
    {
        var data = CreateDirectoryEntryData(1, 12, 2, FileType.Directory, "..");
        var entry = DirectoryEntry.Parse(data, 0);

        Assert.NotNull(entry);
        Assert.False(entry!.IsDot);
        Assert.True(entry.IsDotDot);
    }

    [Fact]
    public void Parse_ZeroInode_IsNotValid()
    {
        var data = CreateDirectoryEntryData(0, 16, 4, FileType.RegularFile, "test");
        var entry = DirectoryEntry.Parse(data, 0);

        Assert.NotNull(entry);
        Assert.False(entry!.IsValid);
    }

    [Fact]
    public void ParseAll_MultipleEntries_ReturnsAll()
    {
        byte[] data = BuildDirectoryBlock(
            (2, ".", FileType.Directory, 12),
            (1, "..", FileType.Directory, 12),
            (10, "home", FileType.Directory, 0),
            (11, "etc", FileType.Directory, 0)
        );

        var entries = DirectoryEntry.ParseAll(data).ToList();

        Assert.Contains(entries, e => e.Name == ".");
        Assert.Contains(entries, e => e.Name == "..");
        Assert.Contains(entries, e => e.Name == "home");
        Assert.Contains(entries, e => e.Name == "etc");
    }

    [Fact]
    public void ParseAll_EmptyData_ReturnsEmpty()
    {
        var entries = DirectoryEntry.ParseAll(new byte[0]).ToList();
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_ShortData_ReturnsNull()
    {
        var entry = DirectoryEntry.Parse(new byte[4], 0); // Less than MinEntrySize
        Assert.Null(entry);
    }
}
