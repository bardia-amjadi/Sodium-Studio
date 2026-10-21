using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SodiumStudio.Projects;
using SodiumStudio.Services;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

public sealed partial class NewProjectViewModel : ObservableObject
{
    private readonly Action<NewProjectRequest?> _close;
    private bool _nameEditedByUser;
    private string? _lastDefaultName;

    public NewProjectViewModel(string locationName, Action<NewProjectRequest?> close)
    {
        _close = close;
        LocationName = locationName;
        SelectedTemplate = Templates[0];
    }

    public string LocationName { get; }

    public string Heading => $"New project in '{LocationName}'";

    public IReadOnlyList<ProjectTemplate> Templates { get; } = ProjectTemplates.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolchainNote), nameof(Description))]
    private ProjectTemplate? selectedTemplate;

    [ObservableProperty]
    private string projectName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string ToolchainNote => SelectedTemplate?.ToolchainNote ?? string.Empty;

    public string Description => SelectedTemplate?.Description ?? string.Empty;

    partial void OnSelectedTemplateChanged(ProjectTemplate? oldValue, ProjectTemplate? newValue)
    {
        // Follow the template's default name until the user types their own.
        if (newValue is not null && (!_nameEditedByUser || ProjectName.Length == 0))
        {
            _lastDefaultName = newValue.DefaultName;
            ProjectName = newValue.DefaultName;
            _nameEditedByUser = false;
        }
    }

    partial void OnProjectNameChanged(string value)
    {
        if (value != _lastDefaultName)
        {
            _nameEditedByUser = true;
        }

        Error = null;
    }

    [RelayCommand]
    private void Create()
    {
        if (SelectedTemplate is null)
        {
            Error = "Choose a template.";
            return;
        }

        var name = ProjectName.Trim();
        if (!NameValidator.TryValidate(name, out var problem))
        {
            Error = problem;
            return;
        }

        _close(new NewProjectRequest(SelectedTemplate, name));
    }

    [RelayCommand]
    private void Cancel() => _close(null);
}
