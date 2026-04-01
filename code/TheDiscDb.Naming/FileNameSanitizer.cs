using System;
using System.Collections.Generic;
using System.IO;

namespace TheDiscDb.Naming;

public static class FileNameSanitizer
{
    private static readonly HashSet<char> IllegalChars = new(Path.GetInvalidFileNameChars());

    /// <summary>
    /// Sanitizes a string for use as a filename by replacing or removing
    /// characters that are illegal in file names on common operating systems.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        // Pre-scan: check if any work is needed
        bool needsWork = false;
        for (int i = 0; i < input.Length; i++)
        {
            if (IllegalChars.Contains(input[i]))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return input;
        }

        // Colon replacement is " - " (3 chars), so worst case is 3x original length
        var chars = new char[input.Length * 3];
        int pos = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == ':')
            {
                // Avoid double spaces when colon is adjacent to existing spaces
                bool prevIsSpace = pos > 0 && chars[pos - 1] == ' ';
                bool nextIsSpace = i + 1 < input.Length && input[i + 1] == ' ';

                if (!prevIsSpace)
                {
                    chars[pos++] = ' ';
                }

                chars[pos++] = '-';

                if (!nextIsSpace)
                {
                    chars[pos++] = ' ';
                }
            }
            else if (!IllegalChars.Contains(c))
            {
                chars[pos++] = c;
            }
            // Illegal chars other than colon are removed (no output)
        }

        return new string(chars, 0, pos);
    }
}
