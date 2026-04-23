// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Internal/PluginLogger.cs
// ========================
//
// Centralized logging for the FormCast plugin. All plugin actions,
// events, errors, and designer operations can be logged to a file
// for debugging and auditing.
//
// Log levels (lowest to highest verbosity):
//   Off     No logging
//   Error   Plugin failures, exceptions
//   Warn    Unexpected conditions that don't fail
//   Info    High-level actions (formopen, formclose, formshow)
//   Debug   Property sets/gets, event dispatch, control creation
//   Trace   Every FORMEVENTS drain, every poll cycle
//
// Usage from BTM:
//   set RC=%@formlog[debug,%TEMP\formcast.log]   Enable debug logging
//   set RC=%@formlog[off]                        Disable logging
//
// Thread-safe: all writes go through a lock. The file is opened
// in append mode with sharing so TCC and the user can both read it.

using System;
using System.Globalization;
using System.IO;

namespace FormCast.Internal
{
    /// <summary>
    /// Singleton logger for the FormCast plugin. Thread-safe.
    /// </summary>
    internal static class PluginLogger
    {
        public enum Level
        {
            Off = 0,
            Error = 1,
            Warn = 2,
            Info = 3,
            Debug = 4,
            Trace = 5,
        }

        private static Level _level = Level.Off;
        private static string? _path;
        private static StreamWriter? _writer;
        private static readonly object _lock = new object();

        /// <summary>Current log level.</summary>
        public static Level CurrentLevel => _level;

        /// <summary>True if logging is enabled at any level.</summary>
        public static bool IsEnabled => _level > Level.Off && _writer is not null;

        /// <summary>
        /// Configure logging. Pass level=Off to disable.
        /// Path is ignored when level is Off.
        /// </summary>
        public static void Configure(Level level, string? path)
        {
            lock (_lock)
            {
                // Close existing writer
                if (_writer is not null)
                {
                    try { _writer.Flush(); _writer.Close(); }
                    catch { }
                    _writer = null;
                }

                _level = level;
                _path = path;

                if (level > Level.Off && !string.IsNullOrEmpty(path))
                {
                    try
                    {
                        _writer = new StreamWriter(
                            new FileStream(path!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        {
                            AutoFlush = true,
                        };
                        _writer.WriteLine($"--- FormCast log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} level={level} ---");
                    }
                    catch
                    {
                        _writer = null;
                        _level = Level.Off;
                    }
                }
            }
        }

        /// <summary>Shut down logging. Called from Plugin.Shutdown.</summary>
        public static void Shutdown()
        {
            Configure(Level.Off, null);
        }

        // Convenience methods for each level

        public static void Error(string message) => Log(Level.Error, message);
        public static void Warn(string message) => Log(Level.Warn, message);
        public static void Info(string message) => Log(Level.Info, message);
        public static void Debug(string message) => Log(Level.Debug, message);
        public static void Trace(string message) => Log(Level.Trace, message);

        /// <summary>
        /// Write a log entry if the current level is >= the given level.
        /// Format: timestamp [LEVEL] message
        /// </summary>
        public static void Log(Level level, string message)
        {
            if (level > _level || _writer is null) { return; }
            string ts = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string tag = level switch
            {
                Level.Error => "ERR",
                Level.Warn => "WRN",
                Level.Info => "INF",
                Level.Debug => "DBG",
                Level.Trace => "TRC",
                _ => "???",
            };
            lock (_lock)
            {
                try { _writer?.WriteLine($"{ts} [{tag}] {message}"); }
                catch { }
            }
        }

        /// <summary>Parse a level string (case-insensitive).</summary>
        public static Level ParseLevel(string? s)
        {
            if (string.IsNullOrEmpty(s)) return Level.Off;
            return s!.Trim().ToLowerInvariant() switch
            {
                "off" => Level.Off,
                "error" => Level.Error,
                "warn" => Level.Warn,
                "info" => Level.Info,
                "debug" => Level.Debug,
                "trace" => Level.Trace,
                _ => Level.Off,
            };
        }
    }
}
