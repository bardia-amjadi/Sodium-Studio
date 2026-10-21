# Phase 2: Editor. Verification Checklist, Risks, Limitations

## 0. What is verified and what is not

| Part | Status |
|---|---|
| Tokenizer (`Editing/SyntaxHighlighter.cs`), language detection, text search (`Editing/TextSearch.cs`), file codec (`Storage/TextFileCodec.cs`), name validation | **Compiled and tested** on a plain .NET 8 console runner: 135 checks, 0 failures, including randomized fuzz tests (token invariants, search vs a naive reference). Command: `cd SodiumStudio.Tests && dotnet run -c Release`. |
| Everything MAUI/Android: views, view models, SAF read/write, CodeEdit.Maui integration | **Never compiled, never run.** The MAUI workload and NuGet are unavailable in the sandbox. Only static checks were done (XML well-formedness, XAML bindings resolve to view-model members, event handlers exist). |

The unit tests prove the *logic* is right in isolation. They say nothing about whether the editor control behaves as assumed on a device.

## 1. Scope of Phase 2

Implemented (unverified on device): open files from the explorer into tabs; one `CodeEditor` control whose
`Document` is swapped per tab (each tab keeps its own undo history); dirty indicator; Save, Save All, Save As
(same folder, never overwrites); close with save/discard prompt; undo/redo; find/next/previous/match-case and
replace/replace-all; syntax coloring for C#, Python, C++, Java, JavaScript; editor options (word wrap, line
numbers, tabs vs spaces, indent size, font size, persisted); status bar (line/column, encoding, line ending, language,
read-only); protection of unsaved work when the project folder is changed or closed; handling of files deleted or
renamed outside the editor.

Not implemented (see section 5): bracket matching, completion, error indicators, keyboard shortcuts, other encodings,
HTML/CSS/JSON highlighting, draft recovery after process death.

## 2. Files

New
- `Editing/LanguageKind.cs`, `Editing/SyntaxHighlighter.cs`, `Editing/TextSearch.cs`, `Editing/EditorSettings.cs`
- `Storage/TextFileCodec.cs`
- `ViewModels/EditorTabViewModel.cs`, `ViewModels/EditorWorkspaceViewModel.cs`
- `Views/EditorView.xaml`, `Views/EditorView.xaml.cs`
- `docs/PHASE2-VERIFICATION.md`
- `../SodiumStudio.Tests/` (`SodiumStudio.Tests.csproj`, `Program.cs`): pure-logic test runner, outside the app project

Modified
- `SodiumStudio.csproj` (adds `CodeEdit.Maui` 0.5.0-preview.2), `MauiProgram.cs` (`UseCodeEditor()`, new DI registrations)
- `Storage/StorageNode.cs` (adds `CanWrite`), `Storage/IProjectFileSystem.cs` and `Storage/SafProjectFileSystem.cs` (read, write, exists)
- `ViewModels/ExplorerViewModel.cs` (file-open, rename, delete and root-change events; confirmation hook), `ViewModels/MainViewModel.cs` (wires explorer and editor)
- `Views/MainPage.xaml`, `Views/MainPage.xaml.cs` (editor in the workspace area, status details), `Resources/Styles/Styles.xaml` (Entry style)
- `README.md`

## 3. Deviations from FEASIBILITY.md (and why)

- **Toolchain decisions: unchanged.** No execution, build or diagnostics code was added.
- **No `ICodeEditor` interface.** The report proposed one. The control's real API was only readable from its README, not compiled, so an interface designed now would be a guess. The coupling is limited to `EditorTabViewModel` (holds a `CodeDocument`), `EditorWorkspaceViewModel` (none) and `Views/EditorView` (the only file touching the control). Replacing the control would change those two files. Introduce the interface if the spike below fails.
- **Folder is `Editing/`, not `Editor/`.** A namespace named `SodiumStudio.Editor` would shadow MAUI's `Editor` control type.
- **The editor spike in the report ("one day on a real device") has not happened.** This phase is written against the published README only. The checklist in section 4 *is* the spike.

## 4. Manual verification checklist (real device or emulator, Android 12+)

Do the Phase 1 checklist first. Mark pass/fail; for failures attach `adb logcat` output.

Opening and tabs
- [ ] Tapping a `.cs`, `.py`, `.cpp`, `.java`, `.js` and `.txt` file opens it in a tab with its text; the tab shows the file name.
- [ ] Tapping the same file again switches to the existing tab (no duplicate). Double-tapping quickly opens one tab.
- [ ] Phone layout: opening a file closes the explorer drawer and shows the editor.
- [ ] Open 5+ files: the tab strip scrolls horizontally; selecting tabs swaps content correctly.
- [ ] **Switching tabs does not mark a clean tab dirty** (no dot after switching).
- [ ] A binary file (e.g. a `.png`), a non-UTF-8 file and a file > 2 MB each show a clear message and do not open.
- [ ] A CRLF file and an LF file both open without a stray `^M` or doubled blank lines.

