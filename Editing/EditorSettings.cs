using Microsoft.Maui.Storage;

namespace SodiumStudio.Editing;

/// <summary>
/// User editor preferences, stored in app preferences (app configuration, kept apart from project data).
/// </summary>
public sealed class EditorSettings
{
    private const string Prefix = "editor.";

    public event Action? Changed;

    public int IndentSize
    {
        get => Math.Clamp(Preferences.Default.Get(Prefix + nameof(IndentSize), 4), 1, 16);
        set => Set(nameof(IndentSize), Math.Clamp(value, 1, 16));
    }

    public bool UseTabs
    {
        get => Preferences.Default.Get(Prefix + nameof(UseTabs), false);
        set => Set(nameof(UseTabs), value);
    }

    public bool WordWrap
    {
        get => Preferences.Default.Get(Prefix + nameof(WordWrap), false);
        set => Set(nameof(WordWrap), value);
    }

    public bool ShowLineNumbers
    {
        get => Preferences.Default.Get(Prefix + nameof(ShowLineNumbers), true);
        set => Set(nameof(ShowLineNumbers), value);
    }

    public int FontSize
    {
        get => Math.Clamp(Preferences.Default.Get(Prefix + nameof(FontSize), 14), 10, 28);
        set => Set(nameof(FontSize), Math.Clamp(value, 10, 28));
    }

    private void Set<T>(string name, T value)
    {
        Preferences.Default.Set(Prefix + name, value);
        Changed?.Invoke();
    }
}
