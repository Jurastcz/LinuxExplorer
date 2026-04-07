namespace LinuxExplorer.ExtFileSystem.RawDisk;

/// <summary>
/// A <see cref="Stream"/> wrapper around <see cref="RawDiskAccess"/> that handles
/// sector-aligned reads and writes transparently.
/// </summary>
public sealed class DiskStream : Stream
{
    private const int SectorSize = 512;

    private readonly RawDiskAccess _disk;
    private long _position;
    private readonly long _length;

    /// <summary>
    /// Initializes a new <see cref="DiskStream"/> for the specified disk.
    /// </summary>
    /// <param name="disk">Underlying raw disk access.</param>
    /// <param name="length">Logical length of the stream in bytes. Use 0 for unknown/unlimited.</param>
    public DiskStream(RawDiskAccess disk, long length = 0)
    {
        _disk = disk ?? throw new ArgumentNullException(nameof(disk));
        _length = length;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => _disk.CanWrite;

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _disk.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End when _length > 0 => _length + offset,
            _ => _position + offset
        };
        return _position;
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        // Align reads to sector boundary
        long sectorStart = (_position / SectorSize) * SectorSize;
        int inSectorOffset = (int)(_position - sectorStart);
        int totalNeeded = inSectorOffset + count;
        int sectorCount = (totalNeeded + SectorSize - 1) / SectorSize;
        int readBytes = sectorCount * SectorSize;

        byte[] raw = _disk.ReadSectors(sectorStart, readBytes);
        int available = Math.Min(count, raw.Length - inSectorOffset);
        if (available <= 0) return 0;

        Buffer.BlockCopy(raw, inSectorOffset, buffer, offset, available);
        _position += available;
        return available;
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite) throw new NotSupportedException("Stream is read-only.");

        // For sector-aligned writes, read-modify-write unaligned portions
        long sectorStart = (_position / SectorSize) * SectorSize;
        int inSectorOffset = (int)(_position - sectorStart);
        int totalNeeded = inSectorOffset + count;
        int sectorCount = (totalNeeded + SectorSize - 1) / SectorSize;
        int totalBytes = sectorCount * SectorSize;

        // Read existing sectors for read-modify-write
        byte[] raw = _disk.ReadSectors(sectorStart, totalBytes);
        Buffer.BlockCopy(buffer, offset, raw, inSectorOffset, count);
        _disk.WriteSectors(sectorStart, raw);
        _position += count;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes at the specified <paramref name="absoluteOffset"/>
    /// without changing <see cref="Position"/>.
    /// </summary>
    public byte[] ReadAt(long absoluteOffset, int count)
    {
        long saved = _position;
        _position = absoluteOffset;
        var buffer = new byte[count];
        int totalRead = 0;
        while (totalRead < count)
        {
            int n = Read(buffer, totalRead, count - totalRead);
            if (n == 0) break;
            totalRead += n;
        }
        _position = saved;
        return buffer;
    }

    /// <summary>
    /// Writes <paramref name="data"/> at the specified <paramref name="absoluteOffset"/>
    /// without changing <see cref="Position"/>.
    /// </summary>
    public void WriteAt(long absoluteOffset, byte[] data)
    {
        long saved = _position;
        _position = absoluteOffset;
        Write(data, 0, data.Length);
        _position = saved;
    }
}