Editing and saving
- [ ] Typing shows the on-screen keyboard; typing, deleting, pasting and selecting work; line numbers show.
- [ ] Editing marks the tab dirty (dot). Save (💾) clears it; the file on disk (check with a file manager or by reopening) has the edit.
- [ ] Save keeps the original line ending (CRLF stays CRLF), BOM (if any), and UTF-8 non-ASCII text (try `é` and an emoji).
- [ ] Shrinking a file and saving leaves no stale text at the end (truncate check).
- [ ] Save As: new name in the same folder works, tab renames, explorer shows the new file. An existing name is refused with a message and nothing is overwritten.
- [ ] Save As failing (e.g. invalid name) leaves no empty file behind.
- [ ] Close a dirty tab: Save / Discard changes / cancel (tap outside) all behave.
- [ ] Save All saves every dirty tab.
- [ ] Undo and redo work, and each tab has its own history (undo in tab A does not touch tab B).
- [ ] Read-only file (if you can create one): tab opens read-only; typing is blocked; Save explains why.

Explorer interaction
- [ ] Rename an open file in the explorer: the tab title updates; saving still writes to it.
- [ ] Delete a clean open file: its tab closes. Delete a dirty open file: the tab stays and shows "(deleted)"; Save asks for a new name.
- [ ] Rename a folder containing an open clean file: the tab closes or stays valid; no crash. With unsaved edits: the tab is kept.
- [ ] Open another folder (or Close folder) with unsaved edits: you are asked Save all / Discard / cancel; cancel keeps everything.

Find and replace
- [ ] 🔍 opens the bar. Next/Previous move through matches, wrap around, and scroll the match into view; "No matches" shows when appropriate.
- [ ] The selected match is visibly highlighted **even though the search box has focus** (see R7).
- [ ] Match-case toggle changes results. Replace: first tap selects, second replaces and moves on. Replace All reports a count and results are correct, including when the replacement contains the search text.
- [ ] After Replace All, Undo reverts (note how many taps; see R5).

