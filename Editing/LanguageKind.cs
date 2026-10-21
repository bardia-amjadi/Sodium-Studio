namespace SodiumStudio.Editing;

public enum LanguageKind
{
    PlainText,
    CSharp,
    Python,
    Cpp,
    Java,
    JavaScript,
}

public static class LanguageInfo
{
    public static LanguageKind FromFileName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
        {
            return LanguageKind.PlainText;
        }

        return fileName[(dot + 1)..].ToLowerInvariant() switch
        {
            "cs" => LanguageKind.CSharp,
            "py" or "pyw" => LanguageKind.Python,
            "c" or "h" or "cc" or "cpp" or "cxx" or "hh" or "hpp" or "hxx" => LanguageKind.Cpp,
            "java" => LanguageKind.Java,
            "js" or "mjs" or "cjs" or "jsx" => LanguageKind.JavaScript,
            _ => LanguageKind.PlainText,
        };
    }

    public static string DisplayName(LanguageKind language) => language switch
    {
        LanguageKind.CSharp => "C#",
        LanguageKind.Python => "Python",
        LanguageKind.Cpp => "C++",
        LanguageKind.Java => "Java",
        LanguageKind.JavaScript => "JavaScript",
        _ => "Plain Text",
    };
}
