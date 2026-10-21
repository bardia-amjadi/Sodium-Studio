using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SodiumStudio.Projects;
using SodiumStudio.Services;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

public sealed partial class ExplorerViewModel : ObservableObject
{
    private const string NewFileAction = "New file";
    private const string NewFolderAction = "New folder";
    private const string RenameAction = "Rename";
    private const string DeleteAction = "Delete";
    private const string OpenAnotherAction = "Open another folder";
    private const string CloseFolderAction = "Close folder";
    private const string NewProjectAction = "New project...";
    private const string ImportArchiveAction = "Import .zip...";
    private const string ExportArchiveAction = "Export as .zip";
    private const string ProjectSettingsAction = "Project settings...";

    private readonly IProjectFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly IProjectDialogs _projectDialogs;
    private readonly IExternalFileService _externalFiles;
    private readonly ProjectCreator _projectCreator;
    private readonly ArchiveService _archives;
    private readonly ProjectSettingsStore _settingsStore;

    // Document ids of expanded folders; survives refreshes.
    private readonly HashSet<string> _expanded = new();

    public ExplorerViewModel(
        IProjectFileSystem fileSystem,
        IDialogService dialogs,
        IProjectDialogs projectDialogs,
        IExternalFileService externalFiles,
        ProjectCreator projectCreator,
        ArchiveService archives,
        ProjectSettingsStore settingsStore)
    {
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _projectDialogs = projectDialogs;
        _externalFiles = externalFiles;
        _projectCreator = projectCreator;
        _archives = archives;
        _settingsStore = settingsStore;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsFolderEmpty));
    }

    /// <summary>
    /// Asked before a folder is exported, so unsaved editor changes are not silently left out.
    /// Returning false cancels the export.
    /// </summary>
    public Func<Task<bool>>? ConfirmExportAsync { get; set; }

    /// <summary>Tells whether a file is currently open in the editor.</summary>
    public Func<StorageNode, bool>? IsFileOpen { get; set; }

    /// <summary>Raised after the user successfully picked a folder.</summary>
    public event EventHandler? FolderPicked;

    /// <summary>A file row was tapped: the file and its parent folder.</summary>
    public event Action<StorageNode, StorageNode>? FileActivated;

    /// <summary>A file was renamed here: old node, new node.</summary>
    public event Action<StorageNode, StorageNode>? NodeRenamed;

    /// <summary>Items were deleted or a folder was renamed, so open files may have disappeared.</summary>
    public event Action? TreeChanged;

    /// <summary>The root folder was replaced or closed; open files from the old root are invalid.</summary>
    public event Action? RootReplaced;

    /// <summary>
    /// Asked before the root folder is changed or closed. Returning false cancels the change
    /// (used to protect unsaved editor buffers).
    /// </summary>
    public Func<Task<bool>>? ConfirmRootChangeAsync { get; set; }

    public ObservableCollection<ExplorerItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoot), nameof(NoRoot), nameof(IsFolderEmpty))]
    [NotifyCanExecuteChangedFor(nameof(NewFileCommand), nameof(NewFolderCommand), nameof(MoreCommand))]
    private StorageNode? root;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderEmpty))]
    [NotifyCanExecuteChangedFor(nameof(NewFileCommand), nameof(NewFolderCommand), nameof(MoreCommand), nameof(RefreshCommand))]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsFolderEmpty))]
    private string? errorMessage;

    [ObservableProperty]
    private ExplorerItemViewModel? selectedItem;

    public bool HasRoot => Root is not null;

    public bool NoRoot => Root is null;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>True when a folder is open, fully loaded, has no entries and no error is shown.</summary>
    public bool IsFolderEmpty => HasRoot && !IsLoading && Items.Count == 0 && !HasError;

    partial void OnSelectedItemChanged(ExplorerItemViewModel? oldValue, ExplorerItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    // ------------------------------------------------------------ startup

    /// <summary>Re-opens the folder from the previous session, if access is still granted.</summary>
    public Task InitializeAsync() => ExecuteAsync(RestoreAsync);

    private async Task RestoreAsync()
    {
        var restored = await _fileSystem.RestoreRootFolderAsync();
        if (restored is null)
        {
            return;
        }

        SetRoot(restored);
        await ReloadAsync();
    }

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private Task OpenFolderAsync() => ExecuteAsync(async () =>
    {
        if (ConfirmRootChangeAsync is { } confirm && !await confirm())
        {
            return; // user keeps the current folder and its unsaved files
        }

        var picked = await _fileSystem.PickRootFolderAsync();
        if (picked is null)
        {
            return; // cancelled
        }

        SetRoot(picked);
        FolderPicked?.Invoke(this, EventArgs.Empty);
        await ReloadAsync();
    });

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ExecuteAsync(() => Root is null ? RestoreAsync() : ReloadAsync());

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task NewFileAsync() => CreateAsync(GetCreationTarget(), isFolder: false);

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task NewFolderAsync() => CreateAsync(GetCreationTarget(), isFolder: true);

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task MoreAsync()
    {
        var choice = await _dialogs.ActionSheetAsync(
            "Folder", NewProjectAction, ImportArchiveAction, ExportArchiveAction, ProjectSettingsAction,
            OpenAnotherAction, CloseFolderAction);
        switch (choice)
        {
            case OpenAnotherAction:
                await OpenFolderCommand.ExecuteAsync(null);
                break;
            case CloseFolderAction:
                await CloseFolderAsync();
                break;
            default:
                if (Root is not null)
                {
                    await HandleProjectActionAsync(choice, Root);
                }

                break;
        }
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    [RelayCommand]
    private Task ToggleItemAsync(ExplorerItemViewModel? item) => ExecuteAsync(async () =>
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        if (!item.IsDirectory)
        {
            FileActivated?.Invoke(item.Node, item.Parent);
            return;
        }

        if (item.IsExpanded)
        {
            Collapse(item);
        }
        else
        {
            await ExpandAsync(item);
        }
    });

    [RelayCommand]
    private async Task ShowItemMenuAsync(ExplorerItemViewModel? item)
    {
        if (item is null || IsLoading)
        {
            return;
        }

        SelectedItem = item;

        var actions = new List<string>();
        if (item.IsDirectory && item.Node.CanCreateChildren)
        {
            actions.Add(NewFileAction);
            actions.Add(NewFolderAction);
            actions.Add(NewProjectAction);
            actions.Add(ImportArchiveAction);
        }

        if (item.IsDirectory)
        {
            actions.Add(ExportArchiveAction);
            actions.Add(ProjectSettingsAction);
        }

        if (item.Node.CanRename)
        {
            actions.Add(RenameAction);
        }

        if (item.Node.CanDelete)
        {
            actions.Add(DeleteAction);
        }

        if (actions.Count == 0)
        {
            await _dialogs.AlertAsync(item.Name, "The storage provider does not allow changes to this item.");
            return;
        }

        var picked = await _dialogs.ActionSheetAsync(item.Name, actions.ToArray());
        switch (picked)
        {
            case NewProjectAction:
            case ImportArchiveAction:
            case ExportArchiveAction:
            case ProjectSettingsAction:
                await HandleProjectActionAsync(picked, item.Node);
                break;
            case NewFileAction:
                await CreateAsync(item.Node, isFolder: false);
                break;
            case NewFolderAction:
                await CreateAsync(item.Node, isFolder: true);
                break;
            case RenameAction:
                await RenameItemAsync(item);
                break;
            case DeleteAction:
                await DeleteItemAsync(item);
                break;
        }
    }

    private bool CanOperate() => Root is not null && !IsLoading;

    private bool CanRefresh() => !IsLoading;

    // ------------------------------------------------------------ operations

    private async Task CreateAsync(StorageNode? target, bool isFolder)
    {
        if (target is null)
        {
            return;
        }

        if (!target.CanCreateChildren)
        {
            await _dialogs.AlertAsync("Cannot create", $"New items cannot be created inside '{target.Name}'.");
            return;
        }

        var kind = isFolder ? "folder" : "file";
        var entered = await _dialogs.PromptAsync(
            isFolder ? "New folder" : "New file",
            $"Name of the new {kind} in '{target.Name}':",
            "Create");
        if (entered is null)
        {
            return;
        }

        var name = entered.Trim();
        if (!NameValidator.TryValidate(name, out var error))
        {
            await _dialogs.AlertAsync("Invalid name", error);
            return;
        }

        await ExecuteAsync(async () =>
        {
            var created = isFolder
                ? await _fileSystem.CreateFolderAsync(target, name)
                : await _fileSystem.CreateFileAsync(target, name);

            _expanded.Add(target.DocumentId); // make the new item visible
            await ReloadAsync(created.DocumentId);
        });
    }

    private async Task RenameItemAsync(ExplorerItemViewModel item)
    {
        var entered = await _dialogs.PromptAsync("Rename", $"New name for '{item.Name}':", "Rename", item.Name);
        if (entered is null)
        {
            return;
        }

        var newName = entered.Trim();
        if (newName == item.Name)
        {
            return;
        }

        if (!NameValidator.TryValidate(newName, out var error))
        {
            await _dialogs.AlertAsync("Invalid name", error);
            return;
        }

        await ExecuteAsync(async () =>
        {
            var renamed = await _fileSystem.RenameAsync(item.Parent, item.Node, newName);

            // Document ids of descendants may change with the parent's id; their expansion state
            // is not carried over (known limitation, documented).
            if (_expanded.Remove(item.Node.DocumentId))
            {
                _expanded.Add(renamed.DocumentId);
            }

            await ReloadAsync(renamed.DocumentId);

            if (item.IsDirectory)
            {
                TreeChanged?.Invoke(); // files below the folder may have moved
            }
            else
            {
                NodeRenamed?.Invoke(item.Node, renamed);
            }
        });
    }

    private async Task DeleteItemAsync(ExplorerItemViewModel item)
    {
        var message = item.IsDirectory
            ? $"Delete the folder '{item.Name}' and everything inside it? This cannot be undone."
            : $"Delete the file '{item.Name}'? This cannot be undone.";

        if (!await _dialogs.ConfirmAsync("Delete", message, "Delete", "Cancel"))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _fileSystem.DeleteAsync(item.Node);
            _expanded.Remove(item.Node.DocumentId);
            if (SelectedItem == item)
            {
                SelectedItem = null;
            }

            await ReloadAsync();
            TreeChanged?.Invoke();
        });
    }

    private async Task CloseFolderAsync()
    {
        if (Root is null)
        {
            return;
        }

        var current = Root;
        await ExecuteAsync(async () =>
        {
            if (ConfirmRootChangeAsync is { } confirm && !await confirm())
            {
                return;
            }

            await _fileSystem.CloseRootFolderAsync(current);
            SetRoot(null);
        });
    }

    // ------------------------------------------------------------ tree handling

    private StorageNode? GetCreationTarget() =>
        SelectedItem is null ? Root
        : SelectedItem.IsDirectory ? SelectedItem.Node
        : SelectedItem.Parent;

    private void SetRoot(StorageNode? newRoot)
    {
        _expanded.Clear();
        SelectedItem = null;
        Items.Clear();
        Root = newRoot;
        RootReplaced?.Invoke();
    }

    private async Task ExpandAsync(ExplorerItemViewModel item)
    {
        var children = await _fileSystem.ListChildrenAsync(item.Node);

        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _expanded.Add(item.Node.DocumentId);
        item.IsExpanded = true;

        var position = index + 1;
        foreach (var child in children)
        {
            Items.Insert(position++, new ExplorerItemViewModel(this, child, item.Node, item.Depth + 1));
        }
    }

    private void Collapse(ExplorerItemViewModel item)
    {
        _expanded.Remove(item.Node.DocumentId);
        item.IsExpanded = false;

        var index = Items.IndexOf(item);
        while (index >= 0 && index + 1 < Items.Count && Items[index + 1].Depth > item.Depth)
        {
            Items.RemoveAt(index + 1);
        }
    }

    /// <summary>
    /// Rebuilds the visible tree from storage, re-expanding folders that were expanded.
    /// A failure while listing the root keeps the old tree and surfaces the error; a failure in
    /// a sub-folder collapses that folder and reports it without aborting the rest.
    /// </summary>
    private async Task ReloadAsync(string? selectDocumentId = null)
    {
        if (Root is null)
        {
            return;
        }

        var wantedId = selectDocumentId ?? SelectedItem?.Node.DocumentId;
        var problems = new List<string>();
        var rebuilt = new List<ExplorerItemViewModel>();

        await AppendChildrenAsync(Root, 0, rebuilt, problems, isRoot: true);

        Items.Clear();
        foreach (var item in rebuilt)
        {
            Items.Add(item);
        }

        SelectedItem = wantedId is null ? null : Items.FirstOrDefault(i => i.Node.DocumentId == wantedId);

        if (problems.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, problems);
        }
    }

    private async Task AppendChildrenAsync(
        StorageNode directory, int depth, List<ExplorerItemViewModel> target, List<string> problems, bool isRoot)
    {
        IReadOnlyList<StorageNode> children;
        try
        {
            children = await _fileSystem.ListChildrenAsync(directory);
        }
        catch (StorageException ex) when (!isRoot)
        {
            _expanded.Remove(directory.DocumentId);
            problems.Add($"Could not open '{directory.Name}': {ex.Message}");
            return;
        }

        foreach (var child in children)
        {
            var item = new ExplorerItemViewModel(this, child, directory, depth);
            target.Add(item);

            if (child.IsDirectory && _expanded.Contains(child.DocumentId))
            {
                item.IsExpanded = true;
                await AppendChildrenAsync(child, depth + 1, target, problems, isRoot: false);
                if (!_expanded.Contains(child.DocumentId))
                {
                    item.IsExpanded = false; // listing failed and was collapsed
                }
            }
        }
    }

    // ------------------------------------------------------------ shared execution wrapper

    private async Task ExecuteAsync(Func<Task> work)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await work();
        }
        catch (StorageException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ArchiveException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user; nothing to report.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
