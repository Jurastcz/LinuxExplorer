namespace LinuxExplorer.ExtFileSystem.Ext;

/// <summary>File type constants used in directory entries and inode mode.</summary>
public enum FileType : byte
{
    /// <summary>Unknown file type.</summary>
    Unknown = 0,
    /// <summary>Regular file.</summary>
    RegularFile = 1,
    /// <summary>Directory.</summary>
    Directory = 2,
    /// <summary>Character device.</summary>
    CharDevice = 3,
    /// <summary>Block device.</summary>
    BlockDevice = 4,
    /// <summary>Named pipe (FIFO).</summary>
    Fifo = 5,
    /// <summary>Socket.</summary>
    Socket = 6,
    /// <summary>Symbolic link.</summary>
    Symlink = 7
}

/// <summary>
/// Represents an ext2/3/4 directory entry (ext2_dir_entry_2 with file_type field).
/// </summary>
public sealed class DirectoryEntry
{
    /// <summary>Inode number. 0 if the entry is unused.</summary>
    public uint InodeNumber { get; private set; }

    /// <summary>Record length in bytes (distance to the next directory entry).</summary>
    public ushort RecLen { get; private set; }

    /// <summary>File name length in bytes.</summary>
    public byte NameLen { get; private set; }

    /// <summary>File type (only valid when the filesystem has the filetype feature).</summary>
    public FileType FileType { get; private set; }

    /// <summary>File name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets whether this entry is valid (non-zero inode and non-empty name).</summary>
    public bool IsValid => InodeNumber != 0 && NameLen > 0;

    /// <summary>Gets whether this entry represents the current directory (.).</summary>
    public bool IsDot => Name == ".";

    /// <summary>Gets whether this entry represents the parent directory (..).</summary>
    public bool IsDotDot => Name == "..";

    /// <summary>Minimum directory entry header size in bytes.</summary>
    public const int MinEntrySize = 8;

    /// <summary>
    /// Parses a directory entry from the given data at the specified offset.
    /// </summary>
    /// <param name="data">Raw block data.</param>
    /// <param name="offset">Offset within data.</param>
    /// <returns>A parsed directory entry, or <see langword="null"/> if data is too short.</returns>
    public static DirectoryEntry? Parse(byte[] data, int offset)
    {
        if (offset + MinEntrySize > data.Length) return null;

        var entry = new DirectoryEntry();
        entry.InodeNumber = BitConverter.ToUInt32(data, offset + 0);
        entry.RecLen = BitConverter.ToUInt16(data, offset + 4);
        entry.NameLen = data[offset + 6];
        entry.FileType = (FileType)data[offset + 7];

        if (entry.RecLen < MinEntrySize) return null;

        int nameEnd = Math.Min(offset + 8 + entry.NameLen, data.Length);
        int nameLen = nameEnd - (offset + 8);
        if (nameLen < 0) nameLen = 0;

        entry.Name = System.Text.Encoding.ASCII.GetString(data, offset + 8, nameLen);

        return entry;
    }

    /// <summary>
    /// Iterates all directory entries within the given block data.
    /// </summary>
    /// <param name="data">Raw directory block data.</param>
    /// <returns>Sequence of valid directory entries.</returns>
    public static IEnumerable<DirectoryEntry> ParseAll(byte[] data)
    {
        int offset = 0;
        while (offset + MinEntrySize <= data.Length)
        {
            var entry = Parse(data, offset);
            if (entry == null) yield break;

            if (entry.RecLen == 0) yield break;

            if (entry.IsValid)
                yield return entry;

            offset += entry.RecLen;
        }
    }
}
