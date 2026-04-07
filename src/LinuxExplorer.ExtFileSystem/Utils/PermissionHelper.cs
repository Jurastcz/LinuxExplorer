namespace LinuxExplorer.ExtFileSystem.Utils;

/// <summary>
/// Helper methods for parsing and formatting Unix file permissions.
/// </summary>
public static class PermissionHelper
{
    private const ushort S_ISUID = 0x0800; // Set UID bit
    private const ushort S_ISGID = 0x0400; // Set GID bit
    private const ushort S_ISVTX = 0x0200; // Sticky bit

    private const ushort S_IRUSR = 0x0100;
    private const ushort S_IWUSR = 0x0080;
    private const ushort S_IXUSR = 0x0040;
    private const ushort S_IRGRP = 0x0020;
    private const ushort S_IWGRP = 0x0010;
    private const ushort S_IXGRP = 0x0008;
    private const ushort S_IROTH = 0x0004;
    private const ushort S_IWOTH = 0x0002;
    private const ushort S_IXOTH = 0x0001;

    // File type bits
    private const ushort S_IFMT = 0xF000;
    private const ushort S_IFIFO = 0x1000;
    private const ushort S_IFCHR = 0x2000;
    private const ushort S_IFDIR = 0x4000;
    private const ushort S_IFBLK = 0x6000;
    private const ushort S_IFREG = 0x8000;
    private const ushort S_IFLNK = 0xA000;
    private const ushort S_IFSOCK = 0xC000;

    /// <summary>
    /// Converts an inode mode value to a Unix permission string like <c>-rwxr-xr-x</c>.
    /// </summary>
    /// <param name="mode">The raw <c>i_mode</c> value from the inode.</param>
    /// <returns>A 10-character string like <c>drwxr-xr-x</c>.</returns>
    public static string ToPermissionString(ushort mode)
    {
        Span<char> result = stackalloc char[10];

        // File type character
        result[0] = (mode & S_IFMT) switch
        {
            S_IFDIR => 'd',
            S_IFLNK => 'l',
            S_IFBLK => 'b',
            S_IFCHR => 'c',
            S_IFIFO => 'p',
            S_IFSOCK => 's',
            _ => '-'
        };

        // User permissions
        result[1] = (mode & S_IRUSR) != 0 ? 'r' : '-';
        result[2] = (mode & S_IWUSR) != 0 ? 'w' : '-';
        result[3] = GetExecuteBit(mode, S_IXUSR, S_ISUID, 's', 'S');

        // Group permissions
        result[4] = (mode & S_IRGRP) != 0 ? 'r' : '-';
        result[5] = (mode & S_IWGRP) != 0 ? 'w' : '-';
        result[6] = GetExecuteBit(mode, S_IXGRP, S_ISGID, 's', 'S');

        // Other permissions
        result[7] = (mode & S_IROTH) != 0 ? 'r' : '-';
        result[8] = (mode & S_IWOTH) != 0 ? 'w' : '-';
        result[9] = GetExecuteBit(mode, S_IXOTH, S_ISVTX, 't', 'T');

        return new string(result);
    }

    private static char GetExecuteBit(ushort mode, ushort executeBit, ushort specialBit, char withExec, char withoutExec)
    {
        bool exec = (mode & executeBit) != 0;
        bool special = (mode & specialBit) != 0;

        if (exec && special) return withExec;
        if (!exec && special) return withoutExec;
        return exec ? 'x' : '-';
    }

    /// <summary>
    /// Gets the Unix octal representation of the permission bits, e.g. "755" or "644".
    /// </summary>
    public static string ToOctalString(ushort mode) =>
        Convert.ToString(mode & 0x0FFF, 8).PadLeft(4, '0');

    /// <summary>
    /// Gets a human-readable file type name from the mode.
    /// </summary>
    public static string GetFileTypeName(ushort mode)
    {
        return (mode & S_IFMT) switch
        {
            S_IFDIR => "Directory",
            S_IFLNK => "Symbolic Link",
            S_IFBLK => "Block Device",
            S_IFCHR => "Character Device",
            S_IFIFO => "Named Pipe",
            S_IFSOCK => "Socket",
            S_IFREG => "Regular File",
            _ => "Unknown"
        };
    }
}
