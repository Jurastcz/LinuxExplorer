using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxExplorer.ExtFileSystem.Format;
using LinuxExplorer.ExtFileSystem.Partition;
using LinuxExplorer.ExtFileSystem.RawDisk;

namespace LinuxExplorer.ViewModels;

/// <summary>
/// Represents a disk entry in the format dialog.
/// </summary>
public sealed class FormatDiskItem
{
    public string DiskPath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long DiskSize { get; init; }
    public List<FormatPartitionItem> Partitions { get; set; } = [];
    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a partition entry in the format dialog.
/// </summary>
public sealed class FormatPartitionItem
{
    public string DisplayName { get; init; } = string.Empty;
    public long StartOffset { get; init; }
    public long Size { get; init; }
    public string DiskPath { get; init; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// ViewModel for the Format Partition dialog.
/// </summary>
public sealed partial class FormatViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<FormatDiskItem> _disks = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPartitions))]
    private FormatDiskItem? _selectedDisk;

    [ObservableProperty]
    private ObservableCollection<FormatPartitionItem> _partitions = [];

    [ObservableProperty]
    private FormatPartitionItem? _selectedPartition;

    [ObservableProperty]
    private ExtVersion _selectedVersion = ExtVersion.Ext4;

    [ObservableProperty]
    private string _volumeLabel = string.Empty;

    [ObservableProperty]
    private int _selectedBlockSize = 4096;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isFormatting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _formatCompleted;

    public bool HasPartitions => SelectedDisk?.Partitions.Count > 0;

    private const long Alignment = 1_048_576; // 1 MiB

    /// <summary>Gets the actual usable free space after accounting for MBR alignment gaps.</summary>
    public long FreeSpace
    {
        get
        {
            if (SelectedDisk is not { } disk || disk.DiskSize <= 0) return 0;

            // Calculate where the next partition would start (aligned to 1 MiB)
            long nextStart = Alignment; // first partition starts at 1 MiB
            if (disk.Partitions.Count > 0)
            {
                long lastEnd = disk.Partitions.Max(p => p.StartOffset + p.Size);
                nextStart = ((lastEnd + Alignment - 1) / Alignment) * Alignment;
            }

            long available = disk.DiskSize - nextStart;
            return available > 0 ? available : 0;
        }
    }

    /// <summary>Gets the free space formatted as a string.</summary>
    public string FreeSpaceText => SelectedDisk != null
        ? $"Free space: {FormatSize(FreeSpace)} / {FormatSize(SelectedDisk.DiskSize)}"
        : string.Empty;

    [ObservableProperty]
    private double _newPartitionSize = 1;

    [ObservableProperty]
    private string _selectedSizeUnit = "GB";

    public List<string> AvailableSizeUnits { get; } = ["MB", "GB"];

    private double UnitMultiplier => _selectedSizeUnit == "GB" ? 1_073_741_824.0 : 1_048_576.0;

    public long NewPartitionSizeBytes => (long)(_newPartitionSize * UnitMultiplier);

    public double MaxPartitionSize => FreeSpace / UnitMultiplier;

    partial void OnNewPartitionSizeChanged(double value)
    {
        OnPropertyChanged(nameof(NewPartitionSizeBytes));
    }

    partial void OnSelectedSizeUnitChanged(string value)
    {
        OnPropertyChanged(nameof(NewPartitionSizeBytes));
        OnPropertyChanged(nameof(MaxPartitionSize));
        if (_newPartitionSize > MaxPartitionSize)
        {
            _newPartitionSize = Math.Round(MaxPartitionSize, 2);
            OnPropertyChanged(nameof(NewPartitionSize));
        }
    }

    public List<ExtVersion> AvailableVersions { get; } = [ExtVersion.Ext2, ExtVersion.Ext3, ExtVersion.Ext4];

    public List<int> AvailableBlockSizes { get; } = [1024, 2048, 4096];

    /// <summary>
    /// Discovers physical disks and their partitions.
    /// </summary>
    public void DiscoverDisks()
    {
        var diskItems = new ObservableCollection<FormatDiskItem>();

        for (int i = 0; i < 16; i++)
        {
            string diskPath = $@"\\.\PhysicalDrive{i}";
            try
            {
                using var diskAccess = new RawDiskAccess(diskPath, readOnly: true);
                var stream = new DiskStream(diskAccess);
                var partReader = new PartitionTableReader(stream);
                var partitions = partReader.ReadPartitions();

                string modelName = GetDiskModelName(i);
                string displayName = string.IsNullOrEmpty(modelName)
                    ? diskPath
                    : $"{modelName} ({diskPath})";

                var partItems = partitions.Select((p, idx) =>
                {
                    string displayName = $"Partition {p.Index + 1} — {FormatSize(p.Size)} [{p.PartitionType}]";
                    if (!string.IsNullOrEmpty(p.Label))
                        displayName = $"{p.Label} - {displayName}";

                    return new FormatPartitionItem
                    {
                        DisplayName = displayName,
                        StartOffset = p.StartOffset,
                        Size = p.Size,
                        DiskPath = diskPath
                    };
                }).ToList();

                long diskSize = GetDiskSize(i);
                diskItems.Add(new FormatDiskItem
                {
                    DiskPath = diskPath,
                    DisplayName = displayName,
                    DiskSize = diskSize,
                    Partitions = partItems
                });
            }
            catch (UnauthorizedAccessException)
            {
                diskItems.Add(new FormatDiskItem
                {
                    DiskPath = diskPath,
                    DisplayName = $"PhysicalDrive{i} (Access Denied)"
                });
            }
            catch (IOException) when (i > 4) { break; }
            catch { }
        }

        Disks = diskItems;
    }

    partial void OnSelectedDiskChanged(FormatDiskItem? value)
    {
        Partitions = value != null
            ? new ObservableCollection<FormatPartitionItem>(value.Partitions)
            : [];
        SelectedPartition = Partitions.FirstOrDefault();
        OnPropertyChanged(nameof(HasPartitions));
        OnPropertyChanged(nameof(FreeSpace));
        OnPropertyChanged(nameof(FreeSpaceText));
        _selectedSizeUnit = FreeSpace >= 1_073_741_824 ? "GB" : "MB";
        OnPropertyChanged(nameof(SelectedSizeUnit));
        OnPropertyChanged(nameof(MaxPartitionSize));
        _newPartitionSize = Math.Round(MaxPartitionSize, 2);
        OnPropertyChanged(nameof(NewPartitionSize));
        OnPropertyChanged(nameof(NewPartitionSizeBytes));
    }

    [RelayCommand]
    private void DeletePartition()
    {
        if (SelectedPartition is not { } partition) return;
        if (SelectedDisk is not { } disk) return;

        var result = System.Windows.MessageBox.Show(
            $"WARNING: This will DELETE the partition entry and ERASE all data in it!\n\n" +
            $"Partition: {partition.DisplayName}\n\n" +
            $"Are you absolutely sure?",
            "Confirm Delete Partition",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            using var diskAccess = new RawDiskAccess(disk.DiskPath, readOnly: false);
            var stream = new DiskStream(diskAccess);

            byte[] mbr = stream.ReadAt(0, 512);

            long startSector = partition.StartOffset / 512;
            bool found = false;
            for (int i = 0; i < 4; i++)
            {
                int off = 446 + i * 16;
                uint lbaStart = BitConverter.ToUInt32(mbr, off + 8);
                uint lbaCount = BitConverter.ToUInt32(mbr, off + 12);
                if (lbaCount == 0) continue;
                if (lbaStart == (uint)startSector)
                {
                    Array.Clear(mbr, off, 16);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                System.Windows.MessageBox.Show("Could not find matching partition entry in MBR.",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            stream.WriteAt(0, mbr);

            // Tell the OS to re-read the partition table — this dismounts any stale volume
            // that was sitting on the deleted partition, freeing its data area for writing.
            diskAccess.UpdateDiskProperties();

            // Small delay to let Windows finish dismounting the volume.
            System.Threading.Thread.Sleep(500);

            // Zero out the first 1 MiB of the partition to wipe filesystem signatures
            // (superblock, journal, etc.). With the volume dismounted this should succeed.
            try
            {
                const int wipeSize = 1_048_576; // 1 MiB
                long actualWipe = Math.Min(wipeSize, partition.Size);
                // Must be sector-aligned
                actualWipe = (actualWipe / 512) * 512;
                if (actualWipe > 0)
                {
                    byte[] zeros = new byte[actualWipe];
                    stream.WriteAt(partition.StartOffset, zeros);
                }
            }
            catch
            {
                // Still can't write — MBR entry is already cleared so the partition
                // won't be re-discovered. Old filesystem data will be overwritten on next format.
            }

            disk.Partitions.Remove(partition);
            Partitions.Remove(partition);
            SelectedPartition = Partitions.FirstOrDefault();
            OnPropertyChanged(nameof(HasPartitions));
            OnPropertyChanged(nameof(FreeSpace));
            OnPropertyChanged(nameof(FreeSpaceText));
            _selectedSizeUnit = FreeSpace >= 1_073_741_824 ? "GB" : "MB";
            OnPropertyChanged(nameof(SelectedSizeUnit));
            OnPropertyChanged(nameof(MaxPartitionSize));
            _newPartitionSize = Math.Round(MaxPartitionSize, 2);
            OnPropertyChanged(nameof(NewPartitionSize));
            OnPropertyChanged(nameof(NewPartitionSizeBytes));

            StatusMessage = $"Partition deleted. Free space: {FormatSize(FreeSpace)}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to delete partition:\n{ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task FormatPartition()
    {
        if (SelectedPartition is not { } partition) return;

        var result = System.Windows.MessageBox.Show(
            $"WARNING: This will ERASE ALL DATA on the selected partition!\n\n" +
            $"Disk: {SelectedDisk?.DisplayName}\n" +
            $"Partition: {partition.DisplayName}\n" +
            $"Format: {SelectedVersion}\n" +
            $"Block size: {SelectedBlockSize}\n" +
            $"Label: {(string.IsNullOrEmpty(VolumeLabel) ? "(none)" : VolumeLabel)}\n\n" +
            $"Are you absolutely sure?",
            "Confirm Format",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsFormatting = true;
        Progress = 0;
        StatusMessage = "Formatting...";
        FormatCompleted = false;

        try
        {
            var options = new FormatOptions
            {
                Version = SelectedVersion,
                VolumeLabel = VolumeLabel,
                BlockSize = SelectedBlockSize
            };

            await Task.Run(() =>
            {
                var diskAccess = new RawDiskAccess(partition.DiskPath, readOnly: false);
                var stream = new DiskStream(diskAccess);
                try
                {
                    var formatter = new ExtFormatter();
                    formatter.Format(stream, partition.StartOffset, partition.Size, options,
                        p => System.Windows.Application.Current.Dispatcher.Invoke(() => Progress = p * 100));
                }
                finally
                {
                    stream.Dispose();
                }
            });

            StatusMessage = $"Format complete! Created {SelectedVersion} filesystem.";
            FormatCompleted = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Format failed: {ex.Message}";
            System.Windows.MessageBox.Show($"Format failed:\n{ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsFormatting = false;
        }
    }

    [RelayCommand]
    private void CreatePartition()
    {
        if (SelectedDisk is not { } disk) return;

        if (NewPartitionSizeBytes <= 0 || NewPartitionSizeBytes > FreeSpace)
        {
            System.Windows.MessageBox.Show(
                $"Requested size ({FormatSize(NewPartitionSizeBytes)}) exceeds available free space ({FormatSize(FreeSpace)}).\n\nReduce the partition size.",
                "Invalid Size",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Create a new MBR partition on {disk.DisplayName}?\n\n" +
            $"Size: {FormatSize(NewPartitionSizeBytes)}\n\n" +
            $"WARNING: This will modify the partition table!",
            "Confirm Create Partition",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            // Calculate start offset after existing partitions (aligned to 1 MiB)
            const long alignment = 1_048_576; // 1 MiB
            long startOffset = alignment; // default: start at 1 MiB
            if (disk.Partitions.Count > 0)
            {
                long lastEnd = disk.Partitions.Max(p => p.StartOffset + p.Size);
                startOffset = ((lastEnd + alignment - 1) / alignment) * alignment;
            }

            long sizeSectors = NewPartitionSizeBytes / 512;
            long startSector = startOffset / 512;

            if (disk.Partitions.Count >= 4)
            {
                System.Windows.MessageBox.Show("MBR supports a maximum of 4 primary partitions.",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            using var diskAccess = new RawDiskAccess(disk.DiskPath, readOnly: false);
            var stream = new DiskStream(diskAccess);

            // Read current MBR
            byte[] mbr = stream.ReadAt(0, 512);

            // If no valid MBR signature, initialize one
            if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            {
                Array.Clear(mbr, 0, 512);
                mbr[510] = 0x55;
                mbr[511] = 0xAA;
            }

            // Find first empty partition entry
            int entryIndex = -1;
            for (int i = 0; i < 4; i++)
            {
                int off = 446 + i * 16;
                uint lbaCount = BitConverter.ToUInt32(mbr, off + 12);
                if (lbaCount == 0) { entryIndex = i; break; }
            }

            if (entryIndex < 0)
            {
                System.Windows.MessageBox.Show("No free MBR partition entry.",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            // Write MBR entry: [status][CHS start 3B][type][CHS end 3B][LBA start 4B][LBA count 4B]
            int entryOff = 446 + entryIndex * 16;
            mbr[entryOff] = 0x00; // not bootable
            mbr[entryOff + 1] = 0xFE; // CHS placeholder
            mbr[entryOff + 2] = 0xFF;
            mbr[entryOff + 3] = 0xFF;
            mbr[entryOff + 4] = 0x83; // Linux type
            mbr[entryOff + 5] = 0xFE; // CHS placeholder
            mbr[entryOff + 6] = 0xFF;
            mbr[entryOff + 7] = 0xFF;
            BitConverter.GetBytes((uint)startSector).CopyTo(mbr, entryOff + 8);
            BitConverter.GetBytes((uint)sizeSectors).CopyTo(mbr, entryOff + 12);

            stream.WriteAt(0, mbr);

            // Update the VM
            var newPart = new FormatPartitionItem
            {
                DisplayName = $"Partition {disk.Partitions.Count + 1} — {FormatSize(NewPartitionSizeBytes)} [0x83]",
                StartOffset = startOffset,
                Size = NewPartitionSizeBytes,
                DiskPath = disk.DiskPath
            };
            disk.Partitions.Add(newPart);
            Partitions.Add(newPart);
            SelectedPartition = newPart;
            OnPropertyChanged(nameof(HasPartitions));
            OnPropertyChanged(nameof(FreeSpace));
            OnPropertyChanged(nameof(FreeSpaceText));
            _selectedSizeUnit = FreeSpace >= 1_073_741_824 ? "GB" : "MB";
            OnPropertyChanged(nameof(SelectedSizeUnit));
            OnPropertyChanged(nameof(MaxPartitionSize));
            _newPartitionSize = Math.Round(MaxPartitionSize, 2);
            OnPropertyChanged(nameof(NewPartitionSize));
            OnPropertyChanged(nameof(NewPartitionSizeBytes));

            StatusMessage = $"Partition created: {FormatSize(NewPartitionSizeBytes)} at offset {FormatSize(startOffset)}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to create partition:\n{ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private static long GetDiskSize(int diskIndex)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT Size FROM Win32_DiskDrive WHERE Index = {diskIndex}");
            foreach (System.Management.ManagementObject disk in searcher.Get())
            {
                var size = disk["Size"];
                if (size != null) return Convert.ToInt64(size);
            }
        }
        catch { }
        return 0;
    }

    private static string GetDiskModelName(int diskIndex)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT Model FROM Win32_DiskDrive WHERE Index = {diskIndex}");
            foreach (System.Management.ManagementObject disk in searcher.Get())
                return disk["Model"]?.ToString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
