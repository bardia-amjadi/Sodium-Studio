namespace SodiumStudio.Services;

// DisplayAlert/DisplayActionSheet are marked obsolete in .NET 10 in favour of *Async variants.
// The older names exist in every MAUI version, so they are used here and the warning is
// suppressed. See docs/PHASE1-VERIFICATION.md (compilation risks).
#pragma warning disable CS0618
public sealed class DialogService : IDialogService
{
    private static Page CurrentPage =>
        Application.Current?.Windows.FirstOrDefault()?.Page
        ?? throw new InvalidOperationException("No page is available to show a dialog.");

    public Task AlertAsync(string title, string message) =>
        CurrentPage.DisplayAlert(title, message, "OK");

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        CurrentPage.DisplayAlert(title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string initialValue = "") =>
        CurrentPage.DisplayPromptAsync(title, message, accept, "Cancel", initialValue: initialValue, maxLength: 255);

    public async Task<string?> ActionSheetAsync(string title, params string[] actions)
    {
        const string cancel = "Cancel";
        var choice = await CurrentPage.DisplayActionSheet(title, cancel, null, actions);
        return choice is null or cancel ? null : choice;
    }
}
#pragma warning restore CS0618
