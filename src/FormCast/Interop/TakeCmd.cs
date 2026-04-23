// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Interop/TakeCmd.cs
// ==================
//
// P/Invoke declarations for the helper functions exported by `TakeCmd.dll`,
// the native heart of TCC. The full export surface (1626 entries as of
// TCC v36 build 21) is documented in `vendor/sdk/TakeCmd.h`; only the
// helpers FormCast actually needs across all 12 phases of the v1 roadmap
// are declared here.
//
// Conventions:
// - All exports use `extern "C"` (verified empirically against TakeCmd.dll
// v36 -- the exact entry point names appear in the export table).
// - Wide-character strings are UTF-16. The `CharSet = CharSet.Unicode`
// attribute on each declaration tells the marshaler to use the W
// suffix and the UTF-16 representation.
// - The native `WCHAR*` parameters are surfaced as `StringBuilder` (when
// the function writes back into the buffer) or `string` (when the buffer
// is read-only).
// - We do not specify CallingConvention. The host loads us as 64-bit,
// and on x64 Windows there is exactly one calling convention (the
// Microsoft x64 calling convention), so the WINAPI/__stdcall vs
// __cdecl distinction in the header is moot for our binaries.
// - All return values are documented per `TakeCmd.h`.
//
// **Threading note:** Some of these (notably `Command`, the writers, and
// `SetEVariable`) must only be called from a thread that TCC owns or that
// the plugin's own callback worker thread services. The WinForms STA
// The WinForms host thread MUST NOT call these directly -- it must
// marshal the call back through the callback worker queue.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FormCast.Interop
{
 /// <summary>
 /// P/Invoke wrappers for the subset of <c>TakeCmd.dll</c> exports that
 /// FormCast needs at runtime. The full helper surface is documented in
 /// the vendor-shipped <c>TakeCmd.h</c>; only the exports we actually
 /// call are declared here.
 /// </summary>
    internal static class TakeCmd
    {
 /// <summary>
 /// Name of the native DLL shipped with TCC v36+. No <c>64</c> suffix
 /// even on the x64 build. The DLL is loaded automatically by the host
 /// process before our plugin is given control, so the loader resolves
 /// it from the TCC install directory without needing an explicit path.
 /// </summary>
        private const string DllName = "TakeCmd.dll";

 /// <summary>
 /// Expand TCC variable references inside <paramref name="buffer"/>.
 /// Mirrors the behavior of TCC's command-line variable expansion:
 /// <c>%name</c>, <c>%name%</c>, <c>%@func[args]</c>, etc. The buffer
 /// is updated in place; pass it pre-sized to a generous capacity
 /// because expansion may grow the contents significantly.
 /// </summary>
 /// <param name="buffer">
 /// On entry: the string to expand, terminated by <c>'\0'</c>.
 /// On exit: the expanded result, terminated by <c>'\0'</c>.
 /// </param>
 /// <param name="reserved">
 /// Reserved by TCC. Pass <c>0</c>.
 /// </param>
 /// <returns>
 /// <c>0</c> on success, non-zero on parse error. See <c>TakeCmd.h</c>
 /// for the error code list.
 /// </returns>
        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "ExpandVariables")]
        public static extern int ExpandVariables(StringBuilder buffer, int reserved);

 /// <summary>
 /// Execute a TCC command in the caller's session. The command runs
 /// in the same context as if the user had typed it at the prompt.
 /// </summary>
 /// <param name="command">
 /// The command line to execute. UTF-16 with a terminating <c>'\0'</c>.
 /// </param>
 /// <param name="reserved">
 /// Reserved by TCC. Pass <c>0</c>.
 /// </param>
 /// <returns>
 /// The exit code of the command (typically <c>0</c> for success),
 /// or a TCC parse error code if the command line could not be
 /// tokenized.
 /// </returns>
        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "Command")]
        public static extern int Command(string command, int reserved);

 /// <summary>
 /// Native <c>SetEVariable</c>: set or delete a TCC environment
 /// variable in the caller's scope. The native signature takes a
 /// single mutable buffer in the conventional <c>NAME=VALUE</c>
 /// form, parsed exactly as the <c>SET</c> command would parse it.
 /// An empty value (<c>NAME=</c>) deletes the variable. Honors
 /// <c>SETLOCAL</c>, so the assignment is local to the current
 /// nested scope and is restored when the scope unwinds.
 /// </summary>
 /// <remarks>
 /// The header declares <c>LPTSTR</c> (mutable), so we marshal as
 /// <see cref="StringBuilder"/> to be safe -- TCC may legitimately
 /// modify the buffer in place during parsing. Callers should
 /// prefer the <see cref="SetEnv(string, string)"/> helper rather
 /// than building the <c>NAME=VALUE</c> string by hand.
 /// </remarks>
 /// <param name="nameEqualsValue">
 /// On entry: a NUL-terminated <c>NAME=VALUE</c> buffer with
 /// enough capacity for any in-place mutation TCC may perform.
 /// </param>
 /// <returns>
 /// <c>0</c> on success, non-zero on failure (read-only var,
 /// malformed name, etc.).
 /// </returns>
        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "SetEVariable")]
        public static extern int SetEVariableNative(StringBuilder nameEqualsValue);

 /// <summary>
 /// High-level wrapper around <see cref="SetEVariableNative"/>.
 /// Builds the <c>NAME=VALUE</c> buffer with generous slack and
 /// dispatches to TCC. The caller's scope (and any active
 /// <c>SETLOCAL</c>) determines visibility -- this is the API
 /// that resolves the very first risk in the design doc:
 /// "can a plugin write back into its caller's variable scope?"
 /// </summary>
 /// <param name="name">
 /// The variable name. Must be a legal TCC variable identifier
 /// (no leading <c>%</c>, no <c>=</c> character).
 /// </param>
 /// <param name="value">
 /// The value to assign. Pass an empty string (or <c>null</c>)
 /// to delete the variable.
 /// </param>
 /// <returns>
 /// <c>0</c> on success, non-zero on failure as reported by TCC,
 /// or <c>-1</c> if <paramref name="name"/> is null/empty/invalid.
 /// </returns>
        public static int SetEnv(string name, string? value)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf('=') >= 0)
            {
                return -1;
            }
            string v = value ?? string.Empty;
 // Slack: name + '=' + value + NUL + a little headroom in
 // case TCC needs to canonicalize in place.
            int capacity = name.Length + v.Length + 16;
            var buf = new StringBuilder(capacity);
            buf.Append(name);
            buf.Append('=');
            buf.Append(v);
            return SetEVariableNative(buf);
        }

 /// <summary>
 /// Yield CPU back to TCC's main loop. Plugins doing long-running
 /// or polling work must call this periodically (typically once per
 /// loop iteration with <paramref name="milliseconds"/> = 1) so
 /// TCC's keystroke handling, console redraw, and other plugins
 /// continue to function.
 /// </summary>
 /// <param name="milliseconds">
 /// How long to yield, in milliseconds. <c>1</c> is the typical value.
 /// </param>
        [DllImport(DllName, EntryPoint = "tty_yield")]
        public static extern void TtyYield(int milliseconds);

 /// <summary>
 /// Native <c>wwriteXP</c>: write a UTF-16 string to a TCC-managed
 /// file handle, routing console handles to the display (with
 /// pipe redirection honored) and other handles straight to the
 /// file. Prefer this over <c>Console.Write</c> for any output
 /// that might end up redirected through a TCC pipeline.
 /// </summary>
 /// <param name="hFile">
 /// The file handle. For console output use
 /// <c>GetStdHandle(STD_OUTPUT_HANDLE)</c>; for stderr use
 /// <c>STD_ERROR_HANDLE</c>. The
 /// <see cref="WriteStdOut(string)"/> helper is the preferred
 /// way to get to stdout.
 /// </param>
 /// <param name="text">The text to write.</param>
 /// <param name="length">
 /// Number of characters to write. Pass <c>-1</c> to let TCC
 /// compute the length from the NUL terminator.
 /// </param>
 /// <returns>The native return value (typically chars written).</returns>
        [DllImport(DllName, CharSet = CharSet.Unicode, EntryPoint = "wwriteXP")]
        public static extern int WriteOutputNative(IntPtr hFile, string text, int length);

 /// <summary>
 /// High-level helper: write <paramref name="text"/> to TCC's
 /// stdout via <see cref="WriteOutputNative"/>, after resolving
 /// the standard output handle from kernel32.
 /// </summary>
        public static int WriteStdOut(string text)
        {
            IntPtr h = NativeMethods.GetStdHandle(NativeMethods.StdOutputHandle);
            return WriteOutputNative(h, text, text?.Length ?? 0);
        }

 /// <summary>
 /// High-level helper: write <paramref name="text"/> to TCC's
 /// stderr via <see cref="WriteOutputNative"/>.
 /// </summary>
        public static int WriteStdErr(string text)
        {
            IntPtr h = NativeMethods.GetStdHandle(NativeMethods.StdErrorHandle);
            return WriteOutputNative(h, text, text?.Length ?? 0);
        }

 /// <summary>
 /// Query whether TCC's current output stream is configured for
 /// Unicode (UTF-16) or ANSI (active code page).
 /// </summary>
 /// <returns>
 /// Non-zero if Unicode, zero if ANSI. Plugins should check this
 /// before encoding output by hand; <see cref="WriteStdOut"/>
 /// already handles the conversion internally.
 /// </returns>
        [DllImport(DllName, EntryPoint = "QueryUnicodeOutput")]
        public static extern int QueryUnicodeOutput();

 /// <summary>
 /// Returns non-zero when the host process is Take Command (the
 /// GUI shell) rather than the bare TCC console. Plugins that
 /// behave differently in the two hosts can branch on this.
 /// </summary>
        [DllImport(DllName, EntryPoint = "QueryIsTCMD")]
        public static extern int QueryIsTCMD();

 /// <summary>
 /// Disable TCC's Ctrl+C / Ctrl+Break handling. Use this around
 /// short critical sections (e.g. the forced-shutdown path)
 /// where a Ctrl+Break in the middle of teardown would leave
 /// handles dangling.
 /// </summary>
        [DllImport(DllName, EntryPoint = "HoldSignals")]
        public static extern void HoldSignals();

 /// <summary>
 /// Re-enable TCC's Ctrl+C / Ctrl+Break handling after a prior
 /// <see cref="HoldSignals"/>. Always pair these calls in a
 /// try/finally so a thrown exception cannot leave signals
 /// permanently disabled.
 /// </summary>
        [DllImport(DllName, EntryPoint = "EnableSignals")]
        public static extern void EnableSignals();

 /// <summary>
 /// Invoke TCC's internal Ctrl+C / Ctrl+Break handler. Useful
 /// from a plugin that wants to surface "the user asked to
 /// abort" through the same code path TCC uses for a real
 /// keyboard break.
 /// </summary>
        [DllImport(DllName, EntryPoint = "BreakHandler")]
        public static extern void BreakHandler();

 // -----------------------------------------------------------------
 // kernel32 helpers used by the WriteStdOut / WriteStdErr wrappers.
 // Hoisted into a nested type so the public surface of TakeCmd
 // stays focused on TCC exports.
 // -----------------------------------------------------------------

        private static class NativeMethods
        {
            public const int StdOutputHandle = -11;
            public const int StdErrorHandle = -12;

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int nStdHandle);
        }
    }
}
