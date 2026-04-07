using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinuxExplorer.Services;

namespace LinuxExplorer.ViewModels;

/// <summary>
/// Represents a directory node in the tree view with lazy-loaded children.
/// </summary>
public partial class DirectoryItemViewModel : ObservableObject
{
    private readonly ExtFileSystemService _service;
    private bool _childrenLoaded;

    // Sentinel child used to show the expand arrow before children are loaded
    private static readonly DirectoryItemViewModel _loadingPlaceholder =
        new DirectoryItemViewModel("[Loading...]", string.Empty, null!);

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<DirectoryItemViewModel> Children { get; } = new();

    private DirectoryItemViewModel(string name, string fullPath, ExtFileSystemService service)
    {
        _name = name;
        _fullPath = fullPath;
        _service = service;
    }

    public DirectoryItemViewModel(string name, string fullPath, ExtFileSystemService service, bool hasChildren = true)
        : this(name, fullPath, service)
    {
        if (hasChildren)
            Children.Add(_loadingPlaceholder);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
            LoadChildren();
    }

    private void LoadChildren()
    {
        _childrenLoaded = true;
        Children.Clear();

        if (_service == null || !_service.IsOpen)
            return;

        try
        {
            var entries = _service.GetEntries(FullPath)
                                  .Where(e => e.IsDirectory);

            foreach (var entry in entries)
            {
                Children.Add(new DirectoryItemViewModel(entry.Name, entry.FullPath, _service, hasChildren: true));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Silently skip directories that are inaccessible (e.g. permission errors);
            // the tree should remain functional for the directories that can be read.
        }
        catch (IOException)
        {
            // Silently skip on I/O errors so the tree remains usable.
        }
    }

    /// <summary>
    /// Forces a reload of the children (e.g. after a refresh).
    /// </summary>
    public void Reload()
    {
        _childrenLoaded = false;
        Children.Clear();
        Children.Add(_loadingPlaceholder);

        if (IsExpanded)
            LoadChildren();
    }
}
