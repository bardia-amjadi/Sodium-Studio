using System.Net;
using System.Text;
using SodiumStudio.Editing;

namespace SodiumStudio.Projects;

/// <summary>
/// The built-in templates and the placeholder renderer.
/// Placeholders: {{ProjectName}} (as typed), {{ProjectNameHtml}} (HTML-encoded),
/// {{Identifier}} (ASCII identifier safe for C#, C++ and CMake), {{PackageName}} (npm-style name).
/// Project names are validated beforehand (no quotes, backslashes, slashes, control characters),
/// so {{ProjectName}} is safe inside string literals and JSON strings.
/// </summary>
public static class ProjectTemplates
{
    public static IReadOnlyList<ProjectTemplate> All { get; } = new[]
    {
        new ProjectTemplate(
            "csharp-console", "C# Console", "A .NET console application with a Program.cs entry point.",
            "HelloCSharp", LanguageKind.CSharp, "Program.cs", ToolchainInfo.Describe(LanguageKind.CSharp),
            new[]
            {
                new TemplateFile("Program.cs", """
                    // {{ProjectName}}
                    Console.WriteLine("Hello from {{ProjectName}}!");
                    """),
                new TemplateFile("{{Identifier}}.csproj", """
                    <Project Sdk="Microsoft.NET.Sdk">

                      <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net10.0</TargetFramework>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <Nullable>enable</Nullable>
                        <RootNamespace>{{Identifier}}</RootNamespace>
                      </PropertyGroup>

                    </Project>
                    """),
                Readme("C# Console"),
            }),

        new ProjectTemplate(
            "python-script", "Python Script", "A Python 3 script with a main() function.",
            "HelloPython", LanguageKind.Python, "main.py", ToolchainInfo.Describe(LanguageKind.Python),
            new[]
            {
                new TemplateFile("main.py", """
                    def main() -> None:
                        print("Hello from {{ProjectName}}!")


                    if __name__ == "__main__":
                        main()
                    """),
                Readme("Python Script"),
            }),

        new ProjectTemplate(
            "cpp-console", "C++ Console", "A C++17 console program with a CMake file.",
            "HelloCpp", LanguageKind.Cpp, "main.cpp", ToolchainInfo.Describe(LanguageKind.Cpp),
            new[]
            {
                new TemplateFile("main.cpp", """
                    #include <iostream>

                    int main() {
                        std::cout << "Hello from {{ProjectName}}!" << std::endl;
                        return 0;
                    }
                    """),
                new TemplateFile("CMakeLists.txt", """
                    cmake_minimum_required(VERSION 3.16)
                    project({{Identifier}} LANGUAGES CXX)

                    set(CMAKE_CXX_STANDARD 17)
                    set(CMAKE_CXX_STANDARD_REQUIRED ON)

                    add_executable({{Identifier}} main.cpp)
                    """),
                Readme("C++ Console"),
            }),

        new ProjectTemplate(
            "java-console", "Java Console", "A Java console program with a Main class.",
            "HelloJava", LanguageKind.Java, "Main.java", ToolchainInfo.Describe(LanguageKind.Java),
            new[]
            {
                new TemplateFile("Main.java", """
                    public class Main {
                        public static void main(String[] args) {
                            System.out.println("Hello from {{ProjectName}}!");
                        }
                    }
                    """),
                Readme("Java Console"),
            }),

        new ProjectTemplate(
            "javascript-script", "JavaScript Script", "A plain JavaScript file.",
            "HelloJavaScript", LanguageKind.JavaScript, "index.js", ToolchainInfo.Describe(LanguageKind.JavaScript),
            new[]
            {
                new TemplateFile("index.js", """
                    console.log("Hello from {{ProjectName}}!");
                    """),
                Readme("JavaScript Script"),
            }),

        new ProjectTemplate(
            "nodejs-project", "Node.js Project", "A Node.js project with a package.json and no dependencies.",
            "HelloNode", LanguageKind.JavaScript, "index.js",
            "Editing and syntax coloring only. Node.js support is a separate, later capability, and npm packages cannot be installed offline.",
            new[]
            {
                new TemplateFile("package.json", """
                    {
                      "name": "{{PackageName}}",
                      "version": "1.0.0",
                      "description": "{{ProjectName}}",
                      "main": "index.js",
                      "scripts": {
                        "start": "node index.js"
                      },
                      "private": true
                    }
                    """),
                new TemplateFile("index.js", """
                    console.log("Hello from {{ProjectName}}!");
                    """),
                new TemplateFile(".gitignore", """
                    node_modules/
                    """),
                Readme("Node.js Project"),
            }),

        new ProjectTemplate(
            "web-site", "HTML/CSS/JavaScript Website", "A small static website: index.html, a stylesheet and a script.",
            "HelloWebsite", LanguageKind.JavaScript, "index.html",
            "Editing and syntax coloring only (HTML and CSS open as plain text for now). Local preview is not available yet.",
            new[]
            {
                new TemplateFile("index.html", """
                    <!DOCTYPE html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <meta name="viewport" content="width=device-width, initial-scale=1">
                      <title>{{ProjectNameHtml}}</title>
                      <link rel="stylesheet" href="css/style.css">
                    </head>
                    <body>
                      <main>
                        <h1>{{ProjectNameHtml}}</h1>
                        <p id="message">Hello from your new website.</p>
                      </main>
                      <script src="js/main.js"></script>
                    </body>
                    </html>
                    """),
                new TemplateFile("css/style.css", """
                    body {
                      font-family: system-ui, sans-serif;
                      margin: 2rem;
                      background: #1e1e1e;
                      color: #d4d4d4;
                    }
                    """),
                new TemplateFile("js/main.js", """
                    document.getElementById("message").textContent = "Hello from JavaScript!";
                    """),
                Readme("HTML/CSS/JavaScript Website"),
            }),
    };

    public static ProjectTemplate? FindById(string? id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Replaces placeholders in a file path or file content. Output uses LF line endings.</summary>
    public static string Render(string text, string projectName)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("{{ProjectNameHtml}}", WebUtility.HtmlEncode(projectName), StringComparison.Ordinal)
            .Replace("{{ProjectName}}", projectName, StringComparison.Ordinal)
            .Replace("{{Identifier}}", ToIdentifier(projectName), StringComparison.Ordinal)
            .Replace("{{PackageName}}", ToPackageName(projectName), StringComparison.Ordinal);
    }

    /// <summary>ASCII letters, digits and underscores; never empty; never starts with a digit.</summary>
    public static string ToIdentifier(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            builder.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' ? c : '_');
        }

        if (builder.Length == 0 || builder.ToString().All(c => c == '_'))
        {
            return "App";
        }

        if (char.IsAsciiDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>Lowercase npm-style name: letters, digits, '-' and '_'; never empty.</summary>
    public static string ToPackageName(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            var keep = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
            if (keep)
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');
        if (result.Length > 100)
        {
            result = result[..100].Trim('-');
        }

        return result.Length == 0 ? "app" : result;
    }

    private static TemplateFile Readme(string templateName) =>
        new("README.md", "# {{ProjectName}}\n\nCreated with Sodium Studio from the " + templateName + " template.\n");
}
