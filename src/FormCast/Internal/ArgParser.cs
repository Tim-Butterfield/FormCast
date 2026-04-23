// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Globalization;

namespace FormCast.Internal
{
 /// <summary>
 /// Lightweight comma-delimited argument parser for the
 /// <c>StringBuilder args</c> that TCC passes to plugin variable
 /// functions and commands. The plugin receives the literal text
 /// between the function brackets without any pre-tokenization, so
 /// every dispatch method that takes more than one argument must do
 /// its own splitting.
 /// </summary>
 /// <remarks>
 /// <para>The current splitter is intentionally simple: it splits on
 /// the comma character and trims whitespace from each token. It does
 /// NOT yet handle quoted commas (e.g. <c>text="Hello, World"</c>),
 /// which is a known follow-up captured in PLUGIN_DESIGN.md section
 /// 7 #14. The plan is to harden this splitter when the first
 /// real-world use case (the @FORMADD property bag) needs it. Until
 /// then, BTM authors who need a comma in a string argument can
 /// substitute it via @CHAR or build the property bag in a separate
 /// SET first.</para>
 /// </remarks>
    internal static class ArgParser
    {
 /// <summary>
 /// Split a comma-delimited argument string into trimmed tokens.
 /// Empty input returns an empty array. Trailing/leading whitespace
 /// on each token is removed.
 /// </summary>
        public static string[] Split(string? args)
        {
            if (string.IsNullOrEmpty(args))
            {
                return Array.Empty<string>();
            }

            string[] parts = args!.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }
            return parts;
        }

 /// <summary>
 /// Parse an integer argument with invariant culture, returning
 /// <paramref name="defaultValue"/> if the input is null, empty,
 /// or not a valid integer. Used for optional positional args
 /// like the position/size on @FORMOPEN.
 /// </summary>
        public static int ParseIntOrDefault(string? value, int defaultValue)
        {
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : defaultValue;
        }
    }
}
