using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LinuxExplorer.ExtFileSystem.RawDisk;

/// <summary>
/// Provides raw read/write access to physical disks and partitions using Win32 API.
/// </summary>
public sealed class RawDiskAccess : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>Gets the path used to open the disk (e.g. <c>\\.\PhysicalDrive0</c>).</summary>
    public string Path { get; }

    /// <summary>Gets whether the disk was opened with write access.</summary>
    public bool CanWrite { get; }

    /// <summary>
    /// Opens a disk or partition for raw access.
    /// </summary>
    /// <param name="path">Win32 device path, e.g. <c>\\.\PhysicalDrive0</c> or <c>\\.\C:</c>.</param>
    /// <param name="readOnly">When <see langword="true"/>, open for reading only.</param>
    /// <exception cref="IOException">Thrown when the device cannot be opened.</exception>
    public RawDiskAccess(string path, bool readOnly = true)
    {
        Path = path;
        CanWrite = !readOnly;

        uint access = NativeMethods.GENERIC_READ;
        if (!readOnly)
            access |= NativeMethods.GENERIC_WRITE;

        uint share = NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE;

        _handle = NativeMethods.CreateFile(
            path,
            access,
            share,
            nint.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL | NativeMethods.FILE_FLAG_NO_BUFFERING,
            nint.Zero);

        if (_handle == NativeMethods.INVALID_HANDLE_VALUE)
            throw new IOException($"Cannot open disk '{path}': {new Win32Exception().Message}");
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes from the disk at the given <paramref name="offset"/>.
    /// Both offset and count must be multiples of the sector size (512 bytes).
    /// </summary>
    public byte[] ReadSectors(long offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Seek(offset);
        var buffer = new byte[count];
        if (!NativeMethods.ReadFile(_handle, buffer, (uint)count, out uint bytesRead, nint.Zero) || bytesRead != count)
            throw new IOException($"Failed to read {count} bytes at offset {offset}: {new Win32Exception().Message}");

        return buffer;
    }

    /// <summary>
    /// Writes <paramref name="data"/> to the disk at the given <paramref name="offset"/>.
    /// Both offset and data length must be multiples of the sector size (512 bytes).
    /// </summary>
    public void WriteSectors(long offset, byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanWrite) throw new InvalidOperationException("Disk was opened read-only.");

        Seek(offset);
        if (!NativeMethods.WriteFile(_handle, data, (uint)data.Length, out uint bytesWritten, nint.Zero) || bytesWritten != data.Length)
            throw new IOException($"Failed to write {data.Length} bytes at offset {offset}: {new Win32Exception().Message}");
    }

    private void Seek(long offset)
    {
        int high = (int)(offset >> 32);
        int low = (int)(offset & 0xFFFFFFFF);
        uint result = NativeMethods.SetFilePointer(_handle, low, ref high, NativeMethods.FILE_BEGIN);
        if (result == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
            throw new IOException($"Seek to offset {offset} failed: {new Win32Exception().Message}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != NativeMethods.INVALID_HANDLE_VALUE)
                NativeMethods.CloseHandle(_handle);
            _handle = NativeMethods.INVALID_HANDLE_VALUE;
            _disposed = true;
        }
    }
}
