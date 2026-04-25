# LinuxExplorer

A Windows Explorer-style file browser for **ext2 / ext3 / ext4** partitions, built with **.NET 8** and **WPF**.  
It lets you browse, read, and write Linux filesystems directly from Windows — without WSL or third-party drivers.

---

## ⚠️ Administrator Privileges Required

LinuxExplorer uses raw Win32 disk I/O (`\\.\PhysicalDriveN`) to access partition tables and filesystem structures directly.  
**The application must be run as Administrator**, otherwise:

- Physical disks will not be enumerated.
- Partitions will not be readable.
- All write operations (format, create/delete partition) will fail with an *Access Denied* error.

**How to run as Administrator:**

1. Right-click `LinuxExplorer.exe` → **Run as administrator**, or  
2. Open a terminal with elevated privileges and launch the executable from there.

---

## Features

| Feature | Description |
|---|---|
| **Disk & partition browser** | Enumerates physical disks and their ext2/3/4 partitions in a tree view on the left panel. |
| **File browser** | Navigate directories and files inside an ext partition just like Windows Explorer (double-click to enter folders). |
| **Two view modes** | Switch between *Details* (list with size/date columns) and *Large Icons* view. |
| **Back / Forward navigation** | Navigate your history with Back and Forward buttons, just like a regular file explorer. |
| **Path bar** | Type a path directly into the address bar and press Enter to navigate. |
| **File operations** | Copy files out of ext partitions to your Windows filesystem. |
| **Format Partition dialog** | Full partition management tool — see below. |

---

## Format Partition Dialog

Accessible via **Tools → Format Partition** in the menu bar.

### Partition Management

| Action | Description |
|---|---|
| **Select disk** | Choose the physical disk from the dropdown. All connected drives are listed with their model name and device path. |
| **Select partition** | If the disk already has partitions, select the one you want to format or delete. |
| **Create Partition** | Create a new MBR primary partition in the free space on the selected disk. |
| **Delete Partition** | Remove the selected partition entry from the MBR and wipe filesystem signatures. The freed space is immediately available for a new partition. |

#### Creating a Partition

1. Select a disk with available free space.
2. In the **Create Partition** panel, set the desired size using the slider or the text box.
   - The size unit (MB / GB) is set automatically based on the available free space.
   - The default value is the maximum available free space.
3. Click **Create Partition** and confirm the warning dialog.
4. The new partition appears in the partition list and is selected automatically.

> **Note:** MBR supports a maximum of **4 primary partitions** per disk.

#### Deleting a Partition

1. Select the partition you want to remove from the partition dropdown.
2. Click **Delete Partition** and confirm the warning dialog.
3. The partition entry is cleared from the MBR, filesystem signatures are wiped, and the space is immediately available for a new partition.

> **Warning:** Deleting a partition permanently destroys all data on it. This operation cannot be undone.

### Formatting a Partition

1. Select the target partition.
2. Choose the **filesystem version**: ext2, ext3, or ext4.
3. Choose the **block size**: 1024, 2048, or 4096 bytes.
4. Optionally enter a **volume label** (max 16 characters).
5. Click **Format** and confirm the warning dialog.
6. Progress is shown in the progress bar. The status message confirms completion or reports an error.

> **Warning:** Formatting permanently erases all data on the selected partition.

---

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **Administrator privileges** (see above)

---

## Building from Source

```
git clone https://github.com/Jurastcz/LinuxExplorer.git
cd LinuxExplorer
dotnet build
```

To run the application directly:

```
dotnet run --project src\LinuxExplorer\LinuxExplorer.csproj
```

> Run your terminal as Administrator before executing the above command.

To run the unit tests:

```
dotnet test
```

---

## Project Structure

```
src/
  LinuxExplorer/                  # WPF application (UI, ViewModels, Views)
  LinuxExplorer.ExtFileSystem/    # Core library (ext2/3/4 parser, raw disk I/O, formatter)
tests/
  LinuxExplorer.ExtFileSystem.Tests/  # Unit tests for the filesystem library
```

---

## License

This project is open source. See [LICENSE](LICENSE) for details.

