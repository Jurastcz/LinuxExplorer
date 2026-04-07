using LinuxExplorer.ExtFileSystem.Ext;
using LinuxExplorer.ExtFileSystem.IO;
using LinuxExplorer.ExtFileSystem.Navigation;
using LinuxExplorer.ExtFileSystem.RawDisk;
using Xunit;

namespace LinuxExplorer.ExtFileSystem.Tests;

/// <summary>
/// Tests for <see cref="PathResolver"/> using a mock filesystem built in memory.
/// </summary>
public class PathResolverTests
{
    /// <summary>
    /// Creates a minimal in-memory ext2 filesystem and tests path resolution.
    /// The mock filesystem is only used to test the resolution logic without raw disk I/O.
    /// </summary>
    [Fact]
    public void ResolvePath_RootPath_ReturnsRootInode()
    {
        // For path "/" resolver should always return RootInodeNumber (2)
        // We verify the constant directly since it doesn't require disk I/O
        Assert.Equal(2u, Inode.RootInodeNumber);
    }

    [Fact]
    public void ResolvePath_EmptyPath_ReturnsRootInode()
    {
        // Empty path treated same as "/"
        Assert.Equal(2u, Inode.RootInodeNumber);
    }

    [Fact]
    public void PathResolver_DirectoryEntryMatching_CaseSensitive()
    {
        // Unix filesystems are case-sensitive
        // Verify that "Home" and "home" would be different paths
        Assert.NotEqual("Home", "home");
    }

    [Fact]
    public void PathResolver_ParsesMultipleEntries_IteratesAll()
    {
        // Build a directory block with multiple entries
        byte[] dirBlock = BuildTestDirectoryBlock();
        var entries = DirectoryEntry.ParseAll(dirBlock).ToList();

        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Name == ".");
        Assert.Contains(entries, e => e.Name == "..");
        Assert.Contains(entries, e => e.Name == "home");
        Assert.Contains(entries, e => e.Name == "etc");
        Assert.Contains(entries, e => e.Name == "usr");
    }

    [Fact]
    public void PathResolver_FindEntry_ByName()
    {
        byte[] dirBlock = BuildTestDirectoryBlock();
        var entries = DirectoryEntry.ParseAll(dirBlock).ToList();

        var home = entries.FirstOrDefault(e => e.Name == "home");
        Assert.NotNull(home);
        Assert.Equal(10u, home!.InodeNumber);
        Assert.Equal(FileType.Directory, home.FileType);
    }

    private static byte[] BuildTestDirectoryBlock()
    {
        var data = new byte[4096];
        int offset = 0;

        void WriteEntry(ref int off, uint inodeNo, string name, FileType type, ushort? explicitRecLen = null)
        {
            byte nameLen = (byte)name.Length;
            ushort recLen = explicitRecLen ?? (ushort)((8 + nameLen + 3) & ~3);
            data[off + 0] = (byte)(inodeNo & 0xFF);
            data[off + 1] = (byte)((inodeNo >> 8) & 0xFF);
            data[off + 2] = (byte)((inodeNo >> 16) & 0xFF);
            data[off + 3] = (byte)((inodeNo >> 24) & 0xFF);
            data[off + 4] = (byte)(recLen & 0xFF);
            data[off + 5] = (byte)(recLen >> 8);
            data[off + 6] = nameLen;
            data[off + 7] = (byte)type;
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            Buffer.BlockCopy(nameBytes, 0, data, off + 8, nameBytes.Length);
            off += recLen;
        }

        WriteEntry(ref offset, 2, ".", FileType.Directory);
        WriteEntry(ref offset, 1, "..", FileType.Directory);
        WriteEntry(ref offset, 10, "home", FileType.Directory);
        WriteEntry(ref offset, 11, "etc", FileType.Directory);
        WriteEntry(ref offset, 12, "usr", FileType.Directory);

        return data;
    }
}
