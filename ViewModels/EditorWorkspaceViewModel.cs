using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SodiumStudio.Editing;
using SodiumStudio.Services;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

/// <summary>Open files, saving, and the editor options. Editor-control interaction lives in EditorView.</summary>
public sealed partial class EditorWorkspaceViewModel : ObservableObject
{
    private readonly IProjectFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly HashSet<string> _opening = new();

    public EditorWorkspaceViewModel(IProjectFileSystem fileSystem, IDialogService dialogs, EditorSettings settings)
    {
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        Settings = settings;
    }

    /// <summary>Raised when the editor created a file (Save As) so the explorer can refresh.</summary>
    public event Action? FilesChanged;

    public EditorSettings Settings { get; }

    public ObservableCollection<EditorTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabs), nameof(NoTabs), nameof(StatusDetails))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand), nameof(EditorOptionsCommand))]
    private EditorTabViewModel? activeTab;

    /// <summary>"Ln 3, Col 14", updated by the editor view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetails))]
    private string caretText = string.Empty;

    public bool HasTabs => ActiveTab is not null;

    public bool NoTabs => ActiveTab is null;

    public string StatusDetails
    {
        get
        {
            if (ActiveTab is not { } tab)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (CaretText.Length > 0)
            {
                parts.Add(CaretText);
            }

            parts.Add(tab.HasBom ? "UTF-8 BOM" : "UTF-8");
            parts.Add(TextFileCodec.DisplayName(tab.LineEnding));
            parts.Add(LanguageInfo.DisplayName(tab.Language));
            if (tab.IsReadOnly)
            {
                parts.Add("Read-only");
            }

            return string.Join("   ", parts);
        }
    }

    partial void OnActiveTabChanged(EditorTabViewModel? oldValue, EditorTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    // ------------------------------------------------------------ opening

    public async Task OpenAsync(StorageNode file, StorageNode parent)
    {
        var key = file.TreeUri + "|" + file.DocumentId;
        var existing = Tabs.FirstOrDefault(t => t.Node.TreeUri == file.TreeUri && t.Node.DocumentId == file.DocumentId);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        if (!_opening.Add(key))
        {
            return; // already being opened (double tap)
        }

        try
        {
            if (file.Size is { } size && size > TextFileCodec.MaxBytes)
            {
                await _dialogs.AlertAsync("Cannot open file",
                    $"'{file.Name}' is larger than {TextFileCodec.MaxBytes / (1024 * 1024)} MB, which this build cannot open.");
                return;
            }

            var bytes = await _fileSystem.ReadAllBytesAsync(file, TextFileCodec.MaxBytes);
            if (!TextFileCodec.TryDecode(bytes, out var decoded, out var error))
            {
                await _dialogs.AlertAsync("Cannot open file", $"'{file.Name}': {error}");
                return;
            }

            var tab = new EditorTabViewModel(this, file, parent, decoded!);
            Tabs.Add(tab);
            ActiveTab = tab;

            if (decoded!.HadMixedLineEndings)
            {
                await _dialogs.AlertAsync("Mixed line endings",
                    $"'{file.Name}' uses several line-ending styles. They will all be saved as {TextFileCodec.DisplayName(decoded.LineEnding)}.");
            }
        }
        catch (StorageException ex)
        {
            await _dialogs.AlertAsync("Cannot open file", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Unexpected error", ex.Message);
        }
        finally
        {
            _opening.Remove(key);
        }
    }

    [RelayCommand]
    private void SelectTab(EditorTabViewModel? tab)
    {
        if (tab is not null)
        {
            ActiveTab = tab;
        }
    }

    // ------------------------------------------------------------ saving

    private bool HasActiveTab() => ActiveTab is not null;

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private Task SaveAsync() => ActiveTab is { } tab ? SaveTabAsync(tab) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private Task SaveAsAsync() => ActiveTab is { } tab ? SaveTabAsAsync(tab) : Task.CompletedTask;

    public async Task<bool> SaveAllAsync()
    {
        foreach (var tab in Tabs.Where(t => t.IsDirty).ToList())
        {
            if (!await SaveTabAsync(tab))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> SaveTabAsync(EditorTabViewModel tab)
    {
        if (tab.IsMissing)
        {
            // The original file is gone; saving must create a new one.
            return await SaveTabAsAsync(tab);
        }

        if (tab.IsSaving)
        {
            return false;
        }

        tab.IsSaving = true;
        try
        {
            var text = tab.Document.Text;
            var bytes = TextFileCodec.Encode(text, tab.LineEnding, tab.HasBom);
            await _fileSystem.WriteAllBytesAsync(tab.Node, bytes);

            // Edits typed while the write was running keep the tab dirty.
            tab.IsDirty = tab.Document.Text != text;
            return true;
        }
        catch (StorageException ex)
        {
            await _dialogs.AlertAsync("Cannot save", $"'{tab.Node.Name}' was NOT saved: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Unexpected error", $"'{tab.Node.Name}' was NOT saved: {ex.Message}");
            return false;
        }
        finally
        {
            tab.IsSaving = false;
        }
    }

    /// <summary>Saves under a new name in the same folder. Never overwrites an existing file.</summary>
    public async Task<bool> SaveTabAsAsync(EditorTabViewModel tab)
    {
        var entered = await _dialogs.PromptAsync(
            "Save As", $"File name (saved in '{tab.Parent.Name}'):", "Save", tab.Node.Name);
        if (entered is null)
        {
            return false;
        }

        var name = entered.Trim();
        if (!NameValidator.TryValidate(name, out var error))
        {
            await _dialogs.AlertAsync("Invalid name", error);
            return false;
        }

        try
        {
            var text = tab.Document.Text;
            var created = await _fileSystem.CreateFileAsync(tab.Parent, name); // fails if the name exists

            try
            {
                await _fileSystem.WriteAllBytesAsync(created, TextFileCodec.Encode(text, tab.LineEnding, tab.HasBom));
            }
            catch
            {
                // Do not leave an empty file behind after a failed Save As.
                try
                {
                    await _fileSystem.DeleteAsync(created);
                }
                catch (Exception)
                {
                    // Best effort.
                }

                throw;
            }

            tab.Node = created;
            tab.IsMissing = false;
            tab.IsDirty = tab.Document.Text != text;
            OnPropertyChanged(nameof(StatusDetails));
            FilesChanged?.Invoke();
            return true;
        }
        catch (StorageException ex)
        {
            await _dialogs.AlertAsync("Cannot save", $"Not saved: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Unexpected error", $"Not saved: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------ closing

    [RelayCommand]
    private Task CloseTabAsync(EditorTabViewModel? tab) => tab is null ? Task.CompletedTask : CloseAsync(tab);

    public async Task<bool> CloseAsync(EditorTabViewModel tab)
    {
        if (tab.IsDirty)
        {
            var choice = await _dialogs.ActionSheetAsync($"'{tab.Node.Name}' has unsaved changes", "Save", "Discard changes");
            if (choice is null)
            {
                return false;
            }

            if (choice == "Save" && !await SaveTabAsync(tab))
            {
                return false;
            }
        }

        RemoveTab(tab);
        return true;
    }

    /// <summary>
    /// Used before the project folder changes. Offers to save or discard unsaved work;
    /// returns false if the user cancels or a save fails.
    /// </summary>
    public async Task<bool> ConfirmCanCloseAllAsync()
    {
        var dirty = Tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return true;
        }

        var choice = await _dialogs.ActionSheetAsync(
            $"{dirty.Count} open file(s) have unsaved changes", "Save all", "Discard changes");
        return choice switch
        {
            null => false,
            "Save all" => await SaveAllAsync(),
            _ => true,
        };
    }

    /// <summary>True if the file is open in a tab.</summary>
    public bool IsOpen(StorageNode node) =>
        Tabs.Any(t => t.Node.TreeUri == node.TreeUri && t.Node.DocumentId == node.DocumentId);

    /// <summary>
    /// Before a zip export: unsaved changes are not in the files on disk, so the user decides whether
    /// to save them first. Returns false if the user cancels or a save fails.
    /// </summary>
    public async Task<bool> ConfirmSaveBeforeExportAsync()
    {
        var dirty = Tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return true;
        }

        const string saveAll = "Save all and export";
        const string asSaved = "Export saved versions";
        var choice = await _dialogs.ActionSheetAsync(
            $"{dirty.Count} open file(s) have unsaved changes", saveAll, asSaved);
        return choice switch
        {
            null => false,
            saveAll => await SaveAllAsync(),
            _ => true,
        };
    }

    public void CloseAllTabs()
    {
        Tabs.Clear();
        ActiveTab = null;
    }

    private void RemoveTab(EditorTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    // ------------------------------------------------------------ explorer changes

    public void HandleRenamed(StorageNode oldNode, StorageNode renamed)
    {
        var tab = Tabs.FirstOrDefault(t => t.Node.TreeUri == oldNode.TreeUri && t.Node.DocumentId == oldNode.DocumentId);
        if (tab is null)
        {
            return;
        }

        tab.Node = renamed;
        OnPropertyChanged(nameof(StatusDetails));
    }

    /// <summary>
    /// After a delete or folder rename: tabs whose file no longer exists are closed if clean,
    /// or kept and flagged "(deleted)" if they have unsaved changes (Save then creates a new file).
    /// </summary>
    public async Task ValidateTabsAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            bool exists;
            try
            {
                exists = await _fileSystem.ExistsAsync(tab.Node);
            }
            catch (Exception)
            {
                continue; // cannot tell; keep the tab
            }

            if (exists)
            {
                continue;
            }

            if (tab.IsDirty)
            {
                tab.IsMissing = true;
            }
            else
            {
                RemoveTab(tab);
            }
        }
    }

    // ------------------------------------------------------------ options

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private async Task EditorOptionsAsync()
    {
        var s = Settings;
        var wrap = $"Word wrap: {(s.WordWrap ? "on" : "off")}";
        var lines = $"Line numbers: {(s.ShowLineNumbers ? "on" : "off")}";
        var indentWith = $"Indent with: {(s.UseTabs ? "tabs" : "spaces")}";
        var indentSize = $"Indent size: {s.IndentSize}";
        var font = $"Font size: {s.FontSize}";
        const string saveAs = "Save As...";
        const string saveAll = "Save all";

        var choice = await _dialogs.ActionSheetAsync("Editor", saveAs, saveAll, wrap, lines, indentWith, indentSize, font);
        if (choice is null)
        {
            return;
        }

        if (choice == saveAs)
        {
            await SaveAsAsync();
        }
        else if (choice == saveAll)
        {
            await SaveAllAsync();
        }
        else if (choice == wrap)
        {
            s.WordWrap = !s.WordWrap;
        }
        else if (choice == lines)
        {
            s.ShowLineNumbers = !s.ShowLineNumbers;
        }
        else if (choice == indentWith)
        {
            s.UseTabs = !s.UseTabs;
        }
        else if (choice == indentSize)
        {
            var picked = await _dialogs.ActionSheetAsync("Indent size", "2", "4", "8");
            if (int.TryParse(picked, out var value))
            {
                s.IndentSize = value;
            }
        }
        else if (choice == font)
        {
            var picked = await _dialogs.ActionSheetAsync("Font size", "12", "14", "16", "18", "20", "24");
            if (int.TryParse(picked, out var value))
            {
                s.FontSize = value;
            }
        }
    }
}
