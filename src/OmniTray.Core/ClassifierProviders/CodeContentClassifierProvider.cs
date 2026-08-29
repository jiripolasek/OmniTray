// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class CodeContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.code",
        "Code classifier",
        "omnitray.code",
        "Code",
        ContentFacets.Code)
{
    private static readonly HashSet<string> SourceFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".asm",
        ".bash",
        ".bat",
        ".c",
        ".cc",
        ".cjs",
        ".clj",
        ".cljs",
        ".cpp",
        ".cs",
        ".css",
        ".cxx",
        ".dart",
        ".ex",
        ".exs",
        ".fs",
        ".fsx",
        ".go",
        ".gradle",
        ".groovy",
        ".h",
        ".hh",
        ".hpp",
        ".htm",
        ".html",
        ".hxx",
        ".java",
        ".js",
        ".jsx",
        ".kt",
        ".kts",
        ".less",
        ".lua",
        ".m",
        ".mjs",
        ".mm",
        ".php",
        ".ps1",
        ".psd1",
        ".psm1",
        ".py",
        ".pyw",
        ".r",
        ".rb",
        ".rs",
        ".s",
        ".scala",
        ".scss",
        ".sh",
        ".sol",
        ".sql",
        ".swift",
        ".ts",
        ".tsx",
        ".vb",
        ".vue",
        ".zsh"
    };

    private static readonly HashSet<string> SourceFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CMakeLists.txt", "Dockerfile", "Makefile"
    };

    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsCode(context.Text, context.Html) ||
        IsSourceCodeFile(context.SourcePath) ||
        IsSourceCodeFile(context.FileFacts?.OriginalFileName);

    private static bool IsSourceCodeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path.Trim());
        return SourceFileNames.Contains(fileName) ||
               SourceFileExtensions.Contains(Path.GetExtension(fileName));
    }
}
