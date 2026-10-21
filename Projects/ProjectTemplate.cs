using SodiumStudio.Editing;

namespace SodiumStudio.Projects;

/// <param name="Path">Relative path using '/'. May contain template placeholders.</param>
/// <param name="Content">File text (LF). May contain template placeholders.</param>
public sealed record TemplateFile(string Path, string Content);

/// <param name="ToolchainNote">What this build can and cannot do with the project (shown in the wizard).</param>
/// <param name="EntryPoint">Relative path of the main file, recorded in the project settings.</param>
public sealed record ProjectTemplate(
    string Id,
    string DisplayName,
    string Description,
    string DefaultName,
    LanguageKind Language,
    string EntryPoint,
    string ToolchainNote,
    IReadOnlyList<TemplateFile> Files);
