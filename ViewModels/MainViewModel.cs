using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

/// <summary>Shell state: responsive-layout mode, explorer drawer, title/status text, and wiring between explorer and editor.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(ExplorerViewModel explorer, EditorWorkspaceViewModel workspace)
    {
        Explorer = explorer;
        Workspace = workspace;
        Explorer.PropertyChanged += OnExplorerPropertyChanged;

        Explorer.FolderPicked += (_, _) =>
        {
            // On phones the explorer is a drawer; show it so the user sees the folder they picked.
            if (IsCompact)
            {
                IsExplorerOpen = true;
            }
        };

        // Explorer -> editor
        Explorer.ConfirmRootChangeAsync = Workspace.ConfirmCanCloseAllAsync;
        Explorer.ConfirmExportAsync = Workspace.ConfirmSaveBeforeExportAsync;
        Explorer.IsFileOpen = Workspace.IsOpen;
        Explorer.RootReplaced += Workspace.CloseAllTabs;
        Explorer.NodeRenamed += Workspace.HandleRenamed;
        Explorer.FileActivated += OnFileActivated;
        Explorer.TreeChanged += OnTreeChanged;

        // Editor -> explorer
        Workspace.FilesChanged += () => Explorer.RefreshCommand.Execute(null);
    }

    public ExplorerViewModel Explorer { get; }

    public EditorWorkspaceViewModel Workspace { get; }

    /// <summary>True on narrow windows (phones): explorer becomes an overlay drawer.</summary>
    [ObservableProperty]
    private bool isCompact;

    [ObservableProperty]
    private bool isExplorerOpen;

    public string ProjectTitle => Explorer.Root?.Name ?? "Sodium Studio";

    public bool HasProject => Explorer.HasRoot;

    public bool NoProject => !Explorer.HasRoot;

    public bool HasSelection => Explorer.SelectedItem is not null;

    public bool NoSelection => Explorer.SelectedItem is null;

    public string StatusText =>
        Explorer.Root is null ? "No folder open"
        : Explorer.SelectedItem is { } item ? $"{Explorer.Root.Name}  ›  {item.Name}"
        : Explorer.Root.Name;

    public string SelectionDetails
    {
        get
        {
            if (Explorer.SelectedItem is not { } item)
            {
                return string.Empty;
            }

            var node = item.Node;
            var size = node.Size is { } bytes ? FormatSize(bytes) : "—";
            var modified = node.LastModified is { } time
                ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "—";

            return $"{(node.IsDirectory ? "Folder" : "File")}: {node.Name}\n" +
                   $"Size: {(node.IsDirectory ? "—" : size)}\n" +
                   $"Modified: {modified}";
        }
    }

    [RelayCommand]
    private void ToggleExplorer() => IsExplorerOpen = !IsExplorerOpen;

    [RelayCommand]
    private void CloseExplorer() => IsExplorerOpen = false;

    // async void is confined to these two event adapters; the awaited methods handle their own errors.
    private async void OnFileActivated(StorageNode file, StorageNode parent)
    {
        if (IsCompact)
        {
            IsExplorerOpen = false; // editor first on phones
        }

        await Workspace.OpenAsync(file, parent);
    }

    private async void OnTreeChanged()
    {
        try
        {
            await Workspace.ValidateTabsAsync();
        }
        catch (Exception)
        {
            // Validation is best effort; open tabs are kept if anything goes wrong.
        }
    }

    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerViewModel.Root):
                OnPropertyChanged(nameof(ProjectTitle));
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(NoProject));
                OnPropertyChanged(nameof(StatusText));
                break;
            case nameof(ExplorerViewModel.SelectedItem):
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(NoSelection));
                OnPropertyChanged(nameof(SelectionDetails));
                OnPropertyChanged(nameof(StatusText));
                break;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
