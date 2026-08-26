// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;

namespace OmniTray.Core;

public static class DataFormatInspectionText
{
    public static string CreatePreview(string value, int maximumLength = 160)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        var visible = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        return visible.Length <= maximumLength
            ? visible
            : $"{visible[..maximumLength]}…";
    }

    public static string FormatByteCount(ulong byteCount)
    {
        var exact = byteCount.ToString("N0", CultureInfo.InvariantCulture);
        if (byteCount < 1024)
        {
            return $"{exact} bytes";
        }

        ReadOnlySpan<string> units = ["KiB", "MiB", "GiB", "TiB"];
        var value = (double)byteCount;
        var unitIndex = -1;
        do
        {
            value /= 1024;
            unitIndex++;
        }
        while (value >= 1024 && unitIndex < units.Length - 1);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{exact} bytes ({value:0.##} {units[unitIndex]})");
    }
}
