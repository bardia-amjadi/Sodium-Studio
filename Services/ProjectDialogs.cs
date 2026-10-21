using SodiumStudio.Projects;
using SodiumStudio.ViewModels;
using SodiumStudio.Views;

namespace SodiumStudio.Services;

/// <summary>Shows the wizard and settings pages modally and waits for the user's answer.</summary>
public sealed class ProjectDialogs : IProjectDialogs
{
    private static INavigation Navigation =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
        ?? throw new InvalidOperationException("No page is available to show a dialog.");

    public Task<NewProjectRequest?> ShowNewProjectAsync(string locationName) =>
        ShowAsync<NewProjectRequest>(close => new NewProjectPage(new NewProjectViewModel(locationName, close)));

    public Task<ProjectSettings?> ShowProjectSettingsAsync(ProjectSettings settings) =>
        ShowAsync<ProjectSettings>(close => new ProjectSettingsPage(new ProjectSettingsViewModel(settings, close)));

    private static async Task<T?> ShowAsync<T>(Func<Action<T?>, Page> createPage) where T : class
    {
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigation = Navigation;
        var page = createPage(result => completion.TrySetResult(result));

        await navigation.PushModalAsync(page);
        var answer = await completion.Task;
        await navigation.PopModalAsync();
        return answer;
    }
}
