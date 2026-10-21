# Phase 3 verification — project wizard, project settings, zip import/export

**STATUS: UNVERIFIED on Android.** None of the MAUI/Android code below has been compiled or run. Only the pure
logic in `Projects/` was compiled (plain .NET 8) and exercised: 565 checks pass, 0 fail (includes all earlier phases).

## What is verified (on .NET 8 console runner, in-memory file system)
- All 7 templates render, create the right files/folders and a valid `sodium.project.json`; name collision and
  invalid names are rejected; a failure mid-creation rolls the half-made folder back.
- Settings JSON round trip, invalid/newer-schema files rejected, entry-point validation, argument parse/format round trip (fuzzed).
- Zip export/import round trip; zip-slip / absolute / backslash / drive-letter entries rejected; `__MACOSX` skipped;
  entry-count, per-file, total-size and depth limits enforced while reading; rollback on failure and cancellation;
  import name collision gets " (2)".
- Three deliberate mutations of the safety code were caught by the tests.

## NOT verified
Everything that touches Android or MAUI: the system file pickers, modal pages, XAML bindings, Picker binding, action sheets.

## Files created
`Projects/*` (ToolchainInfo, ProjectTemplate, ProjectTemplates, ProjectSettings, ArgumentLine, ProjectSettingsStore,
ProjectCreator, ArchiveService), `Storage/IExternalFileService.cs`, `Storage/AndroidExternalFileService.cs`,
`Services/IProjectDialogs.cs`, `Services/ProjectDialogs.cs`, `ViewModels/ExplorerViewModel.Projects.cs`,
`ViewModels/NewProjectViewModel.cs`, `ViewModels/ProjectSettingsViewModel.cs`, `Views/NewProjectPage.xaml(.cs)`,
`Views/ProjectSettingsPage.xaml(.cs)`, `SodiumStudio.Tests/ProjectTests.cs`, `SodiumStudio.Tests/MemoryFileSystem.cs`, this file.

## Files modified
`Platforms/Android/MainActivity.cs` (generic `LaunchPickerAsync`), `ViewModels/ExplorerViewModel.cs` (new actions, services,
hooks, `ArchiveException` catch), `ViewModels/EditorWorkspaceViewModel.cs` (`IsOpen`, `ConfirmSaveBeforeExportAsync`),
`ViewModels/MainViewModel.cs` (wiring), `MauiProgram.cs` (DI), `Resources/Styles/Styles.xaml` (Picker style),
`SodiumStudio.Tests/*` (Program.cs, csproj), `README.md`.

## Behaviour / design decisions
- Project actions operate on a folder chosen in the explorer (⋯ menu or a folder's row menu). The SAF root is not re-rooted.
- Import extracts into a NEW subfolder named after the zip. Export zips the folder contents (no wrapper directory).
- Export asks first about unsaved tabs (save all / export saved versions / cancel).
- Project settings are refused while `sodium.project.json` is open in a tab; an unreadable settings file is never overwritten.
- Templates only create files. Toolchain notes say honestly that this build edits and colors code but does not compile or run it.

## Manual device checklist
1. ⋯ → New project: wizard lists 7 templates; selecting one fills the default name; editing the name then switching template keeps your name.
2. Create each of the 7 templates; files appear in the explorer, `sodium.project.json` exists, files open in the editor.
3. Create a project with an existing name → error, nothing half-created. Try invalid names (`a/b`, empty, `..`).
4. Android back button in the wizard and in settings cancels (no changes).
5. Project settings: change language/entry point/arguments (`a "b c"`), save, reopen — values persist. Entry `../x` and `/x` rejected.
6. Hand-corrupt `sodium.project.json` → settings shows an error and the file is unchanged. Open it in a tab → settings refused.
7. Export: make an unsaved edit, export → prompt appears; "Save all" includes the edit. The system "save" picker appears; unzip the result on a PC.
8. Cancel the save picker → no "Export complete" message.
9. Import: pick a zip made on a PC → new folder named after the zip; import it twice → " (2)" suffix.
10. Import a crafted zip with `../evil.txt` and a `__MACOSX` folder → traversal entry rejected, nothing outside the folder written.
11. Import a zip over 64 MB or with >20,000 entries → clear error, no leftover folder.
12. Rotate / resize while a picker or the wizard is open.

## Known risks (unresolved, unverified)
| # | Risk |
|---|---|
| R1 | `PushModalAsync`/`PopModalAsync` on the first window's page: behaviour with the drawer layout and the back button unverified. |
| R2 | `Picker.SelectedIndex` two-way binding with a compiled-binding page (`x:DataType`) should work but is unverified. |
| R3 | `ACTION_OPEN_DOCUMENT` with `*/*` plus `EXTRA_MIME_TYPES`: some providers may grey out zips or show everything. |
| R4 | Archives are held fully in memory (limit 64 MB); low-RAM devices may fail on large projects. Not streaming. |
| R5 | `OnActivityResult` is deprecated; only one picker can be pending at a time. If Android kills the process during a picker, the action is lost and must be repeated. |
| R6 | Writing via `"wt"` is not atomic; a failed export may leave a partial zip at the chosen destination. |
| R7 | Import is not atomic on the provider side; rollback deletion is best effort and may itself fail on some providers. |
| R8 | `CollectionView` `SelectedItem` binding with `Selected` visual state inside a ScrollView with fixed height (320) is unverified; layout may need tuning on small screens. |
| R9 | All earlier risks (Phase 1 R1–R10, Phase 2 R1–R12) still apply. |

## Limitations
No execution of any project, no toolchain, no import of non-zip formats, no encryption/password zips, UTF-8 text only in settings.
