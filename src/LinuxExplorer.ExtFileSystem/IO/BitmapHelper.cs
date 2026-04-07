namespace LinuxExplorer.ExtFileSystem.IO;

/// <summary>
/// Helper methods for working with bitmap data used in ext2/3/4 block and inode allocation.
/// </summary>
public static class BitmapHelper
{
    /// <summary>
    /// Returns <see langword="true"/> if the bit at position <paramref name="bitIndex"/> is set.
    /// </summary>
    /// <param name="bitmap">The bitmap byte array.</param>
    /// <param name="bitIndex">Zero-based bit index.</param>
    public static bool IsSet(byte[] bitmap, int bitIndex)
    {
        int byteIndex = bitIndex / 8;
        int bit = bitIndex % 8;
        if (byteIndex >= bitmap.Length) return false;
        return (bitmap[byteIndex] & (1 << bit)) != 0;
    }

    /// <summary>
    /// Sets the bit at position <paramref name="bitIndex"/> to 1 (mark as used).
    /// </summary>
    /// <param name="bitmap">The bitmap byte array.</param>
    /// <param name="bitIndex">Zero-based bit index.</param>
    public static void Set(byte[] bitmap, int bitIndex)
    {
        int byteIndex = bitIndex / 8;
        int bit = bitIndex % 8;
        if (byteIndex < bitmap.Length)
            bitmap[byteIndex] |= (byte)(1 << bit);
    }

    /// <summary>
    /// Clears the bit at position <paramref name="bitIndex"/> to 0 (mark as free).
    /// </summary>
    /// <param name="bitmap">The bitmap byte array.</param>
    /// <param name="bitIndex">Zero-based bit index.</param>
    public static void Clear(byte[] bitmap, int bitIndex)
    {
        int byteIndex = bitIndex / 8;
        int bit = bitIndex % 8;
        if (byteIndex < bitmap.Length)
            bitmap[byteIndex] &= (byte)~(1 << bit);
    }

    /// <summary>
    /// Counts the number of free (zero) bits in the bitmap.
    /// </summary>
    public static int CountFree(byte[] bitmap, int totalBits)
    {
        int free = 0;
        for (int i = 0; i < totalBits; i++)
        {
            if (!IsSet(bitmap, i)) free++;
        }
        return free;
    }

    /// <summary>
    /// Finds the first free (zero) bit in the bitmap.
    /// Returns -1 if no free bit is found within <paramref name="totalBits"/>.
    /// </summary>
    public static int FindFirstFree(byte[] bitmap, int totalBits)
    {
        for (int i = 0; i < totalBits; i++)
        {
            if (!IsSet(bitmap, i)) return i;
        }
        return -1;
    }
}
