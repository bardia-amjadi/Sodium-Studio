# Phase 1: Verification Checklist, Risks, Limitations

**Nothing in this document has been verified.** The checklist is for you to run; the risks are
things I could not check because the sandbox could not compile.

## 1. Files created (all new; nothing existed before in `SodiumStudio/`)

Project and app
- `SodiumStudio.csproj`, `MauiProgram.cs`, `App.xaml`, `App.xaml.cs`, `.gitignore`, `README.md`
- `Platforms/Android/MainActivity.cs`, `MainApplication.cs`, `AndroidManifest.xml`
- `Resources/Styles/Colors.xaml`, `Resources/Styles/Styles.xaml`
- `Resources/AppIcon/appicon.svg`, `appiconfg.svg`, `Resources/Splash/splash.svg`

Source
- `Views/MainPage.xaml(.cs)`, `Views/ExplorerView.xaml(.cs)`
- `ViewModels/MainViewModel.cs`, `ExplorerViewModel.cs`, `ExplorerItemViewModel.cs`
- `Services/IDialogService.cs`, `Services/DialogService.cs`
- `Storage/IProjectFileSystem.cs`, `SafProjectFileSystem.cs`, `StorageNode.cs`, `StorageException.cs`, `NameValidator.cs`

Docs
- `docs/PHASE1-VERIFICATION.md` (this file)

Files modified outside `SodiumStudio/`: none (`../FEASIBILITY.md` is unchanged).

## 2. Deviations from FEASIBILITY.md

- **Toolchain decisions: unchanged.** No toolchain, editor, or execution code exists in Phase 1.
- Folder names differ from the sketched layout: `Storage/` is used instead of `FileSystem/`;
  `MauiProgram.cs` is at the project root rather than `App/`; the `Editor/`, `Projects/`,
  `Toolchains/`, `Execution/`, `Settings/` folders are not created yet (they would be empty).
- `IProjectFileSystem.RenameAsync` takes the parent node (needed for the duplicate-name check).

## 3. Manual verification checklist (real device or emulator, Android 12+)

Mark each item pass/fail. Report failures with logcat output (`adb logcat | grep -i sodium`).

Launch and layout
- [ ] App installs and launches with a dark UI, title bar, status bar reading "No folder open".
- [ ] Phone (width < 720 dp): explorer is hidden; the menu button (top-left) opens it as a drawer; tapping the dimmed area closes it; the Back button closes the drawer instead of leaving the app.
- [ ] Tablet / large emulator / Chromebook: explorer is a persistent left pane; no menu button.
- [ ] Rotate the device and resize the window (multi-window, foldable, Chromebook): layout switches between drawer and pane without a crash or lost state.
- [ ] Status bar and navigation bar do not overlap the title bar or status bar (edge-to-edge; see risk R6).

Folder selection
- [ ] "Open Folder" shows the system folder picker; choose a folder (not a storage root or Downloads; Android blocks those).
- [ ] The folder's entries appear: folders first, then files, alphabetical.
- [ ] Cancel the picker: nothing changes, no error.
- [ ] Kill the app and relaunch: the same folder re-opens automatically.
- [ ] Revoke access (Settings > Apps > Sodium Studio > clear data, or remove the folder externally) and relaunch: an error banner appears and "Choose folder" works.

Browsing
- [ ] Tap a folder: it expands in place with indented children; tap again: it collapses.
- [ ] Nested expansion works to at least 3 levels; selection highlight follows the last tapped row.
- [ ] Tap the row's `⋯` button: the action sheet opens and the folder does **not** also toggle.
- [ ] Empty folder (create one outside the app first): the root shows "This folder is empty"; an expanded empty subfolder shows no children.
- [ ] Folder with 500+ files: it lists without freezing the UI (spinner visible); note the time.
- [ ] Refresh (⟳) keeps expanded folders expanded and picks up changes made outside the app (use a file manager to add a file first).

Create / rename / delete
- [ ] New file with nothing selected: created in the root. With a folder selected: created inside it, and the folder is expanded with the new file selected.
- [ ] New file with a file selected: created in that file's parent.
- [ ] New folder works the same way.
- [ ] Creating `notes.txt`, `main.py`, `Program.cs`, `README` (no extension): each appears with exactly the typed name (no extra `.txt` or similar appended).
- [ ] Creating a name that already exists (try different case): error message, no duplicate like `name (1)`.
- [ ] Invalid names (`a/b`, `a:b`, empty, `..`): rejected with a message before reaching storage.
- [ ] Rename file and folder: new name shown, selection kept; renaming to an existing name fails with a message; a rename that changes only the case behaves sensibly.
- [ ] Delete file: confirmation shown, Cancel does nothing, Delete removes it.
- [ ] Delete folder with contents: the confirmation mentions the contents; everything is removed.
- [ ] Verify results with a separate file manager app.

