// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text;

namespace OmniTray.CommandPalette.Helpers;

internal static class MarkdownText
{
    internal static string Escape(string? value, int maximumLength = 4000)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Length <= maximumLength
            ? value
            : $"{value[..maximumLength]}…";
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '<' or '>' or '#')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
