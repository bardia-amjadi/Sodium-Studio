using SodiumStudio.Editing;

namespace SodiumStudio.Projects;

/// <summary>
/// Honest, user-facing statements about what the app can do for each language in THIS build.
/// Mirrors the support matrix in FEASIBILITY.md. Update it only when a toolchain is really integrated.
/// </summary>
public static class ToolchainInfo
{
    public static string Describe(LanguageKind language) => language switch
    {
        LanguageKind.CSharp =>
            "Editing and syntax coloring only. Compiling and running C# is not supported on Android in this build.",
        LanguageKind.Python =>
            "Editing and syntax coloring only. Running Python needs an embedded runtime that is not integrated yet.",
        LanguageKind.Cpp =>
            "Editing and syntax coloring only. Android does not allow running a downloaded compiler, so C++ cannot be built here.",
        LanguageKind.Java =>
            "Editing and syntax coloring only. A Java source compiler is not available in this build.",
        LanguageKind.JavaScript =>
            "Editing and syntax coloring only. Running JavaScript needs an embedded engine that is not integrated yet.",
        _ => "Editing only. No language is selected for this project.",
    };
}
