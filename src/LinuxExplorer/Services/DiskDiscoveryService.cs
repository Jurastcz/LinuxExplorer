using LinuxExplorer.ExtFileSystem.Navigation;
using LinuxExplorer.ExtFileSystem.Partition;
using LinuxExplorer.ExtFileSystem.RawDisk;
using LinuxExplorer.ViewModels;

namespace LinuxExplorer.Services;

/// <summary>
/// Service responsible for discovering physical disks and their ext partitions.
/// </summary>
public sealed class DiskDiscoveryService
{
    private const int MaxDisks = 16;

    /// <summary>
    /// Enumerates all physical disks and returns those with ext2/3/4 partitions.
    /// </summary>
    public List<DiskViewModel> DiscoverDisks()
    {
        var diskVMs = new List<DiskViewModel>();

        for (int i = 0; i < MaxDisks; i++)
        {
            string diskPath = $@"\\.\PhysicalDrive{i}";
            try
            {
                using var diskAccess = new RawDiskAccess(diskPath, readOnly: true);
                var stream = new DiskStream(diskAccess);
                var partReader = new PartitionTableReader(stream);
                var partitions = partReader.ReadPartitions();

                var extPartitions = partitions.Where(p => p.IsExtFilesystem).ToList();
                if (extPartitions.Count == 0) continue;

                var diskVm = new DiskViewModel(diskPath, GetDiskModelName(i));
                var partVms = new List<PartitionViewModel>();

                foreach (var part in extPartitions)
                {
                    partVms.Add(new PartitionViewModel(part));
                }

                diskVm.Partitions = partVms;
                diskVMs.Add(diskVm);
            }
            catch (UnauthorizedAccessException)
            {
                // Not enough privileges – add a placeholder
                var diskVm = new DiskViewModel(diskPath, $"PhysicalDrive{i} (Access Denied)");
                diskVMs.Add(diskVm);
            }
            catch (IOException)
            {
                // Disk doesn't exist or can't be opened – stop trying
                if (i > 4) break;
            }
            catch
            {
                // Ignore other errors for this disk
            }
        }

        return diskVMs;
    }

    /// <summary>
    /// Opens the ext filesystem on the given partition.
    /// </summary>
    public void OpenPartition(PartitionViewModel partitionVm)
    {
        // Find the disk path from the partition info  
        // We need to traverse back up – find the disk that contains this partition
        // For now, we try all physical drives
        for (int i = 0; i < MaxDisks; i++)
        {
            string diskPath = $@"\\.\PhysicalDrive{i}";
            try
            {
                var diskAccess = new RawDiskAccess(diskPath, readOnly: true);
                var stream = new DiskStream(diskAccess);
                var partReader = new PartitionTableReader(stream);
                var partitions = partReader.ReadPartitions();

                bool found = partitions.Any(p =>
                    p.StartOffset == partitionVm.PartitionInfo.StartOffset &&
                    p.Size == partitionVm.PartitionInfo.Size);

                if (found)
                {
                    // Create a new stream for the filesystem (will be owned by ExtFileSystemAccess)
                    var fsAccess = new RawDiskAccess(diskPath, readOnly: true);
                    var fsStream = new DiskStream(fsAccess);
                    var fs = ExtFileSystemAccess.Open(fsStream, partitionVm.PartitionInfo.StartOffset, readOnly: true);
                    partitionVm.Open(fs);
                    return;
                }
                else
                {
                    diskAccess.Dispose();
                }
            }
            catch (IOException) when (i > 4) { break; }
            catch { }
        }

        throw new IOException($"Could not find disk containing partition at offset {partitionVm.PartitionInfo.StartOffset}.");
    }

    private static string GetDiskModelName(int diskIndex)
    {
        try
        {
            // Try to get the disk model via WMI
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT Model FROM Win32_DiskDrive WHERE Index = {diskIndex}");
            foreach (System.Management.ManagementObject disk in searcher.Get())
            {
                return disk["Model"]?.ToString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }
}
