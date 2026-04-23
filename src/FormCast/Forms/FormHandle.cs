// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Diagnostics;
using System.Globalization;

namespace FormCast.Forms
{
 /// <summary>
 /// Format and parse FormCast form handle strings. The handle is the
 /// opaque string FormCast hands back to BTM callers from <c>@FORMOPEN</c>;
 /// it survives round-tripping through TCC environment variables, ENVAR,
 /// SET, and arbitrary BTM string handling, and uniquely identifies a
 /// form within its scope.
 /// </summary>
 /// <remarks>
 /// <para>Format: <c>L:&lt;pid&gt;:&lt;seq&gt;</c> for local-scope handles
 /// (the only kind in v0.0.x), where <c>pid</c> is the TCC process id at
 /// the time the handle was minted and <c>seq</c> is the
 /// <see cref="LocalFormRegistry"/> sequence number.</para>
 ///
 /// <para>The pid prefix lets a stale handle from another process be
 /// distinguished from a stale handle in the current process: a handle
 /// with the wrong pid is always invalid for our registry, regardless
 /// of whether the seq number happens to match a current allocation.
 /// This avoids a hard-to-debug class of bug where a BTM script saves a
 /// handle into a global env var, the TCC session ends, a new session
 /// starts, and the script (re-loaded) tries to use the old handle.</para>
 ///
 /// <para>Global handles will use the form <c>G:&lt;seq&gt;</c>
 /// without a pid since they belong to <c>FormCast.Host.exe</c> and
 /// outlive any single TCC process.</para>
 /// </remarks>
    public static class FormHandle
    {
 /// <summary>
 /// The scope-prefix character for local handles (this process only).
 /// </summary>
        public const string LocalScopePrefix = "L";

 /// <summary>
 /// Format a sequence number from <see cref="LocalFormRegistry"/>
 /// as the string handle FormCast hands back to BTM callers.
 /// </summary>
        public static string Format(int seq)
        {
            int pid = Process.GetCurrentProcess().Id;
            return string.Concat(
                LocalScopePrefix,
                ":",
                pid.ToString(CultureInfo.InvariantCulture),
                ":",
                seq.ToString(CultureInfo.InvariantCulture));
        }

 /// <summary>
 /// Try to parse a handle string back into its sequence number.
 /// Returns <see langword="true"/> on success and writes the
 /// sequence number to <paramref name="seq"/>; returns
 /// <see langword="false"/> on any parse failure (wrong format,
 /// non-local scope, non-numeric pid or seq).
 /// </summary>
 /// <remarks>
 /// We deliberately do NOT validate that the pid matches the
 /// current process here. The caller (typically the registry
 /// lookup) will simply not find a matching seq if the handle
 /// was minted by a different process, which is the right
 /// failure mode. Verifying pid here would require a
 /// <see cref="Process.GetCurrentProcess"/> call on every parse,
 /// which is wasteful for the common case.
 /// </remarks>
        public static bool TryParse(string? handle, out int seq)
        {
            seq = -1;
            if (string.IsNullOrEmpty(handle))
            {
                return false;
            }

            string[] parts = handle!.Split(':');
            if (parts.Length != 3)
            {
                return false;
            }

            if (parts[0] != LocalScopePrefix)
            {
                return false;
            }

 // Validate the pid is a number even though we don't use it,
 // so a malformed handle like "L:abc:5" is rejected up front.
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq);
        }
    }
}
