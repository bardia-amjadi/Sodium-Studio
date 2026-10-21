using CodeEdit.Maui;
using SodiumStudio.Editing;
using SodiumStudio.Projects;
using SodiumStudio.Services;
using SodiumStudio.Storage;
using SodiumStudio.ViewModels;
using SodiumStudio.Views;

namespace SodiumStudio;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.UseCodeEditor(); // CodeEdit.Maui handlers (Phase 2)

        // The project is Android-only (see FEASIBILITY.md), so the SAF implementation
        // is registered directly instead of behind per-platform #if blocks.
        builder.Services.AddSingleton<IProjectFileSystem, SafProjectFileSystem>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<EditorSettings>();
        builder.Services.AddSingleton<IExternalFileService, AndroidExternalFileService>();
        builder.Services.AddSingleton<IProjectDialogs, ProjectDialogs>();
        builder.Services.AddSingleton<ProjectSettingsStore>();
        builder.Services.AddSingleton<ProjectCreator>();
        builder.Services.AddSingleton(sp => new ArchiveService(sp.GetRequiredService<IProjectFileSystem>()));

        builder.Services.AddSingleton<ExplorerViewModel>();
        builder.Services.AddSingleton<EditorWorkspaceViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
