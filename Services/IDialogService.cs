namespace SodiumStudio.Services;

/// <summary>Thin wrapper over the page dialogs so view models stay free of UI types.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <summary>Returns the entered text, or null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string accept, string initialValue = "");

    /// <summary>Returns the chosen action, or null if cancelled.</summary>
    Task<string?> ActionSheetAsync(string title, params string[] actions);
}
