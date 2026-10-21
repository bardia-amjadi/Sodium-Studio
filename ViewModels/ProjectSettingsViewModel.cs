using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SodiumStudio.Editing;
using SodiumStudio.Projects;

namespace SodiumStudio.ViewModels;

public sealed partial class ProjectSettingsViewModel : ObservableObject
{
    private static readonly LanguageKind[] LanguageOrder =
    {
        LanguageKind.PlainText, LanguageKind.CSharp, LanguageKind.Python,
        LanguageKind.Cpp, LanguageKind.Java, LanguageKind.JavaScript,
    };

    private readonly ProjectSettings _original;
    private readonly Action<ProjectSettings?> _close;

    public ProjectSettingsViewModel(ProjectSettings settings, Action<ProjectSettings?> close)
    {
        _original = settings;
        _close = close;
        Name = settings.Name;
        EntryPoint = settings.EntryPoint;
        ArgumentsLine = ArgumentLine.Format(settings.Arguments);
        var index = Array.IndexOf(LanguageOrder, settings.Language);
        SelectedLanguageIndex = index < 0 ? 0 : index;
    }

    public IReadOnlyList<string> LanguageNames { get; } =
        LanguageOrder.Select(LanguageInfo.DisplayName).ToList();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string entryPoint = string.Empty;

    [ObservableProperty]
    private string argumentsLine = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolchainNote))]
    private int selectedLanguageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string ToolchainNote =>
        ToolchainInfo.Describe(LanguageOrder[Math.Clamp(SelectedLanguageIndex, 0, LanguageOrder.Length - 1)]);

    [RelayCommand]
    private void Save()
    {
        var entry = EntryPoint.Trim().Replace('\\', '/');
        if (!ProjectSettingsSerializer.TryValidateEntryPoint(entry, out var entryError))
        {
            Error = entryError;
            return;
        }

        if (!ArgumentLine.TryParse(ArgumentsLine ?? string.Empty, out var args, out var argError))
        {
            Error = argError;
            return;
        }

        _close(new ProjectSettings
        {
            SchemaVersion = ProjectSettings.CurrentSchemaVersion,
            Name = Name.Trim(),
            TemplateId = _original.TemplateId,
            Language = LanguageOrder[Math.Clamp(SelectedLanguageIndex, 0, LanguageOrder.Length - 1)],
            EntryPoint = entry,
            Arguments = args,
        });
    }

    [RelayCommand]
    private void Cancel() => _close(null);
}