Errors and permissions
- [ ] Remove the folder externally while the app shows it, then Refresh: a readable error appears (not a crash).
- [ ] A subfolder that fails to list (e.g. deleted externally while expanded): that folder collapses and a message names it; other content stays.
- [ ] Try the app with a folder on an SD card / USB / cloud provider (Google Drive via the picker) if available: browsing works or fails with a clear message; note provider behavior.
- [ ] The manifest declares no storage permissions and the app never prompts for any.

## 4. Known compilation risks and unresolved API questions

None of these have been checked against a compiler. Roughly ordered by likelihood.

| # | Risk / question | Where | What to do if it fails |
|---|---|---|---|
| R1 | `DisplayAlert` / `DisplayActionSheet` are expected to be obsolete in .NET 10 (replaced by `DisplayAlertAsync` / `DisplayActionSheetAsync`). The old names are used with `#pragma warning disable CS0618`. If they were removed, compile fails. | `Services/DialogService.cs` | Switch to the `*Async` names. |
| R2 | `OnActivityResult` is deprecated on newer API levels; the override may produce warnings, or be flagged as hiding a different signature. The folder picker depends on it. | `Platforms/Android/MainActivity.cs` | Move to the AndroidX Activity Result API (`RegisterForActivityResult` with `OpenDocumentTree`) and register in `OnCreate`. |
| R3 | Android binding names/signatures not verified: `DocumentsContract.BuildChildDocumentsUriUsingTree`, `BuildDocumentUriUsingTree`, `GetTreeDocumentId`, `GetDocumentId`, `CreateDocument`, `RenameDocument`, `DeleteDocument`; `ContentResolver.TakePersistableUriPermission`, `ReleasePersistableUriPermission`, `PersistedUriPermissions`; `UriPermission.Uri/IsReadPermission/IsWritePermission`; `MimeTypeMap.Singleton`. Column names and flags were deliberately hard-coded as literals to reduce this risk. | `Storage/SafProjectFileSystem.cs` | Fix names against IntelliSense for the installed Android API bindings. |
| R4 | CommunityToolkit.Mvvm 8.4.0 field-based `[ObservableProperty]` with multi-argument `[NotifyPropertyChangedFor]` / `[NotifyCanExecuteChangedFor]`, and the generated `partial void OnSelectedItemChanged(old, new)` hook. Newer toolkit analyzers may suggest partial properties (warnings only). Version 8.4.0 itself was not confirmed against NuGet. | ViewModels | Adjust the version to the latest 8.x on NuGet; convert to partial properties if the analyzer errors. |
| R5 | `Microsoft.Maui.ApplicationModel.Platform` is written fully qualified to avoid the `Platform` ambiguity; global usings (`Application`, `Preferences`) are assumed from MAUI implicit usings. | Storage, Services | Add explicit `using` lines. |
| R6 | Android 15/16 edge-to-edge: the .NET 10 MAUI safe-area behavior for `ContentPage` was not verified. Content may draw under the status/navigation bars. | `Views/MainPage.xaml` | Set the page's safe-area handling (the .NET 10 `SafeAreaEdges` property, if present) or add insets padding in `MainPage`. |
| R7 | `Maui.SplashTheme` is referenced by `MainActivity`; it is expected to ship with MAUI, and a splash SVG is included, but this was not confirmed. | `MainActivity`, `.csproj` | Add the splash/style resources from the MAUI template. |
| R8 | Single-target `net10.0-android` project: `Platforms/Android/**` is expected to compile via MAUI SDK default globbing; not confirmed. | `.csproj` | Add explicit `<Compile Include>` or switch to a multi-target layout. |
| R9 | MAUI `TapGestureRecognizer` inside `CollectionView` row, and `DataTrigger` on `Grid`, with compiled bindings: behavior on Android not verified. | `Views/ExplorerView.xaml` | Use `SelectionMode="Single"` or a `Border`/`ContentView` host for the gesture. |
| R10 | `OnBackButtonPressed` may not be called with Android predictive back on newer API levels. | `Views/MainPage.xaml.cs` | Register an `OnBackPressedCallback` in `MainActivity`. |

## 5. Known limitations (by design in Phase 1)

- No editor, build, run, debug, project templates, import/export, or settings page; the workspace says so explicitly.
- Rename: expansion state of descendants of a renamed folder is not preserved (document ids may change).
- The tree is rebuilt on every refresh/mutation using one `ObservableCollection` update per row; very large folders may be slow (to be measured in the 500+ files check, a virtualized/batched approach is a follow-up).
- SAF restrictions: Android will not let the user pick a storage root, `Download`, or `Android/data`; providers may not support rename/delete/create (the UI hides those actions using the provider's flags).
- Rename/delete/create operations are not atomic across providers; a failure mid-way is reported but not rolled back.
- Row actions use a `⋯` menu; right-click context menus and keyboard shortcuts (F2/Delete/F5) for Chromebooks are not implemented (MAUI keyboard accelerators are not available on Android).
- Icons are emoji/text glyphs, not final artwork; the app icon is a placeholder.
- No unit tests: `NameValidator` could be tested without Android, but no test project exists yet.
