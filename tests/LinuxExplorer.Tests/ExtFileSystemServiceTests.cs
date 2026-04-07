using System.Runtime.InteropServices;
using DiscUtils.Ext;
using LinuxExplorer.Services;
using Xunit;

namespace LinuxExplorer.Tests;

public class ExtFileSystemServiceTests : IDisposable
{
    private readonly ExtFileSystemService _service = new();

    private const int MkfsTimeoutMs = 10_000;

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a small ext2 disk image using dd + mkfs.ext2.
    /// Returns the path, or null if tools are not available (e.g. on Windows).
    /// </summary>
    private static string? CreateTestExt2Image()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        if (!IsCommandAvailable("mkfs.ext2"))
            return null;

        var imgPath = Path.Combine(
            Path.GetDirectoryName(typeof(ExtFileSystemServiceTests).Assembly.Location)!,
            $"test_ext2_{Guid.NewGuid():N}.img");

        // Create a 4 MB file
        using (var f = File.Create(imgPath))
            f.SetLength(4 * 1024 * 1024);

        var mkfs = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "mkfs.ext2",
            Arguments = $"-F \"{imgPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        mkfs?.WaitForExit(MkfsTimeoutMs);

        if (mkfs?.ExitCode != 0)
        {
            File.Delete(imgPath);
            return null;
        }

        return imgPath;
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // -----------------------------------------------------------------------
    // Tests: open / close (no image needed)
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenDiskImage_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.OpenDiskImage("/nonexistent/path/disk.img"));
    }

    [Fact]
    public void IsOpen_BeforeOpening_ReturnsFalse()
    {
        Assert.False(_service.IsOpen);
    }

    [Fact]
    public void GetEntries_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.GetEntries("/").ToList());
    }

    [Fact]
    public void ReadFile_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.ReadFile("/somefile", Stream.Null));
    }

    [Fact]
    public void WriteFile_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.WriteFile("/somefile", Stream.Null));
    }

    [Fact]
    public void DeleteEntry_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.DeleteEntry("/somefile"));
    }

    [Fact]
    public void CreateDirectory_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.CreateDirectory("/newdir"));
    }

    [Fact]
    public void MoveEntry_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.MoveEntry("/src", "/dst"));
    }

    [Fact]
    public void GetDiskInfo_WhenNotOpen_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.GetDiskInfo());
    }

    // -----------------------------------------------------------------------
    // Path conversion helpers (unit tests, no image needed)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData("/lost+found", "\\lost+found")]
    [InlineData("/etc/fstab", "\\etc\\fstab")]
    public void ToDiscPath_ConvertsCorrectly(string userPath, string expected)
    {
        Assert.Equal(expected, ExtFileSystemService.ToDiscPath(userPath));
    }

    [Theory]
    [InlineData("", "/")]
    [InlineData("\\lost+found", "/lost+found")]
    [InlineData("\\etc\\fstab", "/etc/fstab")]
    public void ToUserPath_ConvertsCorrectly(string discPath, string expected)
    {
        Assert.Equal(expected, ExtFileSystemService.ToUserPath(discPath));
    }

    // -----------------------------------------------------------------------
    // Tests: real ext2 image (Linux only)
    // Note: DiscUtils.Ext 0.16.13 is a read-only driver. Write operations
    // (WriteFile, CreateDirectory, DeleteEntry, MoveEntry) will throw
    // NotSupportedException — this is an expected DiscUtils limitation.
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenDiskImage_ValidExt2Image_IsOpenReturnsTrue()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            Assert.True(_service.IsOpen);
            Assert.True(_service.IsReadOnly);
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void GetEntries_RootOfExt2Image_ReturnsLostAndFound()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            // DiscUtils.Ext uses "" as root path, but GetEntries accepts "/"
            var entries = _service.GetEntries("/").ToList();
            // A freshly formatted ext2 image has "lost+found"
            Assert.NotEmpty(entries);
            Assert.Contains(entries, e => e.Name == "lost+found" && e.IsDirectory);
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void GetEntries_EmptyRootPath_SameAsSlash()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            var bySlash = _service.GetEntries("/").ToList();
            var byEmpty = _service.GetEntries("").ToList();
            Assert.Equal(bySlash.Count, byEmpty.Count);
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void GetDiskInfo_ValidExt2Image_ReturnsNonZeroTotal()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            var (total, free, used) = _service.GetDiskInfo();
            Assert.True(total > 0, "Total disk space should be positive.");
            Assert.True(free >= 0, "Free space should be non-negative.");
            Assert.True(used >= 0, "Used space should be non-negative.");
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void GetEntries_UserPaths_AreForwardSlashPrefixed()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            var entries = _service.GetEntries("/").ToList();
            foreach (var entry in entries)
            {
                Assert.StartsWith("/", entry.FullPath);
                Assert.DoesNotContain('\\', entry.FullPath);
            }
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void CreateDirectory_ThrowsNotSupportedException_DiscUtilsReadOnlyDriver()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            // Open read-write — DiscUtils.Ext driver itself is read-only
            _service.OpenDiskImage(img!, readOnly: false);
            Assert.Throws<NotSupportedException>(() => _service.CreateDirectory("/testdir"));
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void WriteFile_ThrowsNotSupportedException_DiscUtilsReadOnlyDriver()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: false);
            Assert.Throws<NotSupportedException>(() =>
            {
                using var src = new MemoryStream(new byte[] { 1, 2, 3 });
                _service.WriteFile("/test.bin", src);
            });
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void WriteFile_WhenReadOnly_ThrowsInvalidOperationException()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var src = new MemoryStream(new byte[] { 1 });
                _service.WriteFile("/fail.bin", src);
            });
        }
        finally
        {
            _service.Close();
            if (img != null) File.Delete(img);
        }
    }

    [Fact]
    public void Close_ThenIsOpen_ReturnsFalse()
    {
        var img = CreateTestExt2Image();
        Skip.If(img == null, "mkfs.ext2 not available or not running on Linux.");

        try
        {
            _service.OpenDiskImage(img!, readOnly: true);
            Assert.True(_service.IsOpen);
            _service.Close();
            Assert.False(_service.IsOpen);
        }
        finally
        {
            if (img != null) File.Delete(img);
        }
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        _service.Dispose();
    }
}

/// <summary>
/// Helper for conditional skipping inside test methods using xunit's built-in SkipException.
/// </summary>
public static class Skip
{
    public static void If(bool condition, string reason)
    {
        if (condition)
            throw Xunit.Sdk.SkipException.ForSkip(reason);
    }
}
