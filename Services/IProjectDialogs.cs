using SodiumStudio.Projects;

namespace SodiumStudio.Services;

/// <summary>What the user chose in the new-project wizard.</summary>
public sealed record NewProjectRequest(ProjectTemplate Template, string Name);

/// <summary>Full-page dialogs for project work. Kept behind an interface so view models stay free of UI types.</summary>
public interface IProjectDialogs
{
    /// <summary>Shows the new-project wizard. Returns null if cancelled.</summary>
    Task<NewProjectRequest?> ShowNewProjectAsync(string locationName);

    /// <summary>Shows the settings editor for <paramref name="settings"/>. Returns the edited copy, or null if cancelled.</summary>
    Task<ProjectSettings?> ShowProjectSettingsAsync(ProjectSettings settings);
}
