using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers;

public static class FileNameValidator
{
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name is "." or "..")
            return false;

        foreach (char c in name)
        {
            if (c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                return false;
            if (c < 0x20 || c == 0x7F)
                return false;
        }

        if (name[^1] is ' ' or '.')
            return false;

        if(name.Length>255)
            return false;

        return !IsWindowsReservedName(name);
    }

    private static bool IsWindowsReservedName(string name)
    {
        ReadOnlySpan<char> stem = name.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot >= 0)
            stem = stem[..dot];

        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;

        return stem.Length == 4
            && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) ||
                stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }
}
