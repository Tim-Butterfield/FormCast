// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Program.cs
// ==========
//
// FormCast.Host.exe entry point. Provides argument parsing, version
// banner, single-instance mutex, named-pipe IPC server, and idle-out
// timer.
//
// Why a separate process: a WinForms message loop owns its forms via
// a single STA thread, and no Control instance can be shared across
// processes. The only way to keep a Global\ scoped form alive after
// the TCC session that created it exits is to host it in a daemon
// process that the next session can re-attach to.
//
// Lifecycle:
//   1. The first plugin instance that opens a Global\ handle checks
//      for the well-known mutex. If absent, it spawns this exe.
//   2. This exe acquires the mutex, opens the named pipe, and waits
//      for connections.
//   3. Plugin instances connect over the pipe and dispatch
//      registry / event traffic. Local\ handles never touch this
//      code path.
//   4. When the last global handle is freed AND no plugin has
//      connected for IdleSeconds, the host exits cleanly.

using System;
using System.Threading;

using FormCast.Ipc;

namespace FormCast.Host
{
 /// <summary>
 /// Entry point for the FormCast.Host.exe daemon. Argument parsing
 /// is delegated to <see cref="HostOptions.Parse(string[])"/> so it
 /// can be unit-tested without spinning up a process.
 /// </summary>
    public static class Program
    {
 /// <summary>
 /// Process exit codes. Stay
 /// stable so test BTMs can branch on them.
 /// </summary>
        internal static class ExitCodes
        {
 /// <summary>Normal completion.</summary>
            public const int Success = 0;
 /// <summary>Argument parser rejected the command line.</summary>
            public const int BadArgs = 2;
 /// <summary>Another instance of the host is already running.</summary>
            public const int AlreadyRunning = 3;
        }

 /// <summary>
 /// CLI entry point. Returns one of <see cref="ExitCodes"/>.
 /// </summary>
        public static int Main(string[] args)
        {
            HostOptions options;
            try
            {
                options = HostOptions.Parse(args ?? Array.Empty<string>());
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("FormCast.Host: " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine(HostOptions.UsageText);
                return ExitCodes.BadArgs;
            }

            if (options.ShowHelp)
            {
                Console.Out.WriteLine(HostOptions.UsageText);
                return ExitCodes.Success;
            }

            if (options.ShowVersion)
            {
                Console.Out.WriteLine(BuildVersionBanner());
                return ExitCodes.Success;
            }

 // Acquire the singleton mutex. The name is per-logon-
 // session ("Local\\" + a session-derived suffix) so two
 // users on the same machine each get their own host.
            string mutexName = HostMutex.BuildName(options.SessionId);
            bool createdNew;
            using var mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out createdNew);
            if (!createdNew)
            {
                Console.Error.WriteLine(
                    "FormCast.Host: another instance is already running (mutex '" +
                    mutexName + "').");
                return ExitCodes.AlreadyRunning;
            }

            try
            {
                Console.Out.WriteLine(BuildVersionBanner());
                Console.Out.WriteLine("singleton mutex acquired: " + mutexName);

 // spin up the named-pipe server. Lifetime is
 // bounded by --run-seconds when set (so the bridge
 // smoke test can exit cleanly), otherwise the server
 // runs until the process is terminated. will
 // add the idle-out timer that fires the same
 // CancellationTokenSource we set up here.
                string sessionId = string.IsNullOrEmpty(options.SessionId)
                    ? HostMutex.ResolveCurrentSessionId()
                    : options.SessionId;
                string pipeName = PipeProtocol.BuildPipeName(sessionId);
                Console.Out.WriteLine("named pipe listening: \\\\.\\pipe\\" + pipeName);

                TimeSpan idleTimeout = options.IdleSeconds > 0
                    ? TimeSpan.FromSeconds(options.IdleSeconds)
                    : TimeSpan.Zero;
                if (idleTimeout > TimeSpan.Zero)
                {
                    Console.Out.WriteLine("idle timeout: " +
                        options.IdleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " seconds");
                }
                else
                {
                    Console.Out.WriteLine("idle timeout: disabled");
                }

                using var server = new PipeServer(
                    pipeName,
                    serverName: "FormCast.Host",
                    log: line => Console.Out.WriteLine(line),
                    idleTimeout: idleTimeout);

                using var cts = new CancellationTokenSource();
                if (options.RunSeconds > 0)
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(options.RunSeconds));
                    Console.Out.WriteLine(
                        "auto-stop after " + options.RunSeconds.ToString(
                            System.Globalization.CultureInfo.InvariantCulture) + " seconds");
                }

                server.Run(cts.Token);
                Console.Out.WriteLine("server loop exited; total connections accepted=" +
                    server.AcceptCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { /* swallow */ }
            }
            return ExitCodes.Success;
        }

 /// <summary>
 /// Render the multi-line version banner shown by --version
 /// and at the top of every interactive run. Pure string
 /// formatting so xUnit can pin it.
 /// </summary>
        public static string BuildVersionBanner()
        {
            string version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            return
                "FormCast.Host " + version + Environment.NewLine +
                "Cross-process daemon for FormCast Global\\ handles." + Environment.NewLine +
                "Copyright (c) 2026 Tim Butterfield. MIT License.";
        }
    }
}