Highlighting
- [ ] Each language shows distinct colors for keywords, strings, comments, numbers (and `#include`/`#region` lines). Plain text files show the default light color (not black on dark).
- [ ] Typing keeps working smoothly; colors update about 0.2 s after you stop typing; no flicker storm.
- [ ] Multi-line constructs (block comments, Python triple quotes, JS template strings, C# verbatim strings) color correctly.
- [ ] A 250 KB source file stays usable (note typing lag). A file > 300,000 characters opens without coloring.
- [ ] Text typed inside a comment or string is colored as such after the pass.

Options and status bar
- [ ] Options (⋯): word wrap, line numbers, tabs/spaces, indent size and font size each change the editor immediately and persist after restarting the app.
- [ ] Tab key / auto-indent respect tabs vs spaces and indent size.
- [ ] Status bar shows `Ln x, Col y`, encoding, line ending and language, and updates as the caret moves and tabs change.

Robustness
- [ ] Rotate / resize while a file with unsaved edits is open: edits and caret survive.
- [ ] Put the app in the background and return: the open tabs and unsaved edits are still there (see R8 for process death).

## 5. Known compilation risks and unresolved API questions

All CodeEdit.Maui names below come from its README (0.5.0-preview.2), not from a compiler.

| # | Risk / question | Where | If it fails |
|---|---|---|---|
| R1 | `builder.UseCodeEditor()` namespace and chaining; `CodeEditor` members used: `Document`, `IsReadOnly`, `IndentSize`, `UseTabs`, `WordWrap`, `ShowLineNumbers`, `FontSize`, `SelectedRange`, `Selection.ReplaceText`, `CaretPosition` (`.Line`, `.Column` property names are **guessed**), `GetOffset`, `ScrollIntoView`, `Undo`, `Redo`, `Decorations.CreateLayer()`; events `TextChanged` and `SelectionChanged` (subscribed with 2-parameter lambdas, so delegate shape does not matter). | `MauiProgram.cs`, `Views/EditorView.xaml.cs` | Fix names against IntelliSense; the API reference is at https://hez2010.github.io/RichEdit.Maui/api/index.html. |
| R2 | Types `RichTextRange(start, length)`, `RichTextDecoration`, `RichTextDecorationStyle { ForegroundColor }`; `CodeDocument.FromPlainText`, `.Text`, `.CurrentSnapshot.Text`/`.Revision`; `layer.TrySet(revision, decorations)` taking an **array** (a `List<>` or span parameter would not compile); the layer must implement `IDisposable` (README says it has `Dispose()`; the interface is assumed). | `EditorView.xaml.cs`, `EditorTabViewModel.cs` | Adjust the call; if the layer is not `IDisposable`, store it in a field of its real type. |
| R3 | NuGet: CodeEdit.Maui needs `Microsoft.Maui.Controls >= 10.0.101`. If the installed workload provides an older MAUI, restore may fail (NU1605/NU1608). The package is a preview with few downloads and is the riskiest dependency. | `SodiumStudio.csproj` | Update the workload or set `<MauiVersion>` to at least 10.0.101. |
| R4 | The control's default text color and background on a dark theme are unknown. The highlighter colors **all** text (including uncolored gaps) with `#D4D4D4` to avoid dark-on-dark, and the page background is dark, but if the control paints its own white background, text will be unreadable. Also: during the 0.2 s before a recolor, freshly typed text uses the control default. | `EditorView` | Look for a theme/color property on the control or its handler. |
| R5 | Replace All performs one `ReplaceText` per match, so it may take many Undo taps and be slow for thousands of matches. The README mentions `Document.Edit(edit => edit.InsertText(...))`; a batched replace in the edit builder would give one undo step, but its name is unverified. | `OnReplaceAllClicked` | Switch to a single `Document.Edit` once the API is confirmed. |
| R6 | Dirty tracking assumes assigning `CodeBox.Document` raises its change events **synchronously** inside the `_swappingDocument` window. If the control raises them later, switching tabs would wrongly mark tabs dirty (visible as a dot, harmless but wrong). | `EditorView.ShowTab` | Compare a document revision instead of a flag. |
| R7 | The native selection highlight may not render while the Find entry holds focus. | `SelectMatch` | Move focus to the editor after selecting, accepting the keyboard flicker. |
| R8 | **Data-loss risk:** edits exist only in memory until saved. If Android kills the process (low memory, swipe-away), unsaved edits are lost. There is no autosave or draft recovery in this phase by design (silent writes to project files would be a behavior decision for you to make). | whole editor | Recommended follow-up: store dirty buffers in app-private storage on pause and offer recovery on launch. |
| R9 | Save is not atomic: it truncates and rewrites the file (`"wt"` mode). A provider failure mid-write could leave a truncated file; the tab stays dirty so the content is still in memory, and the written size is checked afterwards when the provider reports it. Some providers may ignore `"wt"`. | `SafProjectFileSystem.WriteAllBytesAsync` | Verify with the "shrink and save" check above. |
| R10 | A `CodeDocument` "can attach to only one editor at a time". After the last tab closes, the closed tab's document stays attached to the (hidden) control until the next tab is shown. Assumed harmless. | `EditorView.ShowTab` | Assign a fresh empty document. |
| R11 | `TapGestureRecognizer` on a `HorizontalStackLayout` inside a horizontal `CollectionView` (tab selection), and the nested close button not triggering selection, are unverified (same family as Phase 1 R9). | `EditorView.xaml` | Wrap in a `Grid`/`Border` host. |
| R12 | The Phase 1 risks R1-R10 still apply. | | |

## 6. Known limitations (by design in Phase 2)

- Syntax coloring is lexical only (keywords, strings, comments, numbers, preprocessor lines): no parsing, types or errors. Known approximations: JavaScript regex literals, C++ raw strings and nested `${}`/`$""` interpolation are not modelled.
- No bracket matching, code completion, error indicators or debugging. Error indicators arrive with diagnostics in Phase 4.
- No keyboard shortcuts (Ctrl+S, Ctrl+F, ...): MAUI keyboard accelerators do not apply on Android; the control's own key hooks could be used later.
- Text files only, UTF-8 only (BOM preserved), at most 2 MB; other encodings are refused, not guessed. Mixed line endings are normalized to the file's dominant style on save (the user is told on open). A UTF-8 file with invalid bytes is never opened, so it cannot be corrupted.
- Undoing back to the saved text leaves the tab marked dirty (conservative).
- Save As works inside the same folder only and never overwrites; exporting a copy elsewhere arrives with import/export in Phase 3.
- Only `.cs .py .c/.cpp/.h(pp) .java .js(.mjs .cjs .jsx)` are colored. HTML, CSS and JSON files open as plain text until the Phase 3 templates need them.
- Files over 300,000 characters are edited without coloring.
