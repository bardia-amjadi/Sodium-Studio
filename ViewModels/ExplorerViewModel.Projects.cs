using SodiumStudio.Projects;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

// Project-level actions: new project, zip import/export, project settings.
public partial class ExplorerViewModel
{
    private async Task HandleProjectActionAsync(string? action, StorageNode target)
    {
        switch (action)
        {
            case NewProjectAction:
                await NewProjectAsync(target);
                break;
            case ImportArchiveAction:
                await ImportArchiveAsync(target);
                break;
            case ExportArchiveAction:
                await ExportArchiveAsync(target);
                break;
            case ProjectSettingsAction:
                await EditProjectSettingsAsync(target);
                break;
        }
    }

    private async Task NewProjectAsync(StorageNode target)
    {
        if (!target.CanCreateChildren)
        {
            await _dialogs.AlertAsync("Cannot create", $"New items cannot be created inside '{target.Name}'.");
            return;
        }

        var request = await _projectDialogs.ShowNewProjectAsync(target.Name);
        if (request is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var created = await _projectCreator.CreateAsync(target, request.Template, request.Name);
            _expanded.Add(target.DocumentId);
            await ReloadAsync(created.DocumentId);
        });
    }

    private async Task ImportArchiveAsync(StorageNode target)
    {
        if (!target.CanCreateChildren)
        {
            await _dialogs.AlertAsync("Cannot import", $"New items cannot be created inside '{target.Name}'.");
            return;
        }

        ImportResult? result = null;
        await ExecuteAsync(async () =>
        {
            var picked = await _externalFiles.PickFileAsync(_archives.Limits.MaxArchiveBytes);
            if (picked is null)
            {
                return;
            }

            result = await _archives.ImportAsync(target, picked.Name, picked.Content);
            _expanded.Add(target.DocumentId);
            await ReloadAsync(result.Folder.DocumentId);
        });

        if (result is null || HasError)
        {
            return;
        }

        var message = $"Imported {result.FileCount} file(s) into '{result.Folder.Name}'.";
        if (result.Skipped.Count > 0)
        {
            const int shown = 8;
            message += $"{Environment.NewLine}{Environment.NewLine}{result.Skipped.Count} entr{(result.Skipped.Count == 1 ? "y was" : "ies were")} skipped:{Environment.NewLine}"
                       + string.Join(Environment.NewLine, result.Skipped.Take(shown));
            if (result.Skipped.Count > shown)
            {
                message += $"{Environment.NewLine}... and {result.Skipped.Count - shown} more.";
            }
        }

        await _dialogs.AlertAsync("Import complete", message);
    }

    private async Task ExportArchiveAsync(StorageNode folder)
    {
        if (ConfirmExportAsync is not null && !await ConfirmExportAsync())
        {
            return;
        }

        string? summary = null;
        await ExecuteAsync(async () =>
        {
            var exported = await _archives.ExportAsync(folder);
            var saved = await _externalFiles.SaveFileAsync(folder.Name + ".zip", "application/zip", exported.Archive);
            if (saved)
            {
                summary = $"Exported {exported.FileCount} file(s) from '{folder.Name}'.";
            }
        });

        if (summary is not null && !HasError)
        {
            await _dialogs.AlertAsync("Export complete", summary);
        }
    }

    private async Task EditProjectSettingsAsync(StorageNode folder)
    {
        LoadedSettings? loaded = null;
        await ExecuteAsync(async () => loaded = await _settingsStore.LoadAsync(folder));
        if (loaded is null)
        {
            return;
        }

        if (loaded.Error is not null)
        {
            // Never overwrite a file we cannot understand; the user can fix or delete it by hand.
            await _dialogs.AlertAsync(
                "Project settings",
                $"{ProjectSettings.FileName} could not be read, so it was left untouched:{Environment.NewLine}{loaded.Error}");
            return;
        }

        if (loaded.File is not null && IsFileOpen?.Invoke(loaded.File) == true)
        {
            await _dialogs.AlertAsync(
                "Project settings",
                $"{ProjectSettings.FileName} is open in the editor. Close its tab first so the two edits cannot conflict.");
            return;
        }

        if (loaded.File is null && !folder.CanCreateChildren)
        {
            await _dialogs.AlertAsync("Project settings", $"'{folder.Name}' does not allow creating the settings file.");
            return;
        }

        if (loaded.File is not null && !loaded.File.CanWrite)
        {
            await _dialogs.AlertAsync("Project settings", $"{ProjectSettings.FileName} is read-only.");
            return;
        }

        var current = loaded.Settings ?? new ProjectSettings { Name = folder.Name };
        var edited = await _projectDialogs.ShowProjectSettingsAsync(current);
        if (edited is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _settingsStore.SaveAsync(folder, loaded.File, edited);
            await ReloadAsync();
        });
    }
}
