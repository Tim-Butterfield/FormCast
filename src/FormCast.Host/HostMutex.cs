// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// HostMutex.cs
// ============
//
// helper that builds the well-known singleton mutex name for
// FormCast.Host.exe and provides a small unit-test seam for the
// session-id derivation. The format is documented in
// PLUGIN_DESIGN.md section 5.4 ("the well-known mutex
// `Global\FormCast.Host.<sid>`") but the current version uses
// "Local\\" instead of "Global\\" so the test process and the
// production exe stay isolated unless the user explicitly opts in.
//
// A future version may switch to "Global\\" with an explicit ACL that limits
// the mutex to the same logon session, matching the named-pipe ACL.
// For now we only need a name that two FormCast.Host instances on
// the same desktop will collide on so the singleton check fires.

using System;
using System.Globalization;

namespace FormCast.Host
{
 /// <summary>
 /// Builds the well-known singleton mutex name used by
 /// <see cref="Program.Main"/> to enforce one host instance per
 /// logon session.
 /// </summary>
    internal static class HostMutex
    {
 /// <summary>
 /// Mutex name prefix. uses the local-namespace prefix
 /// so each user (and each test process) gets an isolated
 /// scope; will switch this to <c>"Global\\"</c> with
 /// an ACL pinning the mutex to the current logon session.
 /// </summary>
        public const string NamePrefix = "Local\\FormCast.Host.";

 /// <summary>
 /// Build the singleton mutex name for the given session id.
 /// Empty / null <paramref name="sessionId"/> falls back to
 /// the current process's session id (or <c>"default"</c>
 /// when that lookup fails).
 /// </summary>
        public static string BuildName(string? sessionId)
        {
            string suffix;
            if (!string.IsNullOrEmpty(sessionId))
            {
                suffix = sessionId!;
            }
            else
            {
                suffix = ResolveCurrentSessionId();
            }
            return NamePrefix + suffix;
        }

 /// <summary>
 /// Look up the current process's logon session id. Returns
 /// the integer id from
 /// <see cref="System.Diagnostics.Process.SessionId"/> when
 /// available, or <c>"default"</c> if anything in the lookup
 /// throws (this happens on minimal containers / non-Windows
 /// hosts during cross-platform CI).
 /// </summary>
        internal static string ResolveCurrentSessionId()
        {
            try
            {
                int sid = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                return sid.ToString(CultureInfo.InvariantCulture);
            }
            catch (PlatformNotSupportedException)
            {
                return "default";
            }
            catch (InvalidOperationException)
            {
                return "default";
            }
        }
    }
}
