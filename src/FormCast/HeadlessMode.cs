// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// HeadlessMode.cs
// ===============
//
// Read-once detection of the FORMCAST_HEADLESS environment variable.
// When set to "1", "true", "yes", or "on" (case-insensitive), the
// plugin runs in a mode where no window is ever shown:
//
// - @FORMSHOW becomes a no-op that logs the request and returns
// success.
// - Modal @FORMSHOW[h, modal] auto-closes after a configurable
// short timeout (default 100 ms) so the script thread is never
// blocked waiting for a human.
// - Error paths that would normally call MessageBox.Show write to
// Console.Error and return the appropriate error code instead.
//
// The forced shutdown contract from PLUGIN_DESIGN.md section 4.6
// still runs in full in headless mode, so test BTMs that load and
// unload the plugin exercise the cleanup path on every run.
//
// This class is intentionally simple: a single static IsEnabled
// property and a Refresh() method for tests. Production code reads
// IsEnabled directly. The detection happens once when the plugin is
// loaded and is not re-evaluated; tests that need to flip headless
// state mid-run call Refresh() after mutating the env var.

using System;

namespace FormCast
{
 /// <summary>
 /// Static accessor for the FormCast headless-mode flag. Read at
 /// plugin load time from the FORMCAST_HEADLESS environment variable.
 /// See the file header for the exact semantics of headless mode.
 /// </summary>
    public static class HeadlessMode
    {
 /// <summary>
 /// The environment variable name FormCast checks. Documented
 /// in the README and PLUGIN_DESIGN.md so users and test
 /// authors can flip headless mode on for unattended runs.
 /// </summary>
        public const string EnvVarName = "FORMCAST_HEADLESS";

 /// <summary>
 /// True when the plugin is running in headless mode. Read once
 /// at plugin load (via the static initializer below) and may be
 /// re-evaluated by calling <see cref="Refresh"/>.
 /// </summary>
        public static bool IsEnabled { get; private set; } = ParseEnvVar();

 /// <summary>
 /// Re-read the environment variable. Tests use this when they
 /// need to flip the headless flag mid-run. Production code does
 /// not need to call this; the static initializer covers the
 /// load-time read.
 /// </summary>
        public static void Refresh()
        {
            IsEnabled = ParseEnvVar();
        }

 /// <summary>
 /// Pure parsing logic exposed for unit testing. Accepts a raw
 /// string (typically from <see cref="Environment.GetEnvironmentVariable(string)"/>)
 /// and decides whether it represents the truthy headless value.
 /// </summary>
 /// <remarks>
 /// Recognized truthy values (case-insensitive, with surrounding
 /// whitespace trimmed): <c>1</c>, <c>true</c>, <c>yes</c>, <c>on</c>.
 /// Everything else, including <c>null</c>, empty string, and any
 /// other non-recognized value, is falsy.
 /// </remarks>
        public static bool ParseTruthy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            switch (raw!.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ParseEnvVar()
        {
            return ParseTruthy(Environment.GetEnvironmentVariable(EnvVarName));
        }
    }
}
