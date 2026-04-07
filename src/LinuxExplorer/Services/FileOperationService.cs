namespace LinuxExplorer.Services;

/// <summary>
/// Service providing high-level file operation functionality.
/// Acts as a helper for the ViewModel layer.
/// </summary>
public sealed class FileOperationService
{
    /// <summary>
    /// Copies a file from a Windows path to the given destination path (byte array content).
    /// </summary>
    /// <param name="windowsSourcePath">Windows filesystem path to read from.</param>
    /// <returns>File content as a byte array.</returns>
    public byte[] ReadWindowsFile(string windowsSourcePath) =>
        System.IO.File.ReadAllBytes(windowsSourcePath);

    /// <summary>
    /// Saves byte array content to a Windows path.
    /// </summary>
    /// <param name="windowsDestPath">Windows filesystem path to write to.</param>
    /// <param name="data">File content.</param>
    public void SaveWindowsFile(string windowsDestPath, byte[] data) =>
        System.IO.File.WriteAllBytes(windowsDestPath, data);
}
