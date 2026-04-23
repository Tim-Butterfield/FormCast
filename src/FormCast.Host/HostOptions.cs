// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// HostOptions.cs
// ==============
//
// command-line argument parser for FormCast.Host.exe.
// Intentionally minimal: --help, --version, --session-id <name>, and
// --run-seconds <int> for testing.
//
// Argument syntax is GNU-style long options. We do not yet need short
// options or positional arguments. Unknown options throw
// ArgumentException; the entry point catches it and prints UsageText.

using System;
using System.Globalization;
using System.Text;

namespace FormCast.Host
{
 /// <summary>
 /// Parsed command-line options for the host process.
 /// Constructed by <see cref="Parse(string[])"/>.
 /// </summary>
    internal sealed class HostOptions
    {
 /// <summary>True when <c>--help</c> was supplied.</summary>
        public bool ShowHelp { get; set; }

 /// <summary>True when <c>--version</c> was supplied.</summary>
        public bool ShowVersion { get; set; }

 /// <summary>
 /// Optional session id used to derive the singleton mutex
 /// name. Defaults to the current Windows logon session id
 /// when omitted (or to <c>"default"</c> on non-Windows hosts
 /// during xUnit cross-platform builds).
 /// </summary>
        public string SessionId { get; set; } = string.Empty;

 /// <summary>
 /// Test knob: how many seconds to sleep before
 /// exiting. The bridge probe sets this to 0 (or omits the
 /// flag) to validate that the exe runs and acquires its
 /// mutex without blocking. Setting it to a positive value
 /// gives the named-pipe development a window to
 /// connect against the running exe.
 /// </summary>
        public int RunSeconds { get; set; }

 /// <summary>
 /// idle-out knob: how many seconds the host will run
 /// without any client connection before it exits cleanly.
 /// Default is 60. Pass <c>0</c> to disable idle-out (the
 /// host runs until killed or until <c>--run-seconds</c>
 /// fires). The countdown is reset on every client connect
 /// and disconnect; while a client is actively connected,
 /// the timer is paused.
 /// </summary>
        public int IdleSeconds { get; set; } = 60;

 /// <summary>
 /// Multi-line usage banner. Pinned by xUnit so the doc
 /// stays in sync with the parser.
 /// </summary>
        public static string UsageText { get; } =
            "Usage: FormCast.Host [options]" + Environment.NewLine +
            "Options:" + Environment.NewLine +
            "  --help                 show this message and exit" + Environment.NewLine +
            "  --version              print version banner and exit" + Environment.NewLine +
            "  --session-id <name>    override the singleton mutex session id" + Environment.NewLine +
            "  --run-seconds <int>    sleep N seconds before exit (for testing)" + Environment.NewLine +
            "  --idle-seconds <int>   exit after N seconds with no client (default 60, 0=off)";

 /// <summary>
 /// Parse a CLI argument vector. Throws
 /// <see cref="ArgumentException"/> on unknown options or
 /// missing values; the entry point in <see cref="Program"/>
 /// catches that and renders <see cref="UsageText"/>.
 /// </summary>
        public static HostOptions Parse(string[] args)
        {
            if (args is null) { throw new ArgumentNullException(nameof(args)); }

            var opts = new HostOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        opts.ShowHelp = true;
                        break;
                    case "--version":
                    case "-v":
                        opts.ShowVersion = true;
                        break;
                    case "--session-id":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("--session-id requires a value");
                        }
                        opts.SessionId = args[++i];
                        break;
                    case "--run-seconds":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("--run-seconds requires a value");
                        }
                        if (!int.TryParse(args[++i], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int n) || n < 0)
                        {
                            throw new ArgumentException(
                                "--run-seconds value must be a non-negative integer");
                        }
                        opts.RunSeconds = n;
                        break;
                    case "--idle-seconds":
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("--idle-seconds requires a value");
                        }
                        if (!int.TryParse(args[++i], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int idleN) || idleN < 0)
                        {
                            throw new ArgumentException(
                                "--idle-seconds value must be a non-negative integer");
                        }
                        opts.IdleSeconds = idleN;
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + a);
                }
            }
            return opts;
        }

 /// <summary>
 /// Compact stringification used by tests to assert the parsed
 /// option set in a single Assert.Equal call.
 /// </summary>
        internal string ToDebugString()
        {
            var sb = new StringBuilder();
            sb.Append("help=").Append(ShowHelp ? "1" : "0");
            sb.Append(" version=").Append(ShowVersion ? "1" : "0");
            sb.Append(" session-id=").Append(SessionId);
            sb.Append(" run-seconds=").Append(RunSeconds.ToString(CultureInfo.InvariantCulture));
            sb.Append(" idle-seconds=").Append(IdleSeconds.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
