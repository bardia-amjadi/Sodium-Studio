# Sodium Studio

An offline-first, lightweight, Visual Studio-inspired code editor and project manager for Android, written in C# with .NET MAUI.

> **Project status: early development, not yet verified on a device.**
> The source is complete through Phase 3, but the Android app has not yet been built or run on a phone or emulator.
> Expect compile errors and bugs until the first successful build. Nothing in this README is a promise that a feature works on your device.

## What it is (and is not)

Sodium Studio is a **code editor and project workspace**. It is *not* yet a build or run environment.

| | |
|---|---|
| Platform | Android 12+ (API 31+) |
| Stack | C#, .NET 10, .NET MAUI (`net10.0-android`), MVVM |
| Storage | Android Storage Access Framework (SAF) — you pick a folder; no broad storage permission is requested |
| Network | None required. The app works offline and never compiles in the cloud |
| Editor | [CodeEdit.Maui](https://github.com/hez2010/RichEdit.Maui) (preview) |

## Features in the source

- Dark, responsive layout (drawer on phones, side panel on tablets)
- Project explorer on top of SAF: browse, create, rename, delete, refresh, with clear permission and error states
- Tabbed editor: open, save, save as, undo/redo, find and replace, unsaved-changes protection
- Syntax coloring for C#, Python, C++, Java and JavaScript
- New-project wizard with 7 templates: C# console, Python script, C++ console, Java console, JavaScript script, Node.js project, HTML/CSS/JS website
- Per-project settings stored in `sodium.project.json` (name, language, entry point, arguments)
- Zip import and export with zip-slip protection and size limits

## What it does not do yet

- **No build, run or debug.** Templates only create files. The app does not compile or execute your code.
- Android forbids running binaries from an app's own storage, so running code will only be possible for runtimes that can live inside the app. JavaScript and HTML are the first planned targets; Python is an experiment; C#, Java and C++ will be reported honestly as unsupported rather than faked. See [`FEASIBILITY.md`](FEASIBILITY.md).
- UTF-8 text files only, up to 2 MB per file.
- No debugger, IntelliSense, Git or terminal.

## Getting the APK

### From GitHub Actions
1. Open the **Actions** tab → **Build Android APK** → pick a run (or press **Run workflow**).
2. Download the **SodiumStudio-apk** artifact and unzip it.
3. Copy the `.apk` to your phone and install it (you must allow "install unknown apps" for your file manager).

The APK is signed with a debug key. It is intended for testing, not for the Play Store.

### Build locally
Requirements: .NET 10 SDK, JDK 17+, Android SDK.

```bash
dotnet workload install maui-android

cd SodiumStudio
dotnet publish -f net10.0-android -c Release \
  -p:AndroidPackageFormat=apk -p:RuntimeIdentifier=android-arm64
```

The APK is written under `SodiumStudio/bin/Release/net10.0-android/`. Install it with `adb install -r <file>.apk`.
If the Android SDK is not found, add `-p:AndroidSdkDirectory=<path>`.

If the app installs but crashes on launch, try adding `-p:PublishTrimmed=false`.

## Tests

The platform-independent logic (syntax tokenizer, text search, file encoding, name validation, project templates, settings, zip import/export) has a plain console test runner that needs no Android tooling:

```bash
cd SodiumStudio.Tests
dotnet run -c Release     # expects: 565 checks passed, 0 failed
```

These tests do **not** cover any MAUI or Android code.

## Repository layout

```
SodiumStudio/          the .NET MAUI Android app
  Views/ ViewModels/   UI and MVVM
  Storage/             SAF file system, text encoding, name validation
  Editing/             language detection, syntax highlighter, search, editor settings
  Projects/            templates, project creation, settings, zip import/export
  Services/            dialogs
  docs/                per-phase verification notes and device checklists
SodiumStudio.Tests/    console test runner for the pure logic
FEASIBILITY.md         feasibility, architecture and risk report
.github/workflows/     APK build
```

## Design rules

- No fake or placeholder functionality.
- Never claim a build or run succeeded when it did not; unsupported toolchains are reported as unsupported.
- Respect scoped storage: the app only touches the folder you choose.
- Never execute project code on open.
- Never silently discard unsaved changes.

## Contributing and reporting problems

Early builds are expected to fail. If the build or the app breaks, please open an issue with the full build log or the Android crash log (`adb logcat`). The manual device checklists in `SodiumStudio/docs/` are the best way to test.

## License

No license has been chosen yet. Until one is added, all rights are reserved by the author.
