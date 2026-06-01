// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Plugin.cs
// =========
//
// FormCast plugin entry point. Implements the TakeCommand.Plugin.ITCCPlugin
// contract that the C++/CLI host (TC-DotNetPluginHost64.dll) discovers via
// reflection after loading our assembly.
//
// Registers the full @FORM* function family, the WinForms host thread,
// layout managers, the event queue, declarative bindings, and the forced-
// shutdown contract.
//
// Diagnostic marker file: when the FORMCAST_INIT_LOG environment variable
// is set (to any non-empty value), Initialize() writes %TEMP%\FormCast.init.log
// with lifecycle and TakeCmd.dll P/Invoke probe data, and Shutdown()
// appends to the same file. Unset in normal release runs so a loaded
// plugin leaves no filesystem footprint in %TEMP%.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using FormCast.Forms;
using FormCast.Forms.Layouts;
using FormCast.Internal;
using FormCast.Interop;
using FormCast.Threading;

using TakeCommand.Plugin;

namespace FormCast
{
    /// <summary>
    /// FormCast plugin entry point. Implements <see cref="ITCCPlugin"/> so
    /// the TCC v36 .NET plugin host can discover, load, and lifecycle this
    /// assembly.
    /// </summary>
    /// <remarks>
    /// The host instantiates exactly one instance of this class per loaded
    /// plugin assembly. Method order on the lifecycle is fixed:
    /// <see cref="GetPluginInfo"/> first (called once after assembly load),
    /// then <see cref="Initialize"/> (called once before any dispatch),
    /// then any number of dispatch calls into command/variable/function
    /// methods (none registered in v0.0.1), then <see cref="Shutdown"/>
    /// (called once at unload).
    /// </remarks>
    public sealed class Plugin : ITCCPlugin
    {
        // Opt-in diagnostic marker file. When FORMCAST_INIT_LOG is set to a
        // non-empty value, Initialize() overwrites %TEMP%\FormCast.init.log
        // with lifecycle and P/Invoke-probe data, and Shutdown()/error paths
        // append to the same file. In normal release runs the env var is
        // unset and all marker calls are no-ops, so the plugin leaves no
        // filesystem footprint in %TEMP%.
        private const string MarkerEnvVar = "FORMCAST_INIT_LOG";

        private static readonly string MarkerFilePath =
            Path.Combine(Path.GetTempPath(), "FormCast.init.log");

        /// <summary>
        /// True when the diagnostic marker file should be written. Controlled
        /// by the <c>FORMCAST_INIT_LOG</c> environment variable (any non-empty
        /// value enables). Read once per call so that setting the variable
        /// from outside the process takes effect on the next load cycle
        /// without requiring a rebuild.
        /// </summary>
        private static bool MarkerEnabled()
        {
            var v = Environment.GetEnvironmentVariable(MarkerEnvVar);
            return !string.IsNullOrEmpty(v);
        }

        /// <summary>
        /// The plugin version string returned by <see cref="f_FORMVERSION"/>.
        /// Kept in sync with the assembly version metadata in
        /// <c>FormCast.csproj</c>; both should be bumped together on every
        /// release.
        /// </summary>
        private static readonly string PluginVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private static readonly TCCPluginInfo PluginInfo = new TCCPluginInfo
        {
            Name = "FormCast",
            Author = "Tim Butterfield",
            // Email intentionally omitted from public plugin metadata.
            // Direct contact and issue reports go through the GitHub repo
            // linked in WWW below.
            Email = string.Empty,
            WWW = "https://github.com/Tim-Butterfield/FormCast",
            Description =
                "A .NET GUI framework for TCC batch scripts: native " +
                "Windows forms with events, layouts, JSONC templates, " +
                "and a visual designer.",

            // Comma-delimited list of TCC-visible names. Convention:
            //   no prefix  = command
            //   _prefix    = internal variable
            //   @prefix    = variable function (the C# method takes the
            //                'f_' prefix because '@' is not a valid
            //                identifier character; see PluginContract.cs)
            //   *prefix    = keystroke handler (NOT supported in .NET)
            Functions = "@FORMVERSION,@FORMOPEN,@FORMCLOSE,@FORMSTATE,@FORMADD," +
                        "@FORMSET,@FORMGET,@FORMSAVE,@FORMLOAD,@FORMSETENV," +
                        "@FORMCMD,@FORMSHOW,@FORMSAVEIMAGE,@FORMSIMULATE," +
                        "@FORMBIND,@FORMIMPORT,@FORMRELAYOUT," +
                        "@FORMTASKDIALOG,@FORMFOCUS,@FORMSENDMESSAGE,@FORMHITTEST," +
                        "@FORMAPPLYBINDINGS,@FORMCONSOLE," +
                        "@FORMOPENDIALOG,@FORMSAVEDIALOG,@FORMFOLDERDIALOG," +
                        "@FORMCOLORDIALOG,@FORMFONTDIALOG,@FORMNOTIFY,@FORMLOG," +
                        "@FORMSAVECOMPOSITE," +
                        "FORMEVENTS,FORMPIPE,FORMICONS",

            Major = 1,
            Minor = 0,
            Build = 100,
        };

        /// <summary>
        /// Live local-scope handle table. Allocated once per Plugin
        /// instance (the host creates exactly one per loaded assembly)
        /// and freed in <see cref="Shutdown"/>. The
        /// <see cref="LocalFormRegistry"/> is thread-safe so the
        /// callback worker thread, the GuiHostThread, and the script
        /// thread can all touch it without external locking.
        /// </summary>
        private readonly IFormRegistry _localRegistry = new LocalFormRegistry();

        /// <summary>
        /// Single-thread STA work queue that marshals GUI events back
        /// to a thread that owns its own re-entrancy model. Brought up
        /// at <see cref="Initialize"/> time and torn down at
        /// <see cref="Shutdown"/> time.
        /// </summary>
        private readonly CallbackWorker _callbackWorker = new CallbackWorker();

        /// <summary>
        /// Dedicated STA thread that runs <c>Application.Run</c> and
        /// owns every WinForms <c>Form</c> FormCast creates. Brought up
        /// at <see cref="Initialize"/> time; <c>FormRealizer</c> uses it
        /// to construct forms, and the forced-shutdown contract tears it
        /// down at <see cref="Shutdown"/> time.
        /// </summary>
        private readonly GuiHostThread _guiHost = new GuiHostThread();

        /// <summary>
        /// Map from registry handle to realized <see cref="Form"/>. A
        /// handle has an entry here once <c>@FORMSHOW</c> has been
        /// called against it (lazy realization). The dispatch surface
        /// that populates it is <see cref="f_FORMSHOW"/>.
        /// <see cref="Shutdown"/> iterates the map to enforce the
        /// forced-shutdown contract from PLUGIN_DESIGN.md section 4.6.
        /// </summary>
        private readonly Dictionary<int, Form> _realizedForms =
            new Dictionary<int, Form>();
        private readonly object _realizedFormsLock = new object();

        /// <summary>
        /// Per-form event queue map. One entry per realized form,
        /// populated lazily by <see cref="GetOrRealize"/> at the same
        /// time the realized <see cref="Form"/> is added to
        /// <see cref="_realizedForms"/>. The queue lives under the
        /// same lock so realize / close / shutdown atomically
        /// add / remove the (form, queue) pair. The FORMEVENTS
        /// streaming command drains these queues on the script thread.
        /// </summary>
        private readonly Dictionary<int, FormEventQueue> _eventQueues =
            new Dictionary<int, FormEventQueue>();

        /// <summary>
        /// Binding registry. Maps a (form handle, control id,
        /// event name) triple to the TCC command line that
        /// <c>@FORMBIND</c> registered for it. Lookup is case-
        /// insensitive on the control id and event name (the form
        /// handle is an opaque integer). Empty control id binds a
        /// form-level event such as <c>close</c>. The bound command
        /// runs on the <see cref="CallbackWorker"/> STA thread,
        /// scheduled by <see cref="DispatchBinding"/> after the
        /// <see cref="FormEventQueue.OnEnqueue"/> hook fires.
        /// </summary>
        private readonly Dictionary<BindingKey, string> _bindings =
            new Dictionary<BindingKey, string>();
        private readonly object _bindingsLock = new object();

        /// <summary>
        /// Test seam. When set, <see cref="DispatchBinding"/>
        /// invokes this delegate from the worker thread instead of
        /// calling <see cref="TakeCmd.Command"/>. Lets xUnit observe
        /// the bound command string and exercise re-entrancy paths
        /// without needing <c>TakeCmd.dll</c> in the test process.
        /// Production code leaves this null.
        /// </summary>
        internal Action<string>? TestCommandHook { get; set; }

        /// <summary>
        /// Test-only accessor: total number of <c>@FORMBIND</c>
        /// entries currently in the registry across all forms.
        /// </summary>
        internal int BindingCount
        {
            get
            {
                lock (_bindingsLock) { return _bindings.Count; }
            }
        }

        /// <summary>
        /// Composite key for <see cref="_bindings"/>. Equality is
        /// case-insensitive on <see cref="Ctrl"/> and <see cref="Evt"/>;
        /// <see cref="FormHandle"/> is compared bitwise.
        /// </summary>
        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            public int FormHandle { get; }
            public string Ctrl { get; }
            public string Evt { get; }

            public BindingKey(int formHandle, string? ctrl, string? evt)
            {
                FormHandle = formHandle;
                Ctrl = ctrl ?? string.Empty;
                Evt = evt ?? string.Empty;
            }

            public bool Equals(BindingKey other) =>
                FormHandle == other.FormHandle &&
                string.Equals(Ctrl, other.Ctrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Evt, other.Evt, StringComparison.OrdinalIgnoreCase);

            public override bool Equals(object? obj) =>
                obj is BindingKey k && Equals(k);

            public override int GetHashCode()
            {
                // Manual combine: targeting net48 means HashCode.Combine
                // is available via System.HashCode (yes on 4.8, but be
                // explicit for clarity). xor + multiply is plenty for a
                // small registry where collisions are cheap.
                int h = FormHandle;
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Ctrl);
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Evt);
                return h;
            }
        }

        /// <summary>
        /// Test-only accessor: number of forms currently in the
        /// realized-form map. Tests verify lazy realization and
        /// forced-shutdown teardown without taking a dependency on the
        /// marker file.
        /// </summary>
        internal int RealizedFormCount
        {
            get
            {
                lock (_realizedFormsLock)
                {
                    return _realizedForms.Count;
                }
            }
        }

        /// <summary>
        /// Test-only accessor: returns <c>true</c> if the given handle
        /// has a realized <see cref="Form"/> in the map.
        /// </summary>
        internal bool IsRealized(int handle)
        {
            lock (_realizedFormsLock)
            {
                return _realizedForms.ContainsKey(handle);
            }
        }

        /// <summary>
        /// Test-only accessor: returns the realized <see cref="Form"/>
        /// for the given handle, or <c>null</c> if the handle has no
        /// realized form. Tests use this to inspect WinForms state
        /// after a synthetic event has been dispatched via
        /// <see cref="f_FORMSIMULATE"/>.
        /// </summary>
        internal Form? TryGetRealizedForm(int handle)
        {
            lock (_realizedFormsLock)
            {
                return _realizedForms.TryGetValue(handle, out Form? f) ? f : null;
            }
        }

        /// <summary>
        /// Test-only accessor: the GUI host thread the plugin owns.
        /// Tests use this to marshal control-state reads back
        /// onto the GUI thread (cross-thread access throws otherwise).
        /// </summary>
        internal GuiHostThread GuiHost => _guiHost;

        /// <summary>
        /// Test peeker: look up a registered form descriptor by
        /// its sequence handle. xUnit tests use this to inspect
        /// per-control prop bag entries (e.g. <c>_lv.col.0</c>) without
        /// going through the dispatch surface.
        /// </summary>
        internal FormDescriptor? LookupDescriptor(int handle)
        {
            return _localRegistry.Lookup(handle);
        }

        /// <summary>
        /// Test-only accessor: returns the per-form event queue
        /// for the given handle, or <c>null</c> if the handle has
        /// not yet been realized. Tests inspect the queue directly
        /// to verify that WinForms event handlers fired the expected
        /// records; FORMEVENTS surfaces the same data via the
        /// streaming command.
        /// </summary>
        internal FormEventQueue? TryGetEventQueue(int handle)
        {
            lock (_realizedFormsLock)
            {
                return _eventQueues.TryGetValue(handle, out FormEventQueue? q) ? q : null;
            }
        }

        /// <inheritdoc />
        public TCCPluginInfo GetPluginInfo() => PluginInfo;

        // -----------------------------------------------------------------
        // Variable functions (TCC-visible @FORM* dispatch methods)
        // -----------------------------------------------------------------

        // TCC variable function calling convention used by FormCast:
        //
        //   - The dispatch method (f_FORMxxx) ALWAYS returns 0 from C#.
        //     Returning non-zero causes TCC to emit a noisy "System error N"
        //     to stderr, which is appropriate for unrecoverable plugin
        //     bugs but wrong for ordinary runtime failures (invalid handle,
        //     wrong arg count) that BTM scripts need to handle gracefully.
        //
        //   - Errors are signaled in the StringBuilder buffer instead.
        //     Each function documents its convention: e.g. @FORMOPEN
        //     returns the handle on success and an empty buffer on
        //     failure; @FORMSTATE returns the bitmask on success and "-1"
        //     on invalid handle; @FORMCLOSE returns "0" on success and
        //     a numeric error code on failure.
        //
        //   - BTM callers check the buffer value, not %_?:
        //         set h=%@formopen[form,settings,10,10,400,300]
        //         iff "%h" == "" echo open failed
        //         set rc=%@formclose[%h]
        //         iff "%rc" != "0" echo close failed
        //
        // Error codes follow PLUGIN_DESIGN.md (the 20000-20999 range is
        // reserved for FormCast). Specific codes used so far:
        //   20100  invalid handle (not parseable, or unknown)
        //   20101  bad arguments (wrong count, wrong type)
        //   20102  unknown control type
        //   20103  unknown control id
        //   20104  unknown form/control property
        //   20105  I/O or invocation failure (worker / GUI thread threw)
        //   20106  parse failure (JSON, format)
        //   20107  unknown @FORMSIMULATE action, or action not applicable
        //          to the target control type
        /// <summary>Handle string could not be parsed or is not in the registry.</summary>
        private const int ErrInvalidHandle = 20100;
        /// <summary>Wrong argument count or unparseable argument value.</summary>
        private const int ErrBadArguments = 20101;
        /// <summary>Control type token not in <see cref="ControlBuilders.RecognizedTypes"/>.</summary>
        private const int ErrUnknownControlType = 20102;
        /// <summary>Named control id not found on the form descriptor.</summary>
        private const int ErrUnknownControlId = 20103;
        /// <summary>Property name not recognized as a well-known form property.</summary>
        private const int ErrUnknownProperty = 20104;
        /// <summary>I/O failure, GUI thread exception, or worker thread fault.</summary>
        private const int ErrIoFailure = 20105;
        /// <summary>JSON parse error, template variable resolution failure, or format error.</summary>
        private const int ErrParseFailure = 20106;
        /// <summary>Unknown <c>@FORMSIMULATE</c> action or action not applicable to the control type.</summary>
        private const int ErrUnknownAction = 20107;

        /// <summary>
        /// Implementation of <c>@FORMVERSION[]</c>: returns the FormCast
        /// plugin version string.
        /// </summary>
        /// <param name="args">
        /// On entry: the contents of the function call's brackets, or
        /// empty for <c>%@FORMVERSION[]</c>. Currently ignored; future
        /// versions may accept format flags (e.g. <c>%@FORMVERSION[long]</c>
        /// for the full assembly version).
        /// </param>
        /// <returns>
        /// <c>0</c> on success. The version string is written back into
        /// <paramref name="args"/> for TCC to surface as the function's
        /// expansion result.
        /// </returns>
        /// <remarks>
        /// The C# method is named <c>f_FORMVERSION</c> with the <c>f_</c>
        /// prefix because <c>@</c> is not a valid C# identifier character;
        /// see PluginContract.cs for the full naming convention. The
        /// signature shape (StringBuilder argument, int return) is fixed
        /// by the JP Software .NET plugin SDK contract.
        /// </remarks>
        public int f_FORMVERSION(StringBuilder args)
        {
            args.Clear();
            args.Append(PluginVersion);
            return 0;
        }

        /// <summary>
        /// <c>@FORMLOG[level[,path]]</c>: configure plugin logging.
        /// Level: off, error, warn, info, debug, trace.
        /// Path: file path for the log (required when level != off).
        /// Returns 0 on success.
        /// </summary>
        public int f_FORMLOG(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length < 1)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            var level = Internal.PluginLogger.ParseLevel(parts[0]);
            string? path = parts.Length >= 2 ? parts[1] : null;

            Internal.PluginLogger.Configure(level, path);
            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMOPEN[type,name,x,y,w,h[,parent[,style]]]</c>:
        /// allocate a new form descriptor in the local registry and return
        /// the resulting handle string.
        /// </summary>
        /// <remarks>
        /// In the v0.0.x logical layer, "open" means "create the
        /// descriptor in memory." No WinForms <c>Form</c> is created.
        /// The realizer is what turns
        /// the descriptor into a real on-screen window.
        /// </remarks>
        public int f_FORMOPEN(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length < 6)
            {
                // Bad args: leave the buffer empty so the BTM caller can
                // detect the failure with `iff "%handle" == "" ...`.
                return 0;
            }

            var descriptor = new FormDescriptor
            {
                Type = parts[0],
                Name = parts[1],
                Title = parts[1], // default title is the name
                X = ArgParser.ParseIntOrDefault(parts[2], 0),
                Y = ArgParser.ParseIntOrDefault(parts[3], 0),
                Width = ArgParser.ParseIntOrDefault(parts[4], 0),
                Height = ArgParser.ParseIntOrDefault(parts[5], 0),
            };

            int seq = _localRegistry.Allocate(descriptor);
            string handle = FormHandle.Format(seq);
            args.Append(handle);
            Internal.PluginLogger.Info($"FORMOPEN {descriptor.Type} name={descriptor.Name} -> {handle}");
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMCLOSE[handle]</c>: free the handle
        /// from the local registry. The descriptor is dropped; in
        /// This also triggers the forced-close sequence on any realized
        /// form (PLUGIN_DESIGN.md section 4.6).
        /// </summary>
        public int f_FORMCLOSE(StringBuilder args)
        {
            string handle = args.ToString().Trim();
            args.Clear();
            Internal.PluginLogger.Info($"FORMCLOSE {handle}");

            if (!FormHandle.TryParse(handle, out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (_localRegistry.Free(seq))
            {
                // If the handle had a realized WinForms Form, tear it
                // down on the GUI thread. This is the ordinary-path
                // counterpart to the forced-shutdown sweep that runs
                // from Plugin.Shutdown.
                Form? realized = null;
                FormEventQueue? queue = null;
                lock (_realizedFormsLock)
                {
                    if (_realizedForms.TryGetValue(seq, out realized))
                    {
                        _realizedForms.Remove(seq);
                        _eventQueues.TryGetValue(seq, out queue);
                        _eventQueues.Remove(seq);
                    }
                }
                // Clean up designer state for this form
                _undoStacks.Remove(seq);
                // Explicitly enqueue the "close" event before tearing
                // down the form. WinForms FormClosing does not fire on
                // never-shown forms, so we can't rely on a wired
                // Form.FormClosing handler to produce this event in the
                // headless path. Enqueue BEFORE Destroy so any
                // concurrent FORMEVENTS drain reads it as the last
                // record on the form.
                queue?.Enqueue(new FormEvent(seq, string.Empty, "close", string.Empty));
                // Purge bindings AFTER the close event has been
                // enqueued, so a form-level close binding has had a
                // chance to fan out via DispatchBinding (which captures
                // the command string into the worker closure
                // synchronously). Purging earlier would silently drop
                // the user's :on_close handler.
                PurgeBindingsForHandle(seq);
                if (realized is not null)
                {
                    try
                    {
                        FormRealizer.Destroy(realized, _guiHost);
                    }
                    catch (Exception ex)
                    {
                        TryAppendMarker(
                            $"FORMCLOSE realized-form teardown threw for handle {seq}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }
                }
                args.Append('0');
            }
            else
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
            }
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMSTATE[handle]</c>: return a state
        /// bitmask for the named form. The bitmask is documented in
        /// PLUGIN_DESIGN.md section 4.1:
        /// <c>1</c>=visible <c>2</c>=enabled <c>4</c>=focused
        /// <c>8</c>=modal <c>16</c>=topmost.
        /// </summary>
        /// <remarks>
        /// In the v0.0.x logical layer there is no realized form, so
        /// most state bits are zero. We return <c>2</c> (enabled, the
        /// default state for a freshly-allocated descriptor) for any
        /// valid handle, and write <c>"-1"</c> into the buffer for an
        /// invalid handle so the BTM caller can distinguish "no such
        /// form" from "form is hidden and disabled" (state = 0). The
        /// The realizer queries the WinForms Form's
        /// Visible/Enabled/Focused properties when the form has been
        /// realized; otherwise falls back to descriptor-only state.
        /// </remarks>
        public int f_FORMSTATE(StringBuilder args)
        {
            string handle = args.ToString().Trim();
            args.Clear();

            if (!FormHandle.TryParse(handle, out int seq))
            {
                args.Append("-1");
                return 0;
            }

            FormDescriptor? d = _localRegistry.Lookup(seq);
            if (d is null)
            {
                args.Append("-1");
                return 0;
            }

            // Query the realized WinForms Form on the GUI thread for
            // Visible/Enabled/Focused. Bits per PLUGIN_DESIGN.md
            // section 4.1:
            //   1  = visible
            //   2  = enabled
            //   4  = focused
            //   8  = modal
            //   16 = topmost     (reserved)
            //   32 = events_pending (at least one event in the
            //        per-form FormEventQueue waiting to be drained
            //        via FORMEVENTS or @FORMBIND dispatch)
            //
            // Bit 32 is what makes `ON CONDITION %@formstate[%H] AND 32`
            // work in BTM scripts: TCC polls the expression between
            // commands and fires the handler the moment a new event
            // lands in the queue.
            //
            // If the form has not been realized yet, fall back to the
            // descriptor-only "enabled" bit.
            int bits = 2;  // Enabled, default for descriptor-only state
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is not null)
            {
                int snapshot = 0;
                _guiHost.Invoke(() =>
                {
                    if (realized.IsDisposed) { return; }
                    if (realized.Visible) { snapshot |= 1; }
                    if (realized.Enabled) { snapshot |= 2; }
                    if (realized.Focused) { snapshot |= 4; }
                    // Modal bit (8) reflects Form.Modal which is
                    // true between ShowDialog enter and exit. The
                    // Modal property is settable only via ShowDialog
                    // so this is the most reliable signal.
                    if (realized.Modal) { snapshot |= 8; }
                });
                bits = snapshot;
            }

            // Events_pending bit (32). Set when the per-form
            // event queue has at least one buffered event waiting to
            // be drained. The check is independent of realization
            // because Plugin tracks the queue separately and may
            // populate it from f_FORMSIMULATE without realizing.
            FormEventQueue? queue;
            lock (_realizedFormsLock)
            {
                _eventQueues.TryGetValue(seq, out queue);
            }
            if (queue is not null && queue.Count > 0)
            {
                bits |= 32;
            }

            args.Append(bits.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMADD[handle,ctrlid,type,x,y,w,h[,text]]</c>:
        /// add a control to a form using the absolute-position argument
        /// shape.
        /// </summary>
        /// <remarks>
        /// Result convention: the buffer is set to "0" on success, the
        /// error code on failure (20100 invalid handle, 20101 bad
        /// arguments, 20102 unknown control type). The C# return is
        /// always 0 to keep TCC quiet.
        /// </remarks>
        public int f_FORMADD(StringBuilder args)
        {
            string raw = args.ToString();
            string[] parts = ArgParser.Split(raw);
            args.Clear();
            Internal.PluginLogger.Debug($"FORMADD {raw}");

            // Absolute form requires 7 (no text) or 8 (with text)
            // positional arguments. The property-bag form (4 args)
            // (the property-bag form uses a different entry point).
            if (parts.Length != 7 && parts.Length != 8)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string ctrlid = parts[1];
            string type = parts[2];
            int x = ArgParser.ParseIntOrDefault(parts[3], 0);
            int y = ArgParser.ParseIntOrDefault(parts[4], 0);
            int width = ArgParser.ParseIntOrDefault(parts[5], 0);
            int height = ArgParser.ParseIntOrDefault(parts[6], 0);
            string text = parts.Length == 8 ? parts[7] : string.Empty;

            if (!ControlBuilders.IsRecognizedType(type))
            {
                args.Append(ErrUnknownControlType.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            // Parent/child id syntax. If the ctrlid contains a
            // '/', everything before the last slash is treated as the
            // path to the parent PANEL and the segment after the last
            // slash is the new control's id. Multi-level nesting works
            // because each path segment is resolved against the prior
            // panel's Children. The new control is added to the
            // resolved parent's Children list instead of the form's
            // top-level Controls list.
            string newId = ctrlid;
            string? parentPath = null;
            List<ControlDescriptor> targetCollection = form.Controls;
            int lastSlash = ctrlid.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                parentPath = ctrlid.Substring(0, lastSlash);
                newId = ctrlid.Substring(lastSlash + 1);
                ControlDescriptor? parent = ResolveNestedParent(form, parentPath);
                if (parent is null)
                {
                    args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                if (!IsContainerType(parent.Type))
                {
                    args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                targetCollection = parent.Children;
            }

            ControlDescriptor control = ControlBuilders.BuildAbsolute(
                type, newId, x, y, width, height, text);

            // The descriptor is mutable and the registry returns a
            // reference; appending to the resolved collection is the
            // entire descriptor-level realization.
            targetCollection.Add(control);

            // If the form is already realized (FORMSHOW was
            // called), also create the WinForms control and add it
            // to the live form. This is what makes the designer's
            // "add new control" workflow work without re-showing.
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is not null)
            {
                FormEventQueue? eq;
                lock (_realizedFormsLock)
                {
                    _eventQueues.TryGetValue(seq, out eq);
                }
                try
                {
                    // RealizeOneControl marshals to the GUI thread
                    // internally via host.Invoke.
                    System.Windows.Forms.Control? wfControl =
                        FormRealizer.RealizeOneControl(control, _guiHost, seq, eq);
                    if (wfControl is not null)
                    {
                        _guiHost.Invoke(() =>
                        {
                            if (realized.IsDisposed) { return; }
                            // Find the WinForms parent if nested.
                            if (parentPath is not null)
                            {
                                string lastSeg = parentPath;
                                int sl = parentPath.LastIndexOf('/');
                                if (sl >= 0) { lastSeg = parentPath.Substring(sl + 1); }
                                System.Windows.Forms.Control? wfParent =
                                    FindRealizedControl(realized, lastSeg);
                                if (wfParent is not null)
                                {
                                    wfParent.Controls.Add(wfControl);
                                    return;
                                }
                            }
                            realized.Controls.Add(wfControl);
                        });
                    }
                }
                catch (Exception ex)
                {
                    TryAppendMarker(
                        $"FORMADD live-add threw: {ex.GetType().Name}: {ex.Message}");
                }
            }

            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Walk a slash-separated path of PANEL ids starting
        /// from the form root, returning the leaf <see cref="ControlDescriptor"/>
        /// or <c>null</c> if any segment fails to resolve. Empty path
        /// returns null. Each path segment is matched
        /// case-insensitively against the prior level's children.
        /// </summary>
        /// <summary>
        /// Returns true if the given control type can hold children
        /// via the slash-id nesting syntax in f_FORMADD.
        /// </summary>
        private static bool IsContainerType(string? type)
        {
            if (string.IsNullOrEmpty(type)) { return false; }
            switch (type!.ToUpperInvariant())
            {
                case "PANEL":
                case "GROUPBOX":
                case "TABCONTROL":
                case "TABPAGE":
                case "SPLITCONTAINER":
                case "MENUSTRIP":
                case "CONTEXTMENU":
                case "TOOLBAR":
                case "STATUSBAR":
                case "FLOWPANEL":
                case "TABLEPANEL":
                // LABEL is a container so that menu items
                // (type LABEL under MENUSTRIP/CONTEXTMENU)
                // can have sub-item children. The descriptor
                // tree is just data; the realizer decides
                // how to interpret children.
                case "LABEL":
                    return true;
                default:
                    return false;
            }
        }

        private static ControlDescriptor? ResolveNestedParent(FormDescriptor form, string path)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            string[] segments = path.Split('/');
            ControlDescriptor? current = null;
            List<ControlDescriptor> level = form.Controls;
            foreach (string segment in segments)
            {
                ControlDescriptor? match = null;
                foreach (ControlDescriptor c in level)
                {
                    if (string.Equals(c.Id, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        match = c;
                        break;
                    }
                }
                if (match is null) { return null; }
                current = match;
                level = match.Children;
            }
            return current;
        }

        /// <summary>
        /// Implementation of <c>@FORMSET[handle,ctrl,prop,value]</c>:
        /// mutate a property on the form descriptor or one of its
        /// controls. Pass an empty <paramref name="args"/> control
        /// position (or "."` to denote the form itself) to address
        /// form-level properties.
        /// </summary>
        /// <remarks>
        /// Result convention: "0" on success; numeric error code on
        /// failure (20100 invalid handle, 20101 bad arguments, 20103
        /// unknown control id, 20104 unknown form property). Unknown
        /// control properties are accepted and stored in the control's
        /// property bag (this is the extension point for layout-manager
        /// hints like row=N, dock=top, etc.).
        /// </remarks>
        public int f_FORMSET(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length != 4)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            Internal.PluginLogger.Debug($"FORMSET {parts[0]} ctrl={parts[1]} prop={parts[2]} val={parts[3]}");

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string ctrl = parts[1];
            string prop = parts[2];
            string value = parts[3];

            if (string.IsNullOrEmpty(ctrl) || ctrl == ".")
            {
                if (!TrySetFormProperty(form, prop, value))
                {
                    args.Append(ErrUnknownProperty.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                // Live theme switching: re-apply theme to the
                // realized form when the theme property changes.
                if (string.Equals(prop, "theme", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyThemeLive(seq, form, value);
                }
                // runtimecontextmenu on the form root
                else if (string.Equals(prop, "runtimecontextmenu", StringComparison.OrdinalIgnoreCase))
                {
                    AttachRuntimeContextMenuToForm(seq, value);
                }
                // Show/hide a realized form
                else if (string.Equals(prop, "visible", StringComparison.OrdinalIgnoreCase))
                {
                    bool show = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    Form? rl;
                    lock (_realizedFormsLock) { _realizedForms.TryGetValue(seq, out rl); }
                    if (rl is not null)
                    {
                        _guiHost.Invoke(() => { if (!rl.IsDisposed) { rl.Visible = show; } });
                    }
                }
                // Clipboard operations for the designer.
                // value = control id for copy/cut, empty for paste.
                else if (string.Equals(prop, "clipcopy", StringComparison.OrdinalIgnoreCase))
                {
                    ClipboardCopy(form, value, false);
                }
                else if (string.Equals(prop, "clipcut", StringComparison.OrdinalIgnoreCase))
                {
                    ClipboardCopy(form, value, true);
                    // Delete the control(s) after copying
                    if (!string.IsNullOrEmpty(value))
                    {
                        ControlDescriptor? cutCtrl = FindControl(form, value);
                        if (cutCtrl is not null)
                        {
                            DeleteControl(seq, cutCtrl, form);
                        }
                    }
                    else
                    {
                        // Multi-select cut: delete all selected controls
                        string allSel = form.Properties.TryGetValue("selectedall", out string? sa2)
                            ? (sa2 ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrEmpty(allSel))
                        {
                            string[] cutIds = allSel.Split(CommandArgSeparators, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string cutId in cutIds)
                            {
                                ControlDescriptor? cc = FindControl(form, cutId);
                                if (cc is not null)
                                {
                                    DeleteControl(seq, cc, form);
                                }
                            }
                        }
                    }
                }
                else if (string.Equals(prop, "clippaste", StringComparison.OrdinalIgnoreCase))
                {
                    ClipboardPaste(seq, form);
                }
                // Undo/redo/snapshot for the designer
                else if (string.Equals(prop, "snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    GetUndoStack(seq).Push(FormSerializer.Serialize(form));
                }
                else if (string.Equals(prop, "undo", StringComparison.OrdinalIgnoreCase))
                {
                    string? state = GetUndoStack(seq).Undo();
                    if (state is not null) { RestoreFormState(seq, form, state); }
                }
                else if (string.Equals(prop, "redo", StringComparison.OrdinalIgnoreCase))
                {
                    string? state = GetUndoStack(seq).Redo();
                    if (state is not null) { RestoreFormState(seq, form, state); }
                }
                // Set WinForms Owner relationship so owned forms
                // activate together and stay above the owner.
                else if (string.Equals(prop, "owner", StringComparison.OrdinalIgnoreCase))
                {
                    Form? thisForm;
                    lock (_realizedFormsLock) { _realizedForms.TryGetValue(seq, out thisForm); }
                    if (thisForm is not null && FormHandle.TryParse(value, out int ownerSeq))
                    {
                        Form? ownerForm;
                        lock (_realizedFormsLock) { _realizedForms.TryGetValue(ownerSeq, out ownerForm); }
                        if (ownerForm is not null)
                        {
                            _guiHost.Invoke(() =>
                            {
                                if (!thisForm.IsDisposed && !ownerForm.IsDisposed)
                                {
                                    var iconBefore = ownerForm.Icon;
                                    thisForm.Owner = ownerForm;
                                    var iconAfter = ownerForm.Icon;
                                    Internal.PluginLogger.Debug(
                                        $"Owner set: owned={thisForm.Text} owner={ownerForm.Text} " +
                                        $"ownerIcon before={iconBefore?.Size} after={iconAfter?.Size} same={ReferenceEquals(iconBefore, iconAfter)}");
                                }
                            });
                        }
                    }
                }
                // Refresh design overlay after programmatic changes
                else if (string.Equals(prop, "refreshdesign", StringComparison.OrdinalIgnoreCase))
                {
                    _guiHost.Invoke(() => FormRealizer.RefreshDesignOverlay(seq));
                }
                // Live grid-size update for design mode
                else if (string.Equals(prop, "gridsize", StringComparison.OrdinalIgnoreCase))
                {
                    int gs = ParseInt(value);
                    _guiHost.Invoke(() => FormRealizer.SetDesignGridSize(seq, gs));
                }
                args.Append('0');
                return 0;
            }

            ControlDescriptor? c = FindControl(form, ctrl);
            if (c is null)
            {
                args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            SetControlProperty(c, prop, value, seq, form);
            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMGET[handle,ctrl,prop]</c>: read a
        /// property from a form descriptor or one of its controls. Pass
        /// an empty control id (or "."`) to read form-level properties.
        /// </summary>
        /// <remarks>
        /// Result convention: the value string on success, an empty
        /// buffer on any failure (invalid handle, unknown control,
        /// unknown property). The empty-buffer-on-miss convention lets
        /// BTM callers use <c>iff "%v" == "" ...</c>.
        /// </remarks>
        public int f_FORMGET(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length != 3) { return 0; }
            if (!FormHandle.TryParse(parts[0], out int seq)) { return 0; }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null) { return 0; }

            string ctrl = parts[1];
            string prop = parts[2];

            string? result;
            if (string.IsNullOrEmpty(ctrl) || ctrl == ".")
            {
                result = TryGetFormProperty(form, prop);
                if (string.Equals(prop, "selectedall", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(prop, "selectioncount", StringComparison.OrdinalIgnoreCase))
                {
                    Internal.PluginLogger.Debug($"FORMGET {parts[0]} ctrl=. prop={prop} -> [{result ?? "(null)"}]");
                }
            }
            else
            {
                ControlDescriptor? c = FindControl(form, ctrl);
                if (c is null)
                {
                    result = null;
                }
                else if (string.Equals(prop, "selecteditem", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(c.Type, "COMBOBOX", StringComparison.OrdinalIgnoreCase))
                    {
                        // COMBOBOX selecteditem reads the currently
                        // selected text from the realized ComboBox.
                        result = TryReadComboBoxSelectedItem(seq, c);
                    }
                    else
                    {
                        // LISTVIEW selecteditem reads the first
                        // non-icon column of the selected row.
                        result = TryReadListViewSelectedItem(seq, c);
                    }
                }
                else if (string.Equals(prop, "text", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(c.Type, "RICHMEMO", StringComparison.OrdinalIgnoreCase))
                {
                    // Text on a realized RICHMEMO reads the
                    // live WPF document content (with styling
                    // stripped). Falls back to the descriptor's
                    // stored Text when the form has not been
                    // realized.
                    result = TryReadRichMemoText(seq, c);
                }
                else if (string.Equals(prop, "text", StringComparison.OrdinalIgnoreCase) &&
                         (string.Equals(c.Type, "MEMO", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(c.Type, "EDIT", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(c.Type, "MASKEDTEXTBOX", StringComparison.OrdinalIgnoreCase)))
                {
                    // Text on a realized EDIT/MEMO/MASKEDTEXTBOX
                    // reads the live TextBox content (what the user
                    // actually typed). Falls back to the descriptor's
                    // stored Text when not realized.
                    result = TryReadMemoText(seq, c);
                }
                else if (string.Equals(prop, "checked", StringComparison.OrdinalIgnoreCase) &&
                         (string.Equals(c.Type, "TOGGLE", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(c.Type, "CHECKBOX", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(c.Type, "RADIO", StringComparison.OrdinalIgnoreCase)))
                {
                    result = TryReadCheckedState(seq, c);
                }
                else if (string.Equals(prop, "value", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(c.Type, "DATETIMEPICKER", StringComparison.OrdinalIgnoreCase))
                {
                    result = TryReadDateTimePickerValue(seq, c);
                }
                else if (string.Equals(prop, "value", StringComparison.OrdinalIgnoreCase) &&
                         (string.Equals(c.Type, "NUMERICUPDOWN", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(c.Type, "TRACKBAR", StringComparison.OrdinalIgnoreCase)))
                {
                    result = TryReadNumericValue(seq, c);
                }
                else
                {
                    result = TryGetControlProperty(c, prop);
                }
            }

            if (result is not null)
            {
                args.Append(result);
            }
            return 0;
        }

        /// <summary>
        /// Marshal to the GUI thread and read the selected
        /// LISTVIEW row's text from the first non-icon column. Returns
        /// the empty string when no row is selected, when the form has
        /// not been realized, or when the named control is not a
        /// ListView.
        /// </summary>
        private string TryReadListViewSelectedItem(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return string.Empty; }

            string selected = string.Empty;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is not System.Windows.Forms.ListView lv) { return; }
                if (lv.SelectedItems.Count == 0) { return; }
                System.Windows.Forms.ListViewItem item = lv.SelectedItems[0];

                // Determine the first non-icon column. Walk the
                // descriptor's _lv.col.N entries in order; the first
                // entry whose type is not "icon" is our column.
                int targetCol = 0;
                for (int i = 0; ; i++)
                {
                    string key = "_lv.col." + i.ToString(CultureInfo.InvariantCulture);
                    if (!c.Properties.TryGetValue(key, out string? colSpec)) { break; }
                    string[] parts2 = colSpec.Split('|');
                    string colType = parts2.Length >= 3 ? parts2[2].Trim() : "text";
                    if (!string.Equals(colType, "icon", StringComparison.OrdinalIgnoreCase))
                    {
                        targetCol = i;
                        break;
                    }
                }

                if (targetCol < item.SubItems.Count)
                {
                    selected = item.SubItems[targetCol].Text ?? string.Empty;
                }
            });
            return selected;
        }

        /// <summary>
        /// Read the selected item text from a realized
        /// ComboBox. Returns empty when not realized or no
        /// selection.
        /// </summary>
        private string TryReadComboBoxSelectedItem(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return string.Empty; }

            string selected = string.Empty;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is ComboBox cb && cb.SelectedItem is not null)
                {
                    selected = cb.SelectedItem.ToString() ?? string.Empty;
                }
            });
            return selected;
        }

        /// <summary>
        /// Read the live text of a realized MEMO (multiline
        /// TextBox). Falls back to the descriptor's Text when
        /// the form has not been realized.
        /// </summary>
        private string TryReadCheckedState(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            string fallback = c.Properties.TryGetValue("checked", out string? v)
                ? v ?? "false" : "false";
            if (realized is null) { return fallback; }

            string result = fallback;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is CheckBox cb)
                {
                    result = cb.Checked ? "true" : "false";
                }
                else if (target is RadioButton rb)
                {
                    result = rb.Checked ? "true" : "false";
                }
                else if (target is Forms.Controls.ToggleSwitch tgl)
                {
                    result = tgl.Checked ? "true" : "false";
                }
            });
            return result;
        }

        private string TryReadDateTimePickerValue(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            string fallback = c.Properties.TryGetValue("value", out string? v)
                ? v ?? string.Empty : string.Empty;
            if (realized is null) { return fallback; }

            string result = fallback;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is DateTimePicker dtp)
                {
                    result = dtp.Value.ToString("o", CultureInfo.InvariantCulture);
                }
            });
            return result;
        }

        /// <summary>
        /// Read the live numeric value of a realized NUMERICUPDOWN or
        /// TRACKBAR. Without this, FORMGET value falls through to the
        /// descriptor's stored "value" property and returns the initial
        /// value rather than what the user changed it to in the form.
        /// Falls back to the stored property when the form has not been
        /// realized.
        /// </summary>
        private string TryReadNumericValue(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            string fallback = c.Properties.TryGetValue("value", out string? v)
                ? v ?? string.Empty : string.Empty;
            if (realized is null) { return fallback; }

            string result = fallback;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is NumericUpDown nud)
                {
                    // Value is a decimal; format invariantly so a
                    // control configured with DecimalPlaces > 0 keeps
                    // its fractional part instead of being truncated.
                    result = nud.Value.ToString(CultureInfo.InvariantCulture);
                }
                else if (target is TrackBar tb)
                {
                    result = tb.Value.ToString(CultureInfo.InvariantCulture);
                }
            });
            return result;
        }

        private string TryReadMemoText(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return c.Text ?? string.Empty; }

            string text = c.Text ?? string.Empty;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is TextBox tb)
                {
                    text = tb.Text ?? string.Empty;
                }
            });
            return text;
        }

        /// <summary>
        /// Read the plain text of a realized RICHMEMO. Falls
        /// back to the descriptor's stored Text when the form has not
        /// been realized.
        /// </summary>
        private string TryReadRichMemoText(int seq, ControlDescriptor c)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return c.Text ?? string.Empty; }

            string text = c.Text ?? string.Empty;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is System.Windows.Forms.Integration.ElementHost host)
                {
                    text = Forms.Controls.RichMemoBuilder.GetPlainText(host);
                }
            });
            return text;
        }

        /// <summary>
        /// Marshal a RICHMEMO live operation onto the GUI
        /// thread. Looks up the realized form, finds the named
        /// ElementHost, and dispatches to the matching
        /// <see cref="Forms.Controls.RichMemoBuilder"/> helper.
        /// Silently no-ops when the form is not realized or the
        /// control is not a RICHMEMO ElementHost.
        /// </summary>
        private void ApplyRichMemoOp(int seq, string ctrlId, string op, string value)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, ctrlId);
                if (target is not System.Windows.Forms.Integration.ElementHost host) { return; }

                switch (op)
                {
                    case "appendcolor":
                    {
                        int bar = value.IndexOf('|');
                        if (bar < 0) { return; }
                        Forms.Controls.RichMemoBuilder.AppendColor(
                            host, value.Substring(0, bar), value.Substring(bar + 1));
                        break;
                    }
                    case "appendstyle":
                    {
                        int bar = value.IndexOf('|');
                        if (bar < 0) { return; }
                        Forms.Controls.RichMemoBuilder.AppendStyle(
                            host, value.Substring(0, bar), value.Substring(bar + 1));
                        break;
                    }
                    case "loadrules":
                        Forms.Controls.RichMemoBuilder.LoadRules(host, value);
                        break;
                    case "settext":
                        Forms.Controls.RichMemoBuilder.SetPlainText(host, value);
                        break;
                }
            });
        }

        /// <summary>
        /// Remove a control from the descriptor AND the
        /// realized form. Used by the designer's Delete action.
        /// </summary>
        private void DeleteControl(int seq, ControlDescriptor c, FormDescriptor form)
        {
            // Remove from the descriptor tree.
            RemoveDescriptorRecursive(form.Controls, c.Id);

            // Remove from the realized form if present.
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                // Clear design-mode selection if the deleted
                // control was selected (removes red border).
                FormRealizer.ClearDesignSelection(seq);
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is not null && target.Parent is not null)
                {
                    target.Parent.Controls.Remove(target);
                    target.Dispose();
                }
            });
        }

        private static bool RemoveDescriptorRecursive(
            List<ControlDescriptor> level, string id)
        {
            for (int i = 0; i < level.Count; i++)
            {
                if (string.Equals(level[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    level.RemoveAt(i);
                    return true;
                }
                if (level[i].Children.Count > 0 &&
                    RemoveDescriptorRecursive(level[i].Children, id))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Bring a realized control to front or send to back.
        /// </summary>
        private void ApplyZOrder(int seq, string ctrlId, bool toFront)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, ctrlId);
                if (target is null) { return; }
                if (toFront) { target.BringToFront(); }
                else { target.SendToBack(); }
            });
        }

        /// <summary>
        /// Set the tab index on a realized control.
        /// </summary>
        private void ApplyTabIndex(int seq, string ctrlId, int index)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, ctrlId);
                if (target is not null) { target.TabIndex = index; }
            });
        }

        /// <summary>
        /// Move a control from its current parent to a new
        /// parent container. Empty newParentId = form root.
        /// Updates both the descriptor tree and the realized form.
        /// </summary>
        private void ReparentControl(int seq, ControlDescriptor c,
            FormDescriptor form, string newParentId)
        {
            // Remove from current location in descriptor tree.
            RemoveDescriptorRecursive(form.Controls, c.Id);

            // Add to new parent.
            if (string.IsNullOrEmpty(newParentId))
            {
                form.Controls.Add(c);
            }
            else
            {
                ControlDescriptor? newParent = FindControlRecursive(form.Controls, newParentId);
                if (newParent is not null)
                {
                    newParent.Children.Add(c);
                }
                else
                {
                    // Parent not found; put back at root.
                    form.Controls.Add(c);
                }
            }

            // Update the realized form.
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is null) { return; }

                // Remove from current parent.
                if (target.Parent is not null)
                {
                    target.Parent.Controls.Remove(target);
                }

                // Add to new parent.
                if (string.IsNullOrEmpty(newParentId))
                {
                    realized.Controls.Add(target);
                }
                else
                {
                    System.Windows.Forms.Control? newParent =
                        FindRealizedControl(realized, newParentId);
                    if (newParent is not null)
                    {
                        newParent.Controls.Add(target);
                    }
                    else
                    {
                        realized.Controls.Add(target);
                    }
                }
            });
        }

        /// <summary>
        /// Attach a runtime-only ContextMenuStrip to a
        /// realized control (or the form itself when ctrlId is
        /// empty). The menu is found by name on the same realized
        /// form, or by "handle:ctrlid" cross-reference to another
        /// form. Since the attachment goes directly to the WinForms
        /// control's ContextMenuStrip property (not the descriptor),
        /// @FORMSAVE produces a clean template.
        /// </summary>
        private void AttachRuntimeContextMenu(int seq, string ctrlId, string menuSpec)
        {
            // Parse menuSpec: either a bare ctrlid (same form) or
            // "handle:ctrlid" for a cross-form reference.
            if (string.IsNullOrEmpty(menuSpec)) { return; }

            int targetSeq = seq;
            string menuCtrlId = menuSpec!;

            // Check for cross-form "L:pid:seq:ctrlid" pattern.
            // The handle has two colons already, so a third colon
            // after the handle separates the ctrlid.
            int thirdColon = -1;
            int colonCount = 0;
            for (int i = 0; i < menuSpec!.Length; i++)
            {
                if (menuSpec[i] == ':')
                {
                    colonCount++;
                    if (colonCount == 3) { thirdColon = i; break; }
                }
            }
            if (thirdColon > 0)
            {
                string handlePart = menuSpec.Substring(0, thirdColon);
                menuCtrlId = menuSpec.Substring(thirdColon + 1);
                if (FormHandle.TryParse(handlePart, out int parsedSeq))
                {
                    targetSeq = parsedSeq;
                }
            }

            Form? targetForm;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(targetSeq, out targetForm);
            }
            Form? thisForm;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out thisForm);
            }
            if (targetForm is null || thisForm is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (targetForm.IsDisposed || thisForm.IsDisposed) { return; }

                // Find the ContextMenuStrip control by name on the
                // target form (which may be a different form).
                ContextMenuStrip? cms = null;
                System.Windows.Forms.Control? menuControl =
                    FindRealizedControl(targetForm, menuCtrlId);
                if (menuControl is ContextMenuStrip found)
                {
                    cms = found;
                }
                else
                {
                    // Search the target form's non-child components
                    // (ContextMenuStrip is added to Controls but may
                    // also be found by name walk).
                    foreach (System.Windows.Forms.Control c in targetForm.Controls)
                    {
                        if (c is ContextMenuStrip strip &&
                            string.Equals(strip.Name, menuCtrlId, StringComparison.OrdinalIgnoreCase))
                        {
                            cms = strip;
                            break;
                        }
                    }
                }
                if (cms is null) { return; }

                // Attach to the target control (or the form itself).
                if (string.IsNullOrEmpty(ctrlId) || ctrlId == ".")
                {
                    thisForm.ContextMenuStrip = cms;
                }
                else
                {
                    System.Windows.Forms.Control? target =
                        FindRealizedControl(thisForm, ctrlId);
                    if (target is not null)
                    {
                        target.ContextMenuStrip = cms;
                    }
                }
            });
        }

        /// <summary>
        /// Set a ToolTip on a realized control. One ToolTip
        /// component is created per form and reused. If the form is
        /// not realized, the value is stored in the prop bag and
        /// applied when @FORMSHOW realizes the form.
        /// </summary>
        private void ApplyTooltip(int seq, string ctrlId, string text)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                // Find or create a ToolTip component on this form.
                // Store it in the form's Tag for reuse.
                ToolTip? tip = realized.Tag as ToolTip;
                if (tip is null)
                {
                    tip = new ToolTip();
                    realized.Tag = tip;
                }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, ctrlId);
                if (target is not null)
                {
                    tip.SetToolTip(target, text);
                    return;
                }
                // ToolStripItems are not Controls -- search ToolStrip.Items
                foreach (System.Windows.Forms.Control ctrl in realized.Controls)
                {
                    if (ctrl is ToolStrip ts)
                    {
                        foreach (ToolStripItem item in ts.Items)
                        {
                            if (string.Equals(item.Name, ctrlId, StringComparison.OrdinalIgnoreCase))
                            {
                                item.ToolTipText = text;
                                return;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Attach a <see cref="ContextMenuStrip"/> to the realized form
        /// itself (not a child control). The menu is resolved by control
        /// id on the same or a cross-referenced form. Since it goes
        /// directly to <c>Form.ContextMenuStrip</c>, it does not appear
        /// in the form's Controls collection and <c>@FORMSAVE</c>
        /// produces a clean template.
        /// </summary>
        // Designer clipboard: serialized ControlDescriptor JSON
        private string? _designerClipboard;
        private int _pasteCounter;

        /// <summary>
        /// Copy one or more controls to the designer clipboard.
        /// If <paramref name="ctrlId"/> is a single ID, copies that
        /// control and its children. If empty, copies all controls
        /// listed in the form's <c>selectedall</c> property (multi-
        /// select). If a selected child is already included via its
        /// parent's Children list, it is skipped to avoid duplicates.
        /// </summary>
        private void ClipboardCopy(FormDescriptor form, string ctrlId, bool isCut)
        {
            var tempForm = new FormDescriptor();

            if (!string.IsNullOrEmpty(ctrlId))
            {
                // Single control copy
                ControlDescriptor? ctrl = FindControl(form, ctrlId);
                if (ctrl is null) { return; }
                tempForm.Controls.Add(ctrl);
            }
            else
            {
                // Multi-select copy from selectedall
                string allSel = form.Properties.TryGetValue("selectedall", out string? sa)
                    ? (sa ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(allSel)) { return; }

                // Collect the selected IDs
                string[] ids = allSel.Split(CommandArgSeparators, StringSplitOptions.RemoveEmptyEntries);
                var selectedSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

                // Collect IDs that are children of other selected controls
                // to avoid duplicating them at top level
                var childOfSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in ids)
                {
                    ControlDescriptor? c = FindControl(form, id);
                    if (c is not null)
                    {
                        CollectChildIds(c.Children, selectedSet, childOfSelected);
                    }
                }

                foreach (string id in ids)
                {
                    if (childOfSelected.Contains(id)) { continue; }
                    ControlDescriptor? c = FindControl(form, id);
                    if (c is not null)
                    {
                        tempForm.Controls.Add(c);
                    }
                }
            }

            if (tempForm.Controls.Count == 0) { return; }
            _designerClipboard = FormSerializer.Serialize(tempForm);
        }

        /// <summary>
        /// Recursively find children of a control that are also
        /// in the selected set. These should be skipped at the
        /// top-level paste to avoid duplication.
        /// </summary>
        private static void CollectChildIds(
            List<ControlDescriptor> children,
            HashSet<string> selectedSet,
            HashSet<string> childOfSelected)
        {
            foreach (var c in children)
            {
                if (!string.IsNullOrEmpty(c.Id) && selectedSet.Contains(c.Id))
                {
                    childOfSelected.Add(c.Id);
                }
                if (c.Children.Count > 0)
                {
                    CollectChildIds(c.Children, selectedSet, childOfSelected);
                }
            }
        }

        /// <summary>
        /// Paste all controls from the designer clipboard onto the
        /// form. Each control (and its children) gets new IDs only
        /// if the original ID already exists on the form. This means
        /// cut+paste reuses the original ID, while copy+paste
        /// generates new ones.
        /// </summary>
        private void ClipboardPaste(int seq, FormDescriptor form)
        {
            if (string.IsNullOrEmpty(_designerClipboard)) { return; }
            // Deep copy via serialization round-trip
            FormDescriptor? temp = FormSerializer.Deserialize(_designerClipboard!);
            if (temp is null || temp.Controls.Count == 0) { return; }

            _pasteCounter++;
            int offset = 20 * _pasteCounter;

            // Collect all existing IDs on the form to avoid collisions
            var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectIds(form.Controls, existingIds);

            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            FormEventQueue? queue = null;
            if (realized is not null)
            {
                lock (_realizedFormsLock)
                {
                    _eventQueues.TryGetValue(seq, out queue);
                }
            }

            foreach (ControlDescriptor source in temp.Controls)
            {
                // Deep copy, renaming only IDs that collide
                ControlDescriptor pasted = DeepCopyWithNewIds(source, existingIds);
                pasted.X += offset;
                pasted.Y += offset;

                form.Controls.Add(pasted);

                // Realize the pasted control on the live form
                if (realized is not null)
                {
                    var ctrl = FormRealizer.RealizeOneControl(pasted, _guiHost, seq, queue);
                    if (ctrl is not null)
                    {
                        _guiHost.Invoke(() =>
                        {
                            if (!realized.IsDisposed)
                            {
                                realized.Controls.Add(ctrl);
                            }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Collect all control IDs recursively from a control list.
        /// </summary>
        private static void CollectIds(List<ControlDescriptor> controls, HashSet<string> ids)
        {
            foreach (var c in controls)
            {
                if (!string.IsNullOrEmpty(c.Id)) { ids.Add(c.Id); }
                if (c.Children.Count > 0) { CollectIds(c.Children, ids); }
            }
        }

        /// <summary>
        /// Deep copy a control descriptor with new unique IDs for
        /// the control and all its children. Strips the trailing
        /// digits from the original ID and increments until a
        /// unique name is found.
        /// </summary>
        private static ControlDescriptor DeepCopyWithNewIds(
            ControlDescriptor source, HashSet<string> existingIds)
        {
            var copy = new ControlDescriptor
            {
                Type = source.Type,
                Id = GenerateUniqueId(source.Id ?? "ctrl", existingIds),
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                Text = source.Text,
            };
            foreach (var kv in source.Properties)
            {
                copy.Properties[kv.Key] = kv.Value;
            }
            foreach (var child in source.Children)
            {
                copy.Children.Add(DeepCopyWithNewIds(child, existingIds));
            }
            return copy;
        }

        /// <summary>
        /// Generate a unique ID. If the original ID is not in the
        /// existing set, reuses it (enables cut+paste to preserve
        /// the original name). Otherwise, strips trailing digits
        /// and increments until a unique name is found.
        /// </summary>
        private static string GenerateUniqueId(string originalId, HashSet<string> existingIds)
        {
            // If the original ID is available, reuse it (cut+paste case)
            if (existingIds.Add(originalId))
            {
                return originalId;
            }
            // Strip trailing digits to get the base
            string baseName = originalId.TrimEnd("0123456789".ToCharArray());
            if (string.IsNullOrEmpty(baseName)) { baseName = "ctrl"; }
            int counter = 1;
            string candidate;
            do
            {
                counter++;
                candidate = baseName + counter.ToString(CultureInfo.InvariantCulture);
            } while (existingIds.Contains(candidate));
            existingIds.Add(candidate);
            return candidate;
        }

        // Per-form undo stacks for the designer.
        private readonly Dictionary<int, Forms.Controls.UndoStack> _undoStacks =
            new Dictionary<int, Forms.Controls.UndoStack>();

        private Forms.Controls.UndoStack GetUndoStack(int handle)
        {
            if (!_undoStacks.TryGetValue(handle, out var stack))
            {
                stack = new Forms.Controls.UndoStack();
                _undoStacks[handle] = stack;
            }
            return stack;
        }

        /// <summary>
        /// Restore a form's descriptor state from a JSONC snapshot
        /// (used by undo/redo). Deserializes the snapshot, copies
        /// fields back into the existing descriptor, and re-realizes.
        /// </summary>
        private void RestoreFormState(int seq, FormDescriptor current, string jsonSnapshot)
        {
            FormDescriptor? restored = FormSerializer.Deserialize(jsonSnapshot);
            if (restored is null) { return; }

            // Copy restored state into the current descriptor
            current.Title = restored.Title;
            current.X = restored.X;
            current.Y = restored.Y;
            current.Width = restored.Width;
            current.Height = restored.Height;
            current.LayoutMode = restored.LayoutMode;
            current.Controls.Clear();
            current.Controls.AddRange(restored.Controls);
            current.Properties.Clear();
            foreach (var kv in restored.Properties)
            {
                current.Properties[kv.Key] = kv.Value;
            }

            // Re-realize: destroy and rebuild the form
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            // Destroy old form and realize new one
            FormEventQueue? queue;
            lock (_realizedFormsLock)
            {
                _eventQueues.TryGetValue(seq, out queue);
            }

            _guiHost.Invoke(() =>
            {
                if (!realized.IsDisposed)
                {
                    var pos = realized.Location;
                    bool wasVisible = realized.Visible;
                    // Disable confirmclose before destroying the old
                    // form so the FormClosing handler does not enqueue
                    // a "closing" event that the BTM would interpret
                    // as a user exit intent.
                    current.Properties.Remove("confirmclose");
                    FormRealizer.Destroy(realized, _guiHost);

                    var newForm = FormRealizer.Realize(current, _guiHost, seq, queue);
                    lock (_realizedFormsLock)
                    {
                        _realizedForms[seq] = newForm;
                    }
                    newForm.Location = pos;
                    if (wasVisible) { newForm.Show(); }
                }
            });
        }

        private void AttachRuntimeContextMenuToForm(int seq, string menuSpec)
        {
            // Reuse the cross-form parsing from AttachRuntimeContextMenu
            // but attach to Form.ContextMenuStrip instead of a control.
            if (string.IsNullOrEmpty(menuSpec)) { return; }

            int targetSeq = seq;
            string menuCtrlId = menuSpec!;
            int thirdColon = -1;
            int colonCount = 0;
            for (int i = 0; i < menuSpec!.Length; i++)
            {
                if (menuSpec[i] == ':')
                {
                    colonCount++;
                    if (colonCount == 3) { thirdColon = i; break; }
                }
            }
            if (thirdColon > 0)
            {
                string handlePart = menuSpec.Substring(0, thirdColon);
                menuCtrlId = menuSpec.Substring(thirdColon + 1);
                if (FormHandle.TryParse(handlePart, out int parsedSeq))
                {
                    targetSeq = parsedSeq;
                }
            }

            Form? targetForm;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(targetSeq, out targetForm);
            }
            Form? thisForm;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out thisForm);
            }
            if (targetForm is null || thisForm is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (targetForm.IsDisposed || thisForm.IsDisposed) { return; }
                System.Windows.Forms.Control? menuControl =
                    FindRealizedControl(targetForm, menuCtrlId);
                if (menuControl is ContextMenuStrip cms)
                {
                    thisForm.ContextMenuStrip = cms;
                }
            });
        }

        /// <summary>
        /// Wire a realized <see cref="PropertyGrid"/> control to a
        /// <see cref="Forms.Controls.DesignPropertyAdapter"/> so the designer can edit
        /// form/control properties interactively. Changes in the grid
        /// flow through the adapter's <c>PropertyChanged</c> event back
        /// into <see cref="SetControlProperty"/> / <see cref="TrySetFormProperty"/>
        /// so the realized canvas updates in real time.
        /// </summary>
        private void BindPropertyGrid(int seq, string? pgCtrlId, string targetSpec)
        {
            // targetSpec = "handle:ctrlId" or "handle:." for form
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null || string.IsNullOrEmpty(pgCtrlId)) { return; }

            // Parse target handle and control id
            int targetSeq = seq;
            string targetCtrlId = targetSpec ?? ".";
            int thirdColon = -1;
            int colonCount = 0;
            for (int i = 0; i < targetSpec!.Length; i++)
            {
                if (targetSpec[i] == ':')
                {
                    colonCount++;
                    if (colonCount == 3) { thirdColon = i; break; }
                }
            }
            if (thirdColon > 0)
            {
                string handlePart = targetSpec.Substring(0, thirdColon);
                targetCtrlId = targetSpec.Substring(thirdColon + 1);
                if (FormHandle.TryParse(handlePart, out int parsedSeq))
                {
                    targetSeq = parsedSeq;
                }
            }

            FormDescriptor? targetForm = _localRegistry.Lookup(targetSeq);
            if (targetForm is null) { return; }

            Form? targetRealized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(targetSeq, out targetRealized);
            }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? pgControl =
                    FindRealizedControl(realized, pgCtrlId!);
                if (pgControl is not PropertyGrid pg) { return; }

                FormCast.Forms.Controls.DesignPropertyAdapter adapter;
                if (string.IsNullOrEmpty(targetCtrlId) || targetCtrlId == ".")
                {
                    adapter = new FormCast.Forms.Controls.DesignPropertyAdapter(targetForm);
                }
                else
                {
                    ControlDescriptor? cd = FindControlDescriptor(targetForm, targetCtrlId);
                    if (cd is null) { return; }
                    adapter = new FormCast.Forms.Controls.DesignPropertyAdapter(cd);
                }

                // Wire property changes to apply live to the
                // realized canvas control.
                int tSeq = targetSeq;
                adapter.PropertyChanged += (prop, value) =>
                {
                    // Push the change through the normal FORMSET
                    // path so the realized control updates.
                    if (targetCtrlId == ".")
                    {
                        TrySetFormProperty(targetForm, prop, value);
                        if (string.Equals(prop, "theme", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyThemeLive(tSeq, targetForm, value);
                        }
                    }
                    else
                    {
                        ControlDescriptor? cd2 = FindControlDescriptor(targetForm, targetCtrlId);
                        if (cd2 is not null)
                        {
                            SetControlProperty(cd2, prop, value, tSeq, targetForm);
                        }
                    }
                };

                // Force full rebuild: clear, reassign, and
                // toggle visibility to ensure WinForms rebuilds
                // the property list from scratch.
                pg.SelectedObject = null;
                pg.SelectedObject = adapter;
                pg.Visible = false;
                pg.Visible = true;
            });
        }

        private static ControlDescriptor? FindControlDescriptor(FormDescriptor form, string id)
        {
            if (id.IndexOf('/') >= 0)
            {
                return ResolveNestedParent(form, id);
            }
            return FindControlRecursive(form.Controls, id);
        }

        /// <summary>
        /// Read text from TCC environment variables and set it as the
        /// control's text. Supports three patterns:
        /// (1) <c>varName_count</c> + <c>varName[0..N]</c> -- explicit array;
        /// (2) <c>varName[0]</c>...<c>varName[N]</c> -- implicit array (until null);
        /// (3) plain <c>varName</c> -- single value.
        /// Array elements are joined with <see cref="Environment.NewLine"/>
        /// so MEMO controls display one element per line.
        /// </summary>
        private void ApplyTextFromVar(int seq, ControlDescriptor c, string varName)
        {
            if (string.IsNullOrEmpty(varName)) { return; }

            var lines = new System.Collections.Generic.List<string>();

            // Check for array count: varName_count or varName.count
            string? countStr = Environment.GetEnvironmentVariable(varName + "_count");
            if (countStr is not null &&
                int.TryParse(countStr, System.Globalization.NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int count) && count > 0)
            {
                // Array with explicit count -- null elements become empty lines
                for (int i = 0; i < count; i++)
                {
                    string? elem = Environment.GetEnvironmentVariable(
                        varName + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                    lines.Add(elem ?? string.Empty);
                }
            }
            else
            {
                // Try as array without count: read until null
                string? first = Environment.GetEnvironmentVariable(varName + "[0]");
                if (first is not null)
                {
                    lines.Add(first);
                    for (int i = 1; i < 1000; i++)
                    {
                        string? elem = Environment.GetEnvironmentVariable(
                            varName + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                        if (elem is null) { break; }
                        lines.Add(elem);
                    }
                }
                else
                {
                    // Single variable
                    string? val = Environment.GetEnvironmentVariable(varName);
                    if (val is not null) { lines.Add(val); }
                }
            }

            if (lines.Count == 0) { return; }

            string text = string.Join(Environment.NewLine, lines);

            // Apply to the descriptor
            c.Text = text;

            // Apply to the realized control
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(c.Id) ? null : FindRealizedControl(realized, c.Id!);
                if (target is not null)
                {
                    target.Text = text;
                    if (target is TextBoxBase tbb)
                    {
                        tbb.SelectionStart = 0;
                        tbb.ScrollToCaret();
                    }
                }
            });
        }

        /// <summary>
        /// Read the entire contents of a file and set it as the control's
        /// text (both the descriptor and the realized control). Silently
        /// no-ops on any I/O exception so a bad path does not crash the
        /// plugin.
        /// </summary>
        private void ApplyTextFromFile(int seq, ControlDescriptor c, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) { return; }
            string text;
            try { text = System.IO.File.ReadAllText(filePath); }
            catch { return; }

            c.Text = text;

            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(c.Id) ? null : FindRealizedControl(realized, c.Id!);
                if (target is not null)
                {
                    target.Text = text;
                    if (target is TextBoxBase tbb)
                    {
                        tbb.SelectionStart = 0;
                        tbb.ScrollToCaret();
                    }
                }
            });
        }

        /// <summary>
        /// Set or clear a stock icon on a realized Button, Label, or PictureBox.
        /// Looks up the icon by name from <see cref="Forms.Controls.StockIcons"/>
        /// and delegates to <see cref="FormRealizer.ApplyStockIcon"/> on the
        /// GUI thread.
        /// </summary>
        private void ApplyLiveStockIcon(int seq, string? ctrlId, string iconName)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is null) { return; }

                if (string.IsNullOrEmpty(iconName))
                {
                    // Clear the icon
                    if (target is ButtonBase bb2) { bb2.Image = null; }
                    else if (target is Label lbl2) { lbl2.Image = null; }
                    else if (target is PictureBox pb2) { pb2.Image = null; }
                    return;
                }

                System.Drawing.Image? icon = Forms.Controls.StockIcons.Get(iconName);
                if (icon is null) { return; }
                FormRealizer.ApplyStockIcon(target, icon);
            });
        }

        /// <summary>
        /// Set the splitter distance on a realized <see cref="SplitContainer"/>.
        /// Clamps the value to prevent the splitter from being pushed off
        /// the visible area (which would throw).
        /// </summary>
        private void ApplyLiveSplitterDistance(int seq, string? ctrlId, string value)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is SplitContainer sc)
                {
                    int dist = 100;
                    int.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out dist);
                    if (dist > 0 && dist < sc.Height - 25)
                    {
                        sc.SplitterDistance = dist;
                    }
                }
            });
        }

        /// <summary>
        /// Toggle <see cref="ScrollableControl.AutoScroll"/> on a realized
        /// Panel or other scrollable container. Truthy values: "1", "true".
        /// </summary>
        private void ApplyLiveAutoScroll(int seq, string? ctrlId, string value)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is ScrollableControl sc)
                {
                    bool flag = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    sc.AutoScroll = flag;
                }
            });
        }

        /// <summary>
        /// Push a numeric or boolean property (value, min, max, checked)
        /// to the realized control. Dispatches by control type because
        /// ProgressBar, TrackBar, NumericUpDown, ScrollBar, CheckBox, and
        /// ToggleSwitch all expose these properties but with different
        /// CLR types and clamping rules.
        /// </summary>
        private void ApplyLiveProperty(int seq, string? ctrlId, string prop, string value)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is null) { return; }
                int intVal = 0;
                int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out intVal);
                string lower = prop.ToLowerInvariant();
                switch (lower)
                {
                    case "value":
                        if (target is ProgressBar pb) { pb.Value = Math.Max(pb.Minimum, Math.Min(pb.Maximum, intVal)); }
                        else if (target is TrackBar tb) { tb.Value = Math.Max(tb.Minimum, Math.Min(tb.Maximum, intVal)); }
                        else if (target is NumericUpDown nud) { nud.Value = Math.Max(nud.Minimum, Math.Min(nud.Maximum, intVal)); }
                        else if (target is ScrollBar sb) { sb.Value = Math.Max(sb.Minimum, Math.Min(sb.Maximum, intVal)); }
                        else if (target is DateTimePicker dtp &&
                                 DateTime.TryParse(value, CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind, out DateTime dtVal))
                        {
                            dtp.Value = dtVal;
                        }
                        break;
                    case "min":
                        if (target is ProgressBar pb2) { pb2.Minimum = intVal; }
                        else if (target is TrackBar tb2) { tb2.Minimum = intVal; }
                        else if (target is NumericUpDown nud2) { nud2.Minimum = intVal; }
                        break;
                    case "max":
                        if (target is ProgressBar pb3) { pb3.Maximum = intVal; }
                        else if (target is TrackBar tb3) { tb3.Maximum = intVal; }
                        else if (target is NumericUpDown nud3) { nud3.Maximum = intVal; }
                        break;
                    case "checked":
                        bool flag = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                        if (target is CheckBox cb) { cb.Checked = flag; }
                        else if (target is RadioButton rb) { rb.Checked = flag; }
                        else if (target is Forms.Controls.ToggleSwitch tgl) { tgl.Checked = flag; }
                        break;
                }
            });
        }

        /// <summary>
        /// Set the position and size of a realized control in one call.
        /// Used by the position/size pseudo-props to avoid separate
        /// Location and Size assignments that would cause two layout passes.
        /// </summary>
        private void ApplyLiveBounds(int seq, string? ctrlId, int x, int y, int w, int h)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is not null)
                {
                    target.SetBounds(x, y, w, h);
                }
            });
        }

        /// <summary>
        /// Set the Text property on a realized control. For TextBox-derived
        /// controls, also resets the caret to position 0 and scrolls to
        /// the top so the user sees the beginning of long text.
        /// </summary>
        private void ApplyLiveText(int seq, string? ctrlId, string text)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is not null)
                {
                    // Setting text to empty on a TreeView clears all nodes
                    if (target is TreeView tvClear && string.IsNullOrEmpty(text))
                    {
                        tvClear.Nodes.Clear();
                    }
                    else
                    {
                        target.Text = text;
                    }
                    if (target is TextBoxBase tbb)
                    {
                        tbb.SelectionStart = 0;
                        tbb.ScrollToCaret();
                    }
                }
            });
        }

        /// <summary>
        /// Re-apply a theme to an already-realized form when the theme
        /// property is changed at runtime via <c>@FORMSET[h,.,theme,dark]</c>.
        /// Delegates to <see cref="FormRealizer.ApplyThemeLive"/> on the
        /// GUI thread.
        /// </summary>
        private void ApplyThemeLive(int seq, FormDescriptor form, string themeValue)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                FormRealizer.ApplyThemeLive(realized, form, themeValue);
            });
        }

        /// <summary>
        /// Parse a color spec (named color or <c>#RRGGBB</c>) and apply
        /// it as the BackColor or ForeColor of a realized control. For
        /// ToolStrip-family controls, switches the render mode to
        /// Professional so the custom BackColor is actually visible
        /// (the default System renderer ignores BackColor). Also searches
        /// ToolStripItem collections for status bar labels and toolbar
        /// buttons that are not in the form's Controls tree.
        /// </summary>
        private void ApplyColor(int seq, string? ctrlId, string prop, string colorSpec)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Drawing.Color? c = FormRealizer.ParseColorPublic(colorSpec);
                if (!c.HasValue) { return; }
                bool isBg = string.Equals(prop, "backcolor", StringComparison.OrdinalIgnoreCase);

                System.Windows.Forms.Control? target =
                    string.IsNullOrEmpty(ctrlId) ? null : FindRealizedControl(realized, ctrlId!);
                if (target is not null)
                {
                    if (isBg)
                    {
                        target.BackColor = c.Value;
                        // ToolStrip/StatusStrip need Professional
                        // renderer for BackColor to be visible.
                        if (target is ToolStrip ts &&
                            ts.RenderMode != ToolStripRenderMode.Professional)
                        {
                            ts.RenderMode = ToolStripRenderMode.Professional;
                        }
                    }
                    else
                    {
                        target.ForeColor = c.Value;
                    }
                    return;
                }
                // Target not in Controls -- check ToolStrip.Items
                // (StatusBar labels, menu items, toolbar buttons).
                foreach (System.Windows.Forms.Control ctrl in realized.Controls)
                {
                    if (ctrl is ToolStrip ts2)
                    {
                        foreach (ToolStripItem item in ts2.Items)
                        {
                            if (string.Equals(item.Name, ctrlId, StringComparison.OrdinalIgnoreCase))
                            {
                                if (isBg) { item.BackColor = c.Value; }
                                else { item.ForeColor = c.Value; }
                                return;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Append a text line to a realized MEMO or RICHMEMO
        /// control on the GUI thread. For MEMO (TextBox with
        /// Multiline=true) calls <c>AppendText</c>; for RICHMEMO
        /// calls <c>RichMemoBuilder.AppendColor</c> with no color
        /// (plain text). No-ops when the form is not realized.
        /// </summary>
        private void AppendTextToControl(int seq, ControlDescriptor c, string text)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, c.Id);
                if (target is TextBox tb && tb.Multiline)
                {
                    int pos = tb.SelectionStart;
                    tb.AppendText(Environment.NewLine + text);
                    tb.SelectionStart = pos;
                    tb.ScrollToCaret();
                }
                else if (target is System.Windows.Forms.Integration.ElementHost host)
                {
                    Forms.Controls.RichMemoBuilder.AppendColor(host, text + Environment.NewLine, "Black");
                }
            });
        }

        /// <summary>
        /// Append a text line with an optional color to a
        /// realized MEMO or RICHMEMO. Used by the <see cref="FORMPIPE"/>
        /// command for each stdin line.
        /// </summary>
        private void AppendLineToControl(int seq, string ctrlId, string line, string? color)
        {
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return; }

            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                System.Windows.Forms.Control? target = FindRealizedControl(realized, ctrlId);
                if (target is TextBox tb && tb.Multiline)
                {
                    tb.AppendText(line + Environment.NewLine);
                }
                else if (target is System.Windows.Forms.Integration.ElementHost host)
                {
                    string c = string.IsNullOrEmpty(color) ? "Black" : color!;
                    Forms.Controls.RichMemoBuilder.AppendColor(host, line + Environment.NewLine, c);
                }
            });
        }

        // -----------------------------------------------------------------
        // FORMPIPE streaming command
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // FORMICONS command -- list all stock icons with categories
        // -----------------------------------------------------------------

        /// <summary>
        /// Implementation of <c>FORMICONS [filter]</c>: write all stock
        /// icon names to stdout, one per line, in the format
        /// <c>category:name</c>. Category headers are written as
        /// <c>@category</c> (prefixed with @) so the BTM can
        /// distinguish them from icon entries.
        /// Consumed from BTM via <c>do line in /p FORMICONS</c>.
        /// Optional filter argument restricts output to icons whose
        /// name or category contains the filter string.
        /// </summary>
        public int FORMICONS(StringBuilder args)
        {
            string filter = args.ToString().Trim();
            args.Clear();

            string lastCat = "";
            foreach (var (cat, name) in Forms.Controls.StockIcons.CategorizedNames)
            {
                // Apply filter if provided
                if (filter.Length > 0 &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    cat.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Emit category header when category changes
                if (!string.Equals(cat, lastCat, StringComparison.Ordinal))
                {
                    Interop.TakeCmd.WriteStdOut("@" + cat + "\r\n");
                    lastCat = cat;
                }
                Interop.TakeCmd.WriteStdOut(cat + ":" + name + "\r\n");
            }
            return 0;
        }

        /// <summary>
        /// Implementation of <c>FORMPIPE handle ctrlid [color]</c>:
        /// read stdin line by line and append to a MEMO or RICHMEMO.
        /// </summary>
        public int FORMPIPE(StringBuilder args)
        {
            string raw = args.ToString().Trim();
            args.Clear();

            // Commands are space-delimited (TCC convention).
            string[] parts = raw.Split(
                CommandArgSeparators,
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || parts.Length > 3)
            {
                return ErrBadArguments;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                return ErrInvalidHandle;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                return ErrInvalidHandle;
            }

            string ctrlId = parts[1];
            string? color = parts.Length >= 3 ? parts[2] : null;

            // Verify the control exists on the descriptor.
            ControlDescriptor? ctrl = FindControl(form, ctrlId);
            if (ctrl is null)
            {
                return ErrUnknownControlId;
            }

            // Read stdin line by line. TCC pipes feed our stdin
            // through the standard Console.In handle. EOF signals
            // the upstream command has finished.
            try
            {
                string? line;
                while ((line = Console.In.ReadLine()) is not null)
                {
                    AppendLineToControl(seq, ctrlId, line, color);
                }
            }
            catch (IOException)
            {
                // Pipe broken: upstream died. Not an error for us.
            }
            catch (ObjectDisposedException)
            {
                // Console closed during shutdown.
            }

            return 0;
        }

        /// <summary>
        /// Recursive search for a realized WinForms control by
        /// <c>Name</c> (case-insensitive). Also checks
        /// <c>Form.ContextMenuStrip</c> because ContextMenuStrip is
        /// stored there (not in <c>Form.Controls</c>) to prevent
        /// layout disruption -- WinForms treats ContextMenuStrip as a
        /// ToolStrip that auto-docks when added to Controls.
        /// </summary>
        private static System.Windows.Forms.Control? FindRealizedControl(
            System.Windows.Forms.Control parent, string name)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
                System.Windows.Forms.Control? nested = FindRealizedControl(c, name);
                if (nested is not null) { return nested; }
            }
            // ContextMenuStrip is stored as Form.ContextMenuStrip
            // (not in Controls) to avoid layout disruption.
            if (parent is Form frm && frm.ContextMenuStrip is not null &&
                string.Equals(frm.ContextMenuStrip.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return frm.ContextMenuStrip;
            }
            return null;
        }

        /// <summary>
        /// Implementation of <c>@FORMSAVE[handle,file]</c>: serialize
        /// the form descriptor to a JSON template file.
        /// </summary>
        /// <remarks>
        /// Result convention: "0" on success; numeric error code on
        /// failure (20100 invalid handle, 20101 bad arguments, 20105
        /// I/O failure). The runtime handle is intentionally not
        /// recorded in the file; <c>@FORMLOAD</c> always allocates a
        /// fresh handle.
        /// </remarks>
        public int f_FORMSAVE(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length != 2)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string path = Path.GetFullPath(parts[1]);
            try
            {
                string json = FormSerializer.Serialize(form);
                File.WriteAllText(path, json, Encoding.UTF8);
                args.Append('0');
            }
            catch (IOException)
            {
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
            }
            catch (UnauthorizedAccessException)
            {
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
            }
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMLOAD[file]</c>: deserialize a JSON
        /// template file into a new form descriptor and register it.
        /// </summary>
        /// <remarks>
        /// Result convention: the new handle string on success, an
        /// empty buffer on failure (file not found, parse error,
        /// I/O error). The empty-buffer convention matches
        /// <c>@FORMOPEN</c>.
        /// </remarks>
        public int f_FORMLOAD(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            // Optional second arg is the BTM-side vars string
            // (key=value|key=value|...) consumed by the template's
            // ${var} substitution.
            if (parts.Length < 1 || parts.Length > 2) { return 0; }

            string path = Path.GetFullPath(parts[0]);
            Dictionary<string, string>? vars = parts.Length == 2
                ? FormSerializer.ParseVars(parts[1])
                : null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                FormDescriptor form = FormSerializer.Deserialize(json, vars);
                int seq = _localRegistry.Allocate(form);
                args.Append(FormHandle.Format(seq));
            }
            catch (IOException ex)
            {
                // Empty buffer signals failure to BTM caller.
                TryAppendMarker(
                    $"FORMLOAD I/O failure for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                TryAppendMarker(
                    $"FORMLOAD access denied for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
            catch (FormatException ex)
            {
                // Parser error: leave buffer empty so caller can detect.
                TryAppendMarker(
                    $"FORMLOAD parse failure for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMIMPORT[handle,file]</c>: load a
        /// template file via <see cref="FormSerializer.Deserialize(string)"/>
        /// and APPEND its controls to the existing form descriptor at
        /// <c>handle</c>. Form-level fields of the imported template
        /// (title, position, size, layout mode, property bag) are
        /// IGNORED; only the control list is consumed.
        /// </summary>
        /// <remarks>
        /// <para>The semantics from PLUGIN_DESIGN.md section 4.1:
        /// "Load additional controls and bindings from a template
        /// into an already-open form, for composing big forms from
        /// small partial templates." Bindings persistence in templates
        /// is a future concern.</para>
        ///
        /// <para>Collision policy: if any control id in the imported
        /// template matches a control id already on the target form
        /// (case-insensitive comparison via
        /// <see cref="FindControl"/>), the entire import is rejected
        /// with <c>20103</c> and zero controls are appended. This is
        /// fail-atomic so the BTM caller can roll back cleanly.</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> on invalid handle; <c>20101</c> on bad arg
        /// count; <c>20103</c> on control id collision; <c>20105</c>
        /// on I/O failure; <c>20106</c> on parse failure.</para>
        /// </remarks>
        public int f_FORMIMPORT(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            // Optional third arg is the BTM-side vars string
            // (key=value|key=value|...) consumed by the imported
            // template's ${var} substitution.
            if (parts.Length != 2 && parts.Length != 3)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string path = Path.GetFullPath(parts[1]);
            Dictionary<string, string>? vars = parts.Length == 3
                ? FormSerializer.ParseVars(parts[2])
                : null;
            FormDescriptor template;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                template = FormSerializer.Deserialize(json, vars);
            }
            catch (IOException ex)
            {
                TryAppendMarker(
                    $"FORMIMPORT I/O failure for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                TryAppendMarker(
                    $"FORMIMPORT access denied for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            catch (FormatException ex)
            {
                TryAppendMarker(
                    $"FORMIMPORT parse failure for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrParseFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            // Pre-flight collision check across the entire imported
            // control list. Only commit the append if every imported
            // id is unique against the target form's existing ids.
            foreach (ControlDescriptor c in template.Controls)
            {
                if (string.IsNullOrEmpty(c.Id)) { continue; }
                if (FindControl(form, c.Id) is not null)
                {
                    TryAppendMarker(
                        $"FORMIMPORT id collision: handle={seq} id={c.Id} path={path}");
                    args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
            }

            foreach (ControlDescriptor c in template.Controls)
            {
                form.Controls.Add(c);
            }

            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMRELAYOUT[handle]</c>: re-run the
        /// layout pass over the form's controls and apply the
        /// resulting bounds in place. The layout manager is selected
        /// by <see cref="FormLayoutFactory.Create"/> based on
        /// <see cref="FormDescriptor.LayoutMode"/> and the form-level
        /// property bag (grid_rows, flow_hgap, dock_padding, etc.).
        ///
        /// </summary>
        /// <remarks>
        /// <para>The contract from PLUGIN_DESIGN.md: a flow / grid /
        /// dock form does not auto-reflow when controls are added or
        /// removed via <c>@FORMADD</c> / <c>@FORMREMOVE</c> / property
        /// changes. The BTM caller batches the structural changes,
        /// then issues <c>@FORMRELAYOUT</c> exactly once to commit
        /// them. For an absolute layout the call is a no-op (the
        /// pass-through layout writes each control's existing bounds
        /// back to itself, idempotent).</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> on invalid handle; <c>20101</c> on bad arg
        /// count; <c>20105</c> if the layout computation itself
        /// throws.</para>
        ///
        /// <para>This dispatch surface only mutates the descriptor.
        /// The realizer also re-applies the bounds onto the live
        /// WinForms controls when the form has been realized.</para>
        /// </remarks>
        public int f_FORMRELAYOUT(StringBuilder args)
        {
            string raw = args.ToString().Trim();
            args.Clear();

            if (string.IsNullOrEmpty(raw))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!FormHandle.TryParse(raw, out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            try
            {
                ILayoutManager mgr = FormLayoutFactory.Create(form);
                IReadOnlyDictionary<string, LayoutRect> bounds = mgr.Compute(
                    form.Controls,
                    form.Width,
                    form.Height);
                for (int i = 0; i < form.Controls.Count; i++)
                {
                    ControlDescriptor c = form.Controls[i];
                    string key = string.IsNullOrEmpty(c.Id)
                        ? "#" + i.ToString(CultureInfo.InvariantCulture)
                        : c.Id;
                    if (!bounds.TryGetValue(key, out LayoutRect r))
                    {
                        // Layout manager omitted this control. Leave
                        // its existing bounds untouched rather than
                        // zero them; this matches the absolute layout
                        // contract for missing entries.
                        continue;
                    }
                    c.X = r.X;
                    c.Y = r.Y;
                    c.Width = r.Width;
                    c.Height = r.Height;
                }
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMRELAYOUT failed for handle {seq}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            args.Append('0');
            return 0;
        }

        // -----------------------------------------------------------------
        // TASKDIALOG, FOCUS, SENDMESSAGE, HITTEST
        // -----------------------------------------------------------------

        /// <summary>
        /// Implementation of <c>@FORMTASKDIALOG[title,text,buttons,icon]</c>:
        /// show a Vista-style task dialog and return the index of
        /// the clicked button (0-based) or -1 on cancel/close.
        /// Recognized buttons: <c>ok</c>, <c>okcancel</c>, <c>yesno</c>,
        /// <c>yesnocancel</c>, <c>retrycancel</c>, <c>abortretryignore</c>.
        /// Recognized icons: <c>none</c>, <c>info</c>, <c>warning</c>,
        /// <c>error</c>, <c>question</c>. Suppressed in headless mode
        /// (returns 0).
        /// </summary>
        public int f_FORMTASKDIALOG(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length < 2 || parts.Length > 4)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            string title = parts[0];
            string text = parts[1];
            string buttonsTok = parts.Length >= 3 ? parts[2].ToLowerInvariant() : "ok";
            string iconTok = parts.Length >= 4 ? parts[3].ToLowerInvariant() : "none";

            if (HeadlessMode.IsEnabled)
            {
                TryAppendMarker(
                    $"FORMTASKDIALOG headless suppressed: title='{title}' buttons={buttonsTok}");
                args.Append('0');
                return 0;
            }

            MessageBoxButtons mbButtons = buttonsTok switch
            {
                "okcancel" => MessageBoxButtons.OKCancel,
                "yesno" => MessageBoxButtons.YesNo,
                "yesnocancel" => MessageBoxButtons.YesNoCancel,
                "retrycancel" => MessageBoxButtons.RetryCancel,
                "abortretryignore" => MessageBoxButtons.AbortRetryIgnore,
                _ => MessageBoxButtons.OK,
            };
            MessageBoxIcon mbIcon = iconTok switch
            {
                "info" => MessageBoxIcon.Information,
                "warning" => MessageBoxIcon.Warning,
                "error" => MessageBoxIcon.Error,
                "question" => MessageBoxIcon.Question,
                _ => MessageBoxIcon.None,
            };

            DialogResult result = DialogResult.None;
            try
            {
                _guiHost.Invoke(() =>
                {
                    result = MessageBox.Show(text, title, mbButtons, mbIcon);
                });
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMTASKDIALOG failed: {ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            int idx = MapDialogResult(buttonsTok, result);
            args.Append(idx.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        private static int MapDialogResult(string buttons, DialogResult r)
        {
            switch (buttons)
            {
                case "okcancel":
                    return r == DialogResult.OK ? 0 : (r == DialogResult.Cancel ? 1 : -1);
                case "yesno":
                    return r == DialogResult.Yes ? 0 : (r == DialogResult.No ? 1 : -1);
                case "yesnocancel":
                    return r switch
                    {
                        DialogResult.Yes => 0,
                        DialogResult.No => 1,
                        DialogResult.Cancel => 2,
                        _ => -1,
                    };
                case "retrycancel":
                    return r == DialogResult.Retry ? 0 : (r == DialogResult.Cancel ? 1 : -1);
                case "abortretryignore":
                    return r switch
                    {
                        DialogResult.Abort => 0,
                        DialogResult.Retry => 1,
                        DialogResult.Ignore => 2,
                        _ => -1,
                    };
                default: // ok
                    return r == DialogResult.OK ? 0 : -1;
            }
        }

        /// <summary>
        /// Implementation of <c>@FORMFOCUS[h]</c> or <c>@FORMFOCUS[TCC]</c>:
        /// move the foreground window focus to the named target. Pass a
        /// FormCast handle to focus a realized form, or the literal
        /// string <c>TCC</c> to focus the parent <c>tcc.exe</c> console
        /// window. Returns "0" on success.
        /// </summary>
        public int f_FORMFOCUS(StringBuilder args)
        {
            string target = args.ToString().Trim();
            args.Clear();
            if (target.Length == 0)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            IntPtr hwnd = IntPtr.Zero;
            if (string.Equals(target, "TCC", StringComparison.OrdinalIgnoreCase))
            {
                hwnd = NativeMethods.GetConsoleWindow();
            }
            else
            {
                if (!FormHandle.TryParse(target, out int seq))
                {
                    args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                Form? realized;
                lock (_realizedFormsLock)
                {
                    _realizedForms.TryGetValue(seq, out realized);
                }
                if (realized is null)
                {
                    args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                IntPtr h = IntPtr.Zero;
                _guiHost.Invoke(() =>
                {
                    if (!realized.IsDisposed)
                    {
                        _ = realized.Handle;
                        h = realized.Handle;
                    }
                });
                hwnd = h;
            }

            if (hwnd == IntPtr.Zero)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            bool ok = NativeMethods.SetForegroundWindow(hwnd);
            args.Append(ok ? '0' : '1');
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMSENDMESSAGE[h,msg,wparam,lparam]</c>:
        /// send a Win32 message to a realized form's HWND. Returns the
        /// LRESULT as a decimal string. <c>msg</c> accepts decimal or
        /// <c>0x</c>-prefixed hex.
        /// </summary>
        public int f_FORMSENDMESSAGE(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();
            if (parts.Length != 4)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!TryParseUint(parts[1], out uint msg) ||
                !TryParseInt(parts[2], out int wparam) ||
                !TryParseInt(parts[3], out int lparam))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            IntPtr result = IntPtr.Zero;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                _ = realized.Handle;
                result = NativeMethods.SendMessage(
                    realized.Handle, msg, new IntPtr(wparam), new IntPtr(lparam));
            });
            args.Append(result.ToInt64().ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMHITTEST[h,x,y]</c>: return the id of
        /// the realized control at the given form-relative pixel
        /// coordinates, or the empty string if no control is at that
        /// point. Used by interactive designers to map mouse clicks to
        /// control ids.
        /// </summary>
        public int f_FORMHITTEST(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();
            if (parts.Length != 3)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (!FormHandle.TryParse(parts[0], out int seq) ||
                !TryParseInt(parts[1], out int x) ||
                !TryParseInt(parts[2], out int y))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            Form? realized;
            lock (_realizedFormsLock)
            {
                _realizedForms.TryGetValue(seq, out realized);
            }
            if (realized is null) { return 0; }

            string id = string.Empty;
            _guiHost.Invoke(() =>
            {
                if (realized.IsDisposed) { return; }
                Control? hit = realized.GetChildAtPoint(new System.Drawing.Point(x, y),
                    GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled);
                if (hit is not null && !string.IsNullOrEmpty(hit.Name))
                {
                    id = hit.Name;
                }
            });
            args.Append(id);
            return 0;
        }

        private static bool TryParseInt(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryParseUint(string s, out uint v)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out v);
            }
            return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        // -----------------------------------------------------------------
        // @FORMCONSOLE: control TCC console window visibility
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>@FORMCONSOLE[action]</c>: control the visibility of
        /// the parent TCC console window. Actions:
        /// <list type="bullet">
        ///   <item><c>hide</c> -- hide the console completely (the
        ///   BTM continues running invisibly)</item>
        ///   <item><c>show</c> -- show the console (restore from
        ///   hidden or minimized state)</item>
        ///   <item><c>minimize</c> -- minimize the console to the
        ///   taskbar</item>
        /// </list>
        /// This is the key primitive for the "BTM as a Windows app"
        /// pattern: hide the console, show the FormCast window, and
        /// the user sees only the GUI form. On exit, show the console
        /// again (or let TCC exit).
        /// Returns "0" on success.
        /// </summary>
        public int f_FORMCONSOLE(StringBuilder args)
        {
            string action = args.ToString().Trim().ToLowerInvariant();
            args.Clear();

            if (string.IsNullOrEmpty(action))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            IntPtr hwnd = NativeMethods.GetConsoleWindow();
            if (hwnd == IntPtr.Zero)
            {
                // No console window (detached process). Not an error.
                args.Append('0');
                return 0;
            }

            switch (action)
            {
                case "hide":
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                    break;
                case "show":
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hwnd);
                    break;
                case "minimize":
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINIMIZED);
                    break;
                default:
                    args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                    return 0;
            }

            args.Append('0');
            return 0;
        }

        // -----------------------------------------------------------------
        // @FORMAPPLYBINDINGS: activate template-stored bindings
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>@FORMAPPLYBINDINGS[h[,validate]]</c>: walk the form
        /// descriptor and for every control that has <c>_bind.*</c>
        /// props (e.g. <c>_bind.click = gosub :on_ok</c>), register
        /// the binding via <c>@FORMBIND</c>. This is what connects
        /// a saved template's event wiring to the loading BTM's
        /// subroutines.
        ///
        /// <para>The designer saves bindings as inert data in the
        /// prop bag. The end-user BTM calls this function after
        /// <c>@FORMLOAD</c> to activate them. The designer itself
        /// does NOT call it, so it never trips over missing
        /// subroutine labels.</para>
        ///
        /// <para>Pass <c>validate</c> as the second arg to check
        /// that every <c>gosub :label</c> target exists before
        /// binding. Returns the count of bindings applied, or
        /// <c>20106</c> if validation fails.</para>
        /// </summary>
        public int f_FORMAPPLYBINDINGS(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length < 1 || parts.Length > 2)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            bool validate = parts.Length >= 2 &&
                string.Equals(parts[1], "validate", StringComparison.OrdinalIgnoreCase);

            int count = 0;
            var errors = new List<string>();

            // Walk every control (recursively for nested containers)
            // and find _bind.* props.
            ApplyBindingsRecursive(form.Controls, seq, string.Empty,
                validate, ref count, errors);

            // Also check form-level bindings (ctrl = ".")
            foreach (KeyValuePair<string, string> kv in form.Properties)
            {
                if (!kv.Key.StartsWith("_bind.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string evt = kv.Key.Substring(6); // after "_bind."
                string cmd = kv.Value;
                if (string.IsNullOrEmpty(cmd)) { continue; }

                if (validate && !ValidateBindingTarget(cmd))
                {
                    errors.Add($"form-level {evt}: {cmd}");
                    continue;
                }
                RegisterBinding(seq, string.Empty, evt, cmd);
                count++;
            }

            if (validate && errors.Count > 0)
            {
                TryAppendMarker(
                    $"FORMAPPLYBINDINGS validation failed: {string.Join("; ", errors)}");
                args.Append(ErrParseFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            args.Append(count.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        private void ApplyBindingsRecursive(
            List<ControlDescriptor> controls,
            int seq,
            string parentPath,
            bool validate,
            ref int count,
            List<string> errors)
        {
            foreach (ControlDescriptor c in controls)
            {
                foreach (KeyValuePair<string, string> kv in c.Properties)
                {
                    if (!kv.Key.StartsWith("_bind.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string evt = kv.Key.Substring(6);
                    string cmd = kv.Value;
                    if (string.IsNullOrEmpty(cmd)) { continue; }

                    if (validate && !ValidateBindingTarget(cmd))
                    {
                        errors.Add($"{c.Id}.{evt}: {cmd}");
                        continue;
                    }
                    RegisterBinding(seq, c.Id, evt, cmd);
                    count++;
                }

                if (c.Children.Count > 0)
                {
                    string childPath = string.IsNullOrEmpty(parentPath)
                        ? c.Id
                        : parentPath + "/" + c.Id;
                    ApplyBindingsRecursive(c.Children, seq, childPath,
                        validate, ref count, errors);
                }
            }
        }

        private void RegisterBinding(int seq, string ctrl, string evt, string cmd)
        {
            var key = new BindingKey(seq, ctrl, evt);
            lock (_bindingsLock)
            {
                _bindings[key] = cmd;
            }
        }

        /// <summary>
        /// Basic validation: if the command starts with "gosub :",
        /// check that TCC can find the label. This uses a heuristic
        /// (the label must start with "gosub :") rather than calling
        /// into TCC because the validator runs at load time before
        /// the script's own labels are fully registered. Returns
        /// true for non-gosub commands (we can't validate arbitrary
        /// command lines).
        /// </summary>
        private static bool ValidateBindingTarget(string cmd)
        {
            // For now, accept everything. Full label validation
            // would require calling TCC's label lookup which isn't
            // available as a P/Invoke. The "validate" flag is
            // reserved for future use when the host surface grows.
            _ = cmd;
            return true;
        }

        // -----------------------------------------------------------------
        // Common dialog dispatch verbs
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>@FORMOPENDIALOG[title[,filter[,initialdir]]]</c>: show the
        /// native Windows Open File dialog. Returns the selected path,
        /// or empty on cancel. Filter uses the standard WinForms
        /// pattern: <c>Text files|*.txt|All files|*.*</c> (pipe-separated
        /// pairs, but since pipe is unsafe in BTM SET, colon works too:
        /// <c>Text files:*.txt:All files:*.*</c>).
        /// </summary>
        public int f_FORMOPENDIALOG(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            string title = parts.Length >= 1 ? parts[0] : "Open";
            string filter = parts.Length >= 2 ? NormalizeDialogFilter(parts[1]) : "All files (*.*)|*.*";
            string? initialDir = parts.Length >= 3 ? parts[2] : null;

            if (HeadlessMode.IsEnabled) { return 0; }

            string result = string.Empty;
            _guiHost.Invoke(() =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = title,
                    Filter = filter,
                };
                if (!string.IsNullOrEmpty(initialDir)) { dlg.InitialDirectory = initialDir!; }
                if (dlg.ShowDialog() == DialogResult.OK) { result = dlg.FileName; }
            });
            args.Append(result);
            return 0;
        }

        /// <summary>
        /// <c>@FORMSAVEDIALOG[title[,filter[,initialdir]]]</c>: show the
        /// native Windows Save File dialog. Returns the selected path,
        /// or empty on cancel.
        /// </summary>
        public int f_FORMSAVEDIALOG(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            string title = parts.Length >= 1 ? parts[0] : "Save";
            string filter = parts.Length >= 2 ? NormalizeDialogFilter(parts[1]) : "All files (*.*)|*.*";
            string? initialDir = parts.Length >= 3 ? parts[2] : null;

            if (HeadlessMode.IsEnabled) { return 0; }

            string result = string.Empty;
            _guiHost.Invoke(() =>
            {
                using var dlg = new SaveFileDialog
                {
                    Title = title,
                    Filter = filter,
                };
                if (!string.IsNullOrEmpty(initialDir)) { dlg.InitialDirectory = initialDir!; }
                if (dlg.ShowDialog() == DialogResult.OK) { result = dlg.FileName; }
            });
            args.Append(result);
            return 0;
        }

        /// <summary>
        /// <c>@FORMFOLDERDIALOG[description[,initialdir]]</c>: show the
        /// native Windows "Browse for Folder" dialog. Returns the
        /// selected path, or empty on cancel.
        /// </summary>
        public int f_FORMFOLDERDIALOG(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            string description = parts.Length >= 1 ? parts[0] : "Select a folder";
            string? initialDir = parts.Length >= 2 ? parts[1] : null;

            if (HeadlessMode.IsEnabled) { return 0; }

            string result = string.Empty;
            _guiHost.Invoke(() =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = description,
                    ShowNewFolderButton = true,
                };
                if (!string.IsNullOrEmpty(initialDir)) { dlg.SelectedPath = initialDir!; }
                if (dlg.ShowDialog() == DialogResult.OK) { result = dlg.SelectedPath; }
            });
            args.Append(result);
            return 0;
        }

        /// <summary>
        /// <c>@FORMCOLORDIALOG[[initialcolor]]</c>: show the native
        /// Windows color picker. Returns the selected color as a
        /// <c>#RRGGBB</c> hex string, or empty on cancel.
        /// </summary>
        public int f_FORMCOLORDIALOG(StringBuilder args)
        {
            string raw = args.ToString().Trim();
            args.Clear();

            if (HeadlessMode.IsEnabled) { return 0; }

            string result = string.Empty;
            _guiHost.Invoke(() =>
            {
                using var dlg = new ColorDialog
                {
                    FullOpen = true,
                };
                if (!string.IsNullOrEmpty(raw))
                {
                    try
                    {
                        dlg.Color = System.Drawing.ColorTranslator.FromHtml(raw);
                    }
                    catch { /* ignore bad color */ }
                }
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    result = "#" + dlg.Color.R.ToString("X2", CultureInfo.InvariantCulture) +
                             dlg.Color.G.ToString("X2", CultureInfo.InvariantCulture) +
                             dlg.Color.B.ToString("X2", CultureInfo.InvariantCulture);
                }
            });
            args.Append(result);
            return 0;
        }

        /// <summary>
        /// <c>@FORMFONTDIALOG[[initialfont]]</c>: show the native
        /// Windows font picker. Returns the selected font as
        /// <c>family:size[:style]</c> (e.g. <c>Consolas:12:Bold</c>),
        /// or empty on cancel.
        /// </summary>
        public int f_FORMFONTDIALOG(StringBuilder args)
        {
            string raw = args.ToString().Trim();
            args.Clear();

            if (HeadlessMode.IsEnabled) { return 0; }

            string result = string.Empty;
            _guiHost.Invoke(() =>
            {
                using var dlg = new FontDialog
                {
                    ShowColor = false,
                    ShowEffects = true,
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    System.Drawing.Font f = dlg.Font;
                    result = f.FontFamily.Name + ":" +
                             f.Size.ToString(CultureInfo.InvariantCulture);
                    if (f.Style != System.Drawing.FontStyle.Regular)
                    {
                        result += ":" + f.Style.ToString();
                    }
                }
            });
            args.Append(result);
            return 0;
        }

        /// <summary>
        /// Normalize a dialog filter string that uses colon separators
        /// (BTM-safe) into the pipe-separated format WinForms expects.
        /// If the string already contains pipe, pass it through.
        /// </summary>
        private static string NormalizeDialogFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter)) { return "All files (*.*)|*.*"; }
            // If filter contains pipe, it's already in WinForms format.
            if (filter.IndexOf('|') >= 0) { return filter; }
            // Otherwise replace colon with pipe.
            return filter.Replace(':', '|');
        }

        // -----------------------------------------------------------------
        // NotifyIcon (system tray)
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>@FORMNOTIFY[action[,args...]]</c>: manage a system tray
        /// notification icon. Actions:
        /// <list type="bullet">
        ///   <item><c>show,tooltiptext[,iconpath]</c> -- show the tray
        ///   icon with the given tooltip. Optional icon path (ICO file);
        ///   defaults to the app icon.</item>
        ///   <item><c>balloon,title,text[,icon]</c> -- show a balloon
        ///   notification. icon = none/info/warning/error.</item>
        ///   <item><c>hide</c> -- remove the tray icon.</item>
        /// </list>
        /// Returns "0" on success. Headless mode suppresses the icon.
        /// </summary>
        public int f_FORMNOTIFY(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();
            if (parts.Length < 1)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string action = parts[0].ToLowerInvariant();

            if (HeadlessMode.IsEnabled)
            {
                args.Append('0');
                return 0;
            }

            switch (action)
            {
                case "show":
                {
                    string tip = parts.Length >= 2 ? parts[1] : "FormCast";
                    _guiHost.Invoke(() =>
                    {
                        if (_notifyIcon is null)
                        {
                            _notifyIcon = new NotifyIcon();
                        }
                        _notifyIcon.Text = tip;
                        _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                        if (parts.Length >= 3 && System.IO.File.Exists(parts[2]))
                        {
                            try { _notifyIcon.Icon = new System.Drawing.Icon(parts[2]); }
                            catch { /* keep default */ }
                        }
                        _notifyIcon.Visible = true;
                    });
                    args.Append('0');
                    break;
                }
                case "balloon":
                {
                    if (parts.Length < 3)
                    {
                        args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                    string title = parts[1];
                    string text = parts[2];
                    ToolTipIcon icon = parts.Length >= 4
                        ? parts[3].ToLowerInvariant() switch
                        {
                            "info" => ToolTipIcon.Info,
                            "warning" => ToolTipIcon.Warning,
                            "error" => ToolTipIcon.Error,
                            _ => ToolTipIcon.None,
                        }
                        : ToolTipIcon.None;
                    _guiHost.Invoke(() =>
                    {
                        if (_notifyIcon is null)
                        {
                            _notifyIcon = new NotifyIcon
                            {
                                Icon = System.Drawing.SystemIcons.Application,
                                Visible = true,
                            };
                        }
                        _notifyIcon.ShowBalloonTip(5000, title, text, icon);
                    });
                    args.Append('0');
                    break;
                }
                case "hide":
                {
                    _guiHost.Invoke(() =>
                    {
                        if (_notifyIcon is not null)
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Dispose();
                            _notifyIcon = null;
                        }
                    });
                    args.Append('0');
                    break;
                }
                default:
                    args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                    break;
            }
            return 0;
        }

        private NotifyIcon? _notifyIcon;

        /// <summary>
        /// Implementation of <c>@FORMCMD[command line]</c>: marshal a
        /// TCC command through the <see cref="CallbackWorker"/>
        /// and execute it via <c>TakeCmd.Command</c>. This verifies
        /// whether a non-TCC-owned background thread (the worker) can
        /// issue TCC commands successfully -- the answer determines
        /// whether the <c>@FORMBIND</c> callback design is
        /// viable as written.
        /// </summary>
        /// <remarks>
        /// <para>The full <paramref name="args"/> buffer is treated as
        /// the command line, NOT as a comma-delimited argument list.
        /// This means <c>%@formcmd[set FOO=hello, world]</c> passes
        /// the entire string to TCC, including the comma. Splitting
        /// would corrupt commands like <c>echo a,b,c</c> or any
        /// option that legitimately contains commas. The BTM caller
        /// is responsible for any quoting TCC itself requires.</para>
        ///
        /// <para>Result convention: the integer return code from
        /// <c>TakeCmd.Command</c> on success (typically <c>0</c>);
        /// <c>20101</c> if the buffer is empty; <c>20105</c> (I/O
        /// failure code, repurposed) if the worker thread itself
        /// faulted while invoking the native call.</para>
        /// </remarks>
        public int f_FORMCMD(StringBuilder args)
        {
            string commandLine = args.ToString();
            args.Clear();

            if (string.IsNullOrWhiteSpace(commandLine))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            int rc = 0;
            try
            {
                _callbackWorker.SubmitAndWait(() =>
                {
                    rc = TakeCmd.Command(commandLine, 0);
                });
            }
            catch (CallbackWorkerInvocationException)
            {
                // The worker action threw -- surface as an I/O-style
                // failure code. Marker file will already have logged
                // the underlying exception via the UnhandledException
                // subscriber wired in Initialize.
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            args.Append(rc.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        /// <summary>
        /// Implementation of <c>@FORMSHOW[handle[,mode]]</c>: realize
        /// the descriptor at <paramref name="args"/> into a real
        /// WinForms <c>Form</c> on the GUI host thread, and (in headless
        /// mode) log the request without actually displaying the window.
        /// </summary>
        /// <remarks>
        /// <para>Every <c>@FORMSHOW</c> call lazily realizes the
        /// descriptor (constructing the <c>Form</c> on the GUI host
        /// thread, populating its controls, wiring the forced-shutdown
        /// sentinel) and stores it in the realized-form map keyed by
        /// handle. The Form is created with <c>Visible = false</c> and
        /// the headless code path never flips that bit.</para>
        ///
        /// <para>In non-headless mode the default (no-mode and
        /// "visible") branch calls <c>Form.Show()</c> on the GUI thread.
        /// The dispatch surface (handle parsing, error codes, optional
        /// <c>modal</c> arg) is stable so BTM scripts written against
        /// it stay valid.</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> on invalid handle; <c>20101</c> on bad arg
        /// count; <c>20105</c> if realization itself threw.</para>
        /// </remarks>
        public int f_FORMSHOW(StringBuilder args)
        {
            string raw = args.ToString();
            args.Clear();
            Internal.PluginLogger.Info($"FORMSHOW {raw}");

            string[] parts = ArgParser.Split(raw);
            if (parts.Length < 1 || parts.Length > 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            FormDescriptor? d = _localRegistry.Lookup(seq);
            if (d is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string mode = parts.Length == 2 ? (parts[1] ?? string.Empty) : string.Empty;

            Form realized;
            try
            {
                realized = GetOrRealize(seq, d);
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMSHOW realize failed for handle {seq}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            // In non-headless mode the default (no-mode and "visible")
            // branch calls Form.Show() on the GUI thread.
            // The "modal" / "modal:N" branch routes through
            // Form.ShowDialog with an optional WinForms.Timer
            // auto-dismiss so unattended scripts can drive modal
            // dialogs to completion without human input. Headless
            // mode never displays anything, but the modal branch
            // still drives ShowDialog so the DialogResult round
            // trip is exercised; the form is hidden the entire
            // time because Show*Dialog* on a Visible=false form
            // still creates the message loop and the auto-close
            // timer fires inside it before any paint happens.
            string modeLower = mode.ToLowerInvariant();
            bool isVisibleMode = mode.Length == 0 || modeLower == "visible";
            bool isModal = modeLower == "modal" || modeLower.StartsWith("modal:", StringComparison.Ordinal);
            string modeLabel = string.IsNullOrEmpty(mode) ? "default" : mode;
            string headlessLabel = HeadlessMode.IsEnabled ? "True" : "False";

            if (isModal)
            {
                // Headless modal is a SYNTHETIC path: we never call
                // Form.ShowDialog because ShowDialog would force the
                // form Visible=true and paint a real window briefly,
                // which violates the HeadlessMode "no window ever
                // shown" contract. Instead we return
                // DialogResult.Cancel (=2) immediately. The actual
                // ShowDialog code path is exercised by the
                // non-headless modal branch and by the bridge BTM
                // running inside real TCC v36.
                if (HeadlessMode.IsEnabled)
                {
                    int synthetic = (int)System.Windows.Forms.DialogResult.Cancel;
                    TryAppendMarker(
                        $"FORMSHOW handle={seq} name={d.Name} mode={modeLabel} " +
                        $"headless=True (synthetic Cancel)");
                    args.Append(synthetic.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }

                // Non-headless modal: real ShowDialog. Parse the
                // optional auto-dismiss timeout from the mode arg
                // (`modal:N`). Without an explicit N, ShowDialog
                // blocks until a human closes the dialog.
                int autoCloseMs = 0;
                int colon = mode.IndexOf(':');
                if (colon >= 0 && colon + 1 < mode.Length)
                {
                    string tail = mode.Substring(colon + 1);
                    if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                        && parsed > 0)
                    {
                        autoCloseMs = parsed;
                    }
                }

                int dialogResult;
                try
                {
                    dialogResult = ShowDialogWithAutoClose(realized, autoCloseMs);
                }
                catch (Exception ex)
                {
                    TryAppendMarker(
                        $"FORMSHOW modal failed for handle {seq}: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                TryAppendMarker(
                    $"FORMSHOW handle={seq} name={d.Name} mode={modeLabel} " +
                    $"headless={headlessLabel} autoCloseMs={autoCloseMs} " +
                    $"(ShowDialog returned {dialogResult})");
                args.Append(dialogResult.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!HeadlessMode.IsEnabled && isVisibleMode)
            {
                try
                {
                    _guiHost.Invoke(() =>
                    {
                        if (realized.IsDisposed) { return; }
                        _ = realized.Handle;  // force HWND on GUI thread
                        realized.Show();
                        // Force the taskbar icon via WM_SETICON. WinForms
                        // Form.Icon doesn't always update the taskbar when
                        // the host process (TCC) has its own embedded icon.
                        if (realized.Icon is not null && realized.ShowInTaskbar)
                        {
                            NativeMethods.ForceWindowIcon(realized.Handle, realized.Icon);
                        }
                        Internal.PluginLogger.Debug(
                            $"FORMSHOW post-show: handle={seq} title={realized.Text} " +
                            $"icon={realized.Icon?.Size} showInTaskbar={realized.ShowInTaskbar}");
                    });
                }
                catch (Exception ex)
                {
                    TryAppendMarker(
                        $"FORMSHOW show failed for handle {seq}: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                TryAppendMarker(
                    $"FORMSHOW handle={seq} name={d.Name} mode={modeLabel} " +
                    $"headless={headlessLabel} (shown)");
            }
            else
            {
                TryAppendMarker(
                    $"FORMSHOW handle={seq} name={d.Name} mode={modeLabel} " +
                    $"headless={headlessLabel} (realized but not shown)");
            }

            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Marshal to the GUI thread, optionally schedule a one-shot
        /// <see cref="System.Windows.Forms.Timer"/> that fires
        /// <c>form.Close()</c> after <paramref name="autoCloseMs"/>
        /// milliseconds, then call <c>form.ShowDialog()</c>. The
        /// returned integer is the <c>DialogResult</c> the dialog
        /// closed with (Cancel=2 when the auto-close timer is what
        /// closed it).
        /// </summary>
        /// <remarks>
        /// <para>When <paramref name="autoCloseMs"/> is zero we do
        /// not schedule a timer; the dialog runs to user-driven
        /// completion. Non-headless callers that want a "no human"
        /// modal must pass <c>modal:N</c> with N greater than zero.</para>
        /// <para>The timer must be created and started on the GUI
        /// thread BEFORE <c>ShowDialog</c> enters its nested message
        /// loop, so the loop pumps WM_TIMER once <paramref name="autoCloseMs"/>
        /// elapses and the Tick handler closes the dialog from
        /// inside the loop. Both lifetimes (timer and ShowDialog
        /// call) are confined to the single <c>_guiHost.Invoke</c>
        /// below; the dispatching TCC thread blocks here for as
        /// long as the dialog is up.</para>
        /// </remarks>
        private int ShowDialogWithAutoClose(Form form, int autoCloseMs)
        {
            int result = (int)System.Windows.Forms.DialogResult.None;
            _guiHost.Invoke(() =>
            {
                if (form.IsDisposed) { return; }
                _ = form.Handle;
                System.Windows.Forms.Timer? timer = null;
                if (autoCloseMs > 0)
                {
                    timer = new System.Windows.Forms.Timer { Interval = autoCloseMs };
                    timer.Tick += (s, e) =>
                    {
                        try { timer.Stop(); } catch { /* swallow */ }
                        if (!form.IsDisposed)
                        {
                            try { form.Close(); } catch { /* swallow */ }
                        }
                    };
                    timer.Start();
                }
                try
                {
                    result = (int)form.ShowDialog();
                }
                finally
                {
                    if (timer is not null)
                    {
                        try { timer.Stop(); } catch { /* swallow */ }
                        try { timer.Dispose(); } catch { /* swallow */ }
                    }
                }
            });
            return result;
        }

        /// <summary>
        /// Implementation of <c>@FORMSAVEIMAGE[handle,path]</c>: render
        /// the realized form to a PNG file at the given path.
        /// </summary>
        /// <remarks>
        /// <para>The form is realized lazily if not already realized
        /// (same path as <c>@FORMSHOW</c>), then rendered via
        /// <see cref="FormRealizer.SaveImage"/> on the GUI host thread.
        /// The form is NEVER displayed: <c>Control.DrawToBitmap</c>
        /// works against the off-screen WinForms render path, so
        /// scripts can capture form images for documentation, golden-
        /// bitmap comparison, or designer round-trips without ever
        /// flashing a window in front of a user.</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> on invalid handle; <c>20101</c> on bad arg
        /// count or empty path; <c>20105</c> on render or I/O failure.</para>
        /// </remarks>
        public int f_FORMSAVEIMAGE(StringBuilder args)
        {
            string raw = args.ToString();
            args.Clear();

            string[] parts = ArgParser.Split(raw);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            FormDescriptor? d = _localRegistry.Lookup(seq);
            if (d is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string path = Path.GetFullPath(parts[1]);

            try
            {
                Form realized = GetOrRealize(seq, d);
                FormRealizer.SaveImage(realized, _guiHost, path);
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMSAVEIMAGE failed for handle {seq} path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            args.Append('0');
            return 0;
        }

        /// <summary>
        /// <c>@FORMSAVECOMPOSITE[path,h1,h2,...]</c>: render multiple
        /// realized forms into a single composite PNG. Each form is drawn
        /// at its current screen position relative to the bounding
        /// rectangle of all supplied forms. Useful for capturing multi-
        /// window layouts (e.g. the designer's three-window setup) as a
        /// single screenshot for documentation.
        /// </summary>
        /// <remarks>
        /// Result convention: <c>"0"</c> on success; <c>20101</c> on bad
        /// arg count (need at least path + one handle); <c>20100</c> if
        /// any handle is invalid; <c>20105</c> on render or I/O failure.
        /// </remarks>
        public int f_FORMSAVECOMPOSITE(StringBuilder args)
        {
            string raw = args.ToString();
            args.Clear();

            string[] parts = ArgParser.Split(raw);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string path = Path.GetFullPath(parts[0]);
            var forms = new List<Form>();

            for (int i = 1; i < parts.Length; i++)
            {
                if (!FormHandle.TryParse(parts[i], out int seq))
                {
                    args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                FormDescriptor? d = _localRegistry.Lookup(seq);
                if (d is null)
                {
                    args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }
                forms.Add(GetOrRealize(seq, d));
            }

            try
            {
                FormRealizer.SaveCompositeImage(forms, _guiHost, path);
                Internal.PluginLogger.Info($"FORMSAVECOMPOSITE -> {path} ({forms.Count} forms)");
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMSAVECOMPOSITE failed for path={path}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Implementation of
        /// <c>@FORMSIMULATE[handle,ctrlid,action[,value]]</c>: fire a
        /// synthetic event on a control by realizing the form (if not
        /// already realized) and dispatching the action on the GUI host
        /// thread via <see cref="FormRealizer.Simulate"/>. Lets BTM
        /// tests drive controls programmatically (Button.PerformClick,
        /// TextBox.AppendText, CheckBox.Checked = ...) without ever
        /// requiring a visible window or user input.
        /// </summary>
        /// <remarks>
        /// <para>Recognized actions: <c>click</c>, <c>type</c>,
        /// <c>settext</c>, <c>check</c>, <c>uncheck</c>, <c>focus</c>.
        /// <c>type</c> and <c>settext</c> require a 4th argument
        /// (the text); the others use 3 arguments. Action names are
        /// case-insensitive.</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> invalid handle; <c>20101</c> bad arg count;
        /// <c>20103</c> unknown control id on the form;
        /// <c>20105</c> if the GUI-thread invocation itself threw;
        /// <c>20107</c> for unknown action OR an action that does not
        /// apply to the target control type (e.g. <c>type</c> on a
        /// Button). The latter two share a code because BTM scripts
        /// treat both as "the simulation request was wrong" and the
        /// marker file logs the distinction for diagnosis.</para>
        /// </remarks>
        public int f_FORMSIMULATE(StringBuilder args)
        {
            string raw = args.ToString();
            args.Clear();

            string[] parts = ArgParser.Split(raw);
            // 3 args = handle,ctrl,action; 4 args = handle,ctrl,action,value.
            if (parts.Length != 3 && parts.Length != 4)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            if (string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrEmpty(parts[1]) ||
                string.IsNullOrWhiteSpace(parts[2]))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            FormDescriptor? d = _localRegistry.Lookup(seq);
            if (d is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string controlId = parts[1];
            string action = parts[2];
            string? value = parts.Length == 4 ? parts[3] : null;

            FormRealizer.SimulateResult sr;
            try
            {
                Form realized = GetOrRealize(seq, d);
                sr = FormRealizer.Simulate(realized, _guiHost, controlId, action, value);
            }
            catch (Exception ex)
            {
                TryAppendMarker(
                    $"FORMSIMULATE failed for handle {seq} ctrl={controlId} " +
                    $"action={action}: {ex.GetType().Name}: {ex.Message}");
                args.Append(ErrIoFailure.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            switch (sr)
            {
                case FormRealizer.SimulateResult.Success:
                    args.Append('0');
                    break;
                case FormRealizer.SimulateResult.UnknownControl:
                    args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormRealizer.SimulateResult.UnknownAction:
                    TryAppendMarker(
                        $"FORMSIMULATE handle={seq} ctrl={controlId}: " +
                        $"unknown action '{action}'");
                    args.Append(ErrUnknownAction.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormRealizer.SimulateResult.UnsupportedForControl:
                    TryAppendMarker(
                        $"FORMSIMULATE handle={seq} ctrl={controlId}: " +
                        $"action '{action}' not applicable to control type");
                    args.Append(ErrUnknownAction.ToString(CultureInfo.InvariantCulture));
                    break;
            }
            return 0;
        }

        // -----------------------------------------------------------------
        // @FORMBIND declarative event handler registration
        // -----------------------------------------------------------------

        /// <summary>
        /// Implementation of
        /// <c>@FORMBIND[handle,ctrlid,event,command]</c>: register a
        /// TCC command line that fires automatically when the named
        /// event occurs on the named control. Pass an empty
        /// <c>ctrlid</c> to bind a form-level event such as
        /// <c>close</c>. Pass an empty <c>command</c> to clear an
        /// existing binding.
        /// </summary>
        /// <remarks>
        /// <para>The bound command runs on the
        /// <see cref="CallbackWorker"/> STA thread, not the GUI thread
        /// and not the script thread (PLUGIN_DESIGN.md section 7 #8).
        /// A bound BTM script can call back into <c>@FORMSTATE</c>,
        /// <c>@FORMCLOSE</c>, <c>@FORMSIMULATE</c>, etc. without
        /// deadlocking.</para>
        ///
        /// <para>Dispatch is asynchronous via
        /// <c>CallbackWorker.Enqueue</c>. The WinForms event handler
        /// returns to the GUI thread message loop immediately after
        /// the bound action is queued; the bound command runs on the
        /// worker thread when it reaches the head of the queue.
        /// Multiple events on the same form serialize through the
        /// worker, so bound handlers never overlap.</para>
        ///
        /// <para>Result convention: <c>"0"</c> on success;
        /// <c>20100</c> on invalid handle; <c>20101</c> on bad arg
        /// count or empty event name; <c>20103</c> if the named
        /// control does not exist on the form (form-level binds with
        /// an empty control id always pass this check).</para>
        ///
        /// <para>Re-binding the same (handle, ctrl, event) triple
        /// silently replaces the previous command. The binding is
        /// purged automatically when <c>@FORMCLOSE</c> frees the
        /// handle, and all bindings are cleared at
        /// <see cref="Shutdown"/>.</para>
        /// </remarks>
        public int f_FORMBIND(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            if (parts.Length != 4)
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            if (!FormHandle.TryParse(parts[0], out int seq))
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            FormDescriptor? form = _localRegistry.Lookup(seq);
            if (form is null)
            {
                args.Append(ErrInvalidHandle.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string ctrl = parts[1] ?? string.Empty;
            string evt = parts[2] ?? string.Empty;
            string command = parts[3] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(evt))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            // Non-empty control id must reference an existing control
            // on the form. Empty control id (form-level event such as
            // "close") bypasses the check.
            if (!string.IsNullOrEmpty(ctrl) && FindControl(form, ctrl) is null)
            {
                args.Append(ErrUnknownControlId.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            var key = new BindingKey(seq, ctrl, evt);
            lock (_bindingsLock)
            {
                if (string.IsNullOrEmpty(command))
                {
                    _bindings.Remove(key);
                }
                else
                {
                    _bindings[key] = command;
                }
            }
            args.Append('0');
            return 0;
        }

        /// <summary>
        /// Binding-dispatch hook. Wired by <see cref="GetOrRealize"/>
        /// onto every per-form <see cref="FormEventQueue"/>. Runs
        /// synchronously on the
        /// thread that enqueued the event (typically the GUI host
        /// thread for WinForms-fired events, or the script thread for
        /// the explicit close enqueue from <see cref="f_FORMCLOSE"/>).
        /// Looks up a matching binding and schedules the bound TCC
        /// command on the callback worker thread via
        /// <c>CallbackWorker.Enqueue</c>. The hook itself never blocks.
        /// </summary>
        /// <summary>
        /// Look up a binding for the given form event and, if found,
        /// enqueue the bound TCC command onto the <see cref="CallbackWorker"/>
        /// for asynchronous execution. Called from the
        /// <see cref="FormEventQueue.OnEnqueue"/> hook, which fires on the
        /// GUI thread immediately after an event is enqueued.
        /// </summary>
        /// <remarks>
        /// The command is captured into a closure at lookup time so a
        /// concurrent re-bind (unlikely but possible) does not affect
        /// in-flight dispatches. The worker serializes all bound commands
        /// for a given form, so BTM binding handlers never overlap.
        /// </remarks>
        private void DispatchBinding(int formHandle, FormEvent ev)
        {
            if (ev is null) { return; }

            string commandLine;
            var key = new BindingKey(formHandle, ev.ControlId, ev.EventType);
            lock (_bindingsLock)
            {
                if (!_bindings.TryGetValue(key, out string? cmd) ||
                    string.IsNullOrEmpty(cmd))
                {
                    return;
                }
                commandLine = cmd!;
            }

            Action<string>? testHook = TestCommandHook;
            try
            {
                _callbackWorker.Enqueue(() =>
                {
                    if (testHook is not null)
                    {
                        // Test seam: route the command string to the
                        // test instead of TakeCmd.Command. Exceptions
                        // surface via the worker UnhandledException
                        // event already wired in Initialize.
                        testHook(commandLine);
                        return;
                    }

                    try
                    {
                        // Discard rc: a bound BTM script's exit code
                        // is not surfaced anywhere observable. The
                        // bound code's side effects (env vars, calls
                        // back into @FORM*, output) are the contract,
                        // not the integer return.
                        _ = TakeCmd.Command(commandLine, 0);
                    }
                    catch (DllNotFoundException)
                    {
                        // Same posture as FORMEVENTS: missing
                        // TakeCmd.dll in xUnit test mode is normal.
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // Same.
                    }
                    catch (BadImageFormatException)
                    {
                        // Architecture mismatch in test mode.
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // Worker has already been stopped (Shutdown in
                // progress). Drop the binding silently rather than
                // crash the WinForms event handler that triggered
                // the enqueue.
            }
        }

        /// <summary>
        /// Remove every binding registered against the given form
        /// handle. Called from <see cref="f_FORMCLOSE"/> as the
        /// handle is freed.
        /// </summary>
        private void PurgeBindingsForHandle(int handle)
        {
            lock (_bindingsLock)
            {
                if (_bindings.Count == 0) { return; }
                List<BindingKey>? toRemove = null;
                foreach (KeyValuePair<BindingKey, string> kvp in _bindings)
                {
                    if (kvp.Key.FormHandle == handle)
                    {
                        toRemove ??= new List<BindingKey>();
                        toRemove.Add(kvp.Key);
                    }
                }
                if (toRemove is null) { return; }
                foreach (BindingKey k in toRemove) { _bindings.Remove(k); }
            }
        }

        // -----------------------------------------------------------------
        // FORMEVENTS streaming command
        // -----------------------------------------------------------------

        // Hoisted to a static readonly field to satisfy CA1861
        // (avoid allocating a fresh char[] on every command call).
        private static readonly char[] CommandArgSeparators = new[] { ' ', '\t' };

        /// <summary>
        /// Implementation of <c>FORMEVENTS [handle|"" [scope]]</c>: drain
        /// the per-form event queue(s) and write one record per line to
        /// TCC's stdout via <see cref="TakeCmd.WriteStdOut"/>. Consumed
        /// from BTM via <c>do ev in /p formevents</c>.
        /// </summary>
        /// <remarks>
        /// <para>This is the FIRST real plugin command on FormCast (no
        /// <c>@</c> prefix in TCC, no <c>f_</c> prefix in C#). Commands
        /// can write to stdout; variable functions cannot. The reason
        /// FORMEVENTS exists at all is that the captured event stream
        /// has to flow through a TCC pipe, and the only way for a
        /// plugin to put bytes into a pipe is to call
        /// <c>wwriteXP</c> against the standard output handle.</para>
        ///
        /// <para>Argument shape:
        /// <list type="bullet">
        ///   <item><description>No args: drain every form's queue,
        ///   in arbitrary form-order, in per-form FIFO.</description></item>
        ///   <item><description>One arg = handle string: drain only
        ///   that form's queue. An unknown handle returns
        ///   <c>20100</c> (no events written).</description></item>
        ///   <item><description>One arg = literal <c>""</c> sentinel:
        ///   same as no args.</description></item>
        ///   <item><description>Two args (handle, scope): scope is
        ///   accepted and ignored (reserved for future cross-process
        ///   global handle support).</description></item>
        /// </list>
        /// More than two args returns <c>20101</c>.</para>
        ///
        /// <para>Return convention is the COMMAND convention, not the
        /// variable-function convention: the integer return is the
        /// command's exit code (visible to BTM via <c>%_?</c>).
        /// <c>0</c> on success, <c>20100</c> on unknown handle,
        /// <c>20101</c> on bad arg count or unparseable handle.</para>
        ///
        /// <para>Per-line shape (PLUGIN_DESIGN.md section 4.2):
        /// <c>handle kind ctrl data</c>. The data field is escaped to
        /// keep one event on one line; see
        /// <see cref="FormEventFormatter"/>.</para>
        /// </remarks>
        public int FORMEVENTS(StringBuilder args)
        {
            string raw = args.ToString().Trim();
            args.Clear();

            int? targetHandle = null;
            if (raw.Length > 0)
            {
                // Commands are space-delimited (TCC convention),
                // unlike variable functions which are comma-delimited.
                string[] parts = raw.Split(
                    CommandArgSeparators,
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 2)
                {
                    return ErrBadArguments;
                }

                if (parts.Length >= 1)
                {
                    string first = parts[0];
                    // The "" sentinel is documented in section 4.2
                    // as "drain everything"; in BTM the user types
                    // `formevents ""` so the first token literally
                    // arrives as the two-character string "".
                    if (first.Length > 0 && first != "\"\"")
                    {
                        if (!FormHandle.TryParse(first, out int seq))
                        {
                            return ErrInvalidHandle;
                        }
                        // An unknown handle returns 20100 even though
                        // there is nothing to drain; this gives BTM
                        // scripts a clean way to detect a typo.
                        lock (_realizedFormsLock)
                        {
                            if (!_eventQueues.ContainsKey(seq))
                            {
                                return ErrInvalidHandle;
                            }
                        }
                        targetHandle = seq;
                    }
                }
                // parts[1] (scope) is intentionally ignored (reserved for future use).
            }

            IReadOnlyList<string> lines = DrainEventLines(targetHandle);
            Internal.PluginLogger.Trace($"FORMEVENTS drain: {lines.Count} event(s)");
            foreach (string line in lines)
            {
                Internal.PluginLogger.Debug($"FORMEVENTS >> {line}");
                try
                {
                    // wwriteXP needs the trailing newline so the
                    // pipe consumer's line splitter sees a record
                    // terminator. CRLF matches TCC's native console
                    // line ending.
                    TakeCmd.WriteStdOut(line + "\r\n");
                }
                catch (DllNotFoundException)
                {
                    // Headless test mode: TakeCmd.dll is not loaded
                    // in the test process so the P/Invoke target is
                    // missing. The events have already been removed
                    // from the queue; tests use DrainEventLines
                    // directly to inspect the formatted output.
                }
                catch (EntryPointNotFoundException)
                {
                    // Same: missing entry point in a stub TakeCmd.dll.
                }
                catch (BadImageFormatException)
                {
                    // Architecture mismatch: test runner bitness
                    // differs from the native TakeCmd.dll (e.g.
                    // x64 test host loading x86 DLL).
                }
            }
            return 0;
        }

        /// <summary>
        /// Drain the relevant per-form event queue(s) and return the
        /// formatted lines that <see cref="FORMEVENTS"/> would write
        /// to stdout. Pure logic, suitable for direct invocation from
        /// xUnit tests that cannot reach <c>wwriteXP</c>.
        /// </summary>
        /// <param name="handle">
        /// Optional form handle filter. <c>null</c> drains every
        /// form's queue (the default-args path); a value drains only
        /// that form's queue.
        /// </param>
        internal IReadOnlyList<string> DrainEventLines(int? handle)
        {
            List<FormEventQueue> queuesToDrain;
            lock (_realizedFormsLock)
            {
                if (handle.HasValue)
                {
                    queuesToDrain = _eventQueues.TryGetValue(handle.Value, out FormEventQueue? q)
                        ? new List<FormEventQueue> { q }
                        : new List<FormEventQueue>();
                }
                else
                {
                    queuesToDrain = new List<FormEventQueue>(_eventQueues.Values);
                }
            }

            var lines = new List<string>();
            foreach (FormEventQueue q in queuesToDrain)
            {
                foreach (FormEvent ev in q.DrainAll())
                {
                    lines.Add(FormEventFormatter.Format(ev));
                }
            }
            return lines;
        }

        /// <summary>
        /// Lazy realization helper: looks up <paramref name="handle"/>
        /// in the realized-form map; if absent, calls
        /// <see cref="FormRealizer.Realize"/> on the GUI host thread,
        /// inserts the result, and returns it. Race-safe: a concurrent
        /// realization for the same handle is detected after the
        /// expensive Realize call and the loser's Form is destroyed.
        /// </summary>
        private Form GetOrRealize(int handle, FormDescriptor descriptor)
        {
            lock (_realizedFormsLock)
            {
                if (_realizedForms.TryGetValue(handle, out Form? cached))
                {
                    return cached;
                }
            }

            // Allocate the event queue eagerly so its identity is
            // stable for the lifetime of the form. Even if the realize
            // loses the race below and we destroy our Form, the
            // queue allocation cost is negligible (one ConcurrentQueue
            // header) and the winning realize already created its own.
            var queue = new FormEventQueue();
            // Install the binding-dispatch hook so events from this
            // form can fan out to any @FORMBIND-registered TCC command.
            // The hook closure captures the registry handle so
            // DispatchBinding has both axes of the (handle, ctrl,
            // event) lookup key. Loser-race queues drop their hook
            // when their queue is dropped; no cleanup needed.
            queue.OnEnqueue = ev => DispatchBinding(handle, ev);

            // Realize OUTSIDE the lock: FormRealizer.Realize calls
            // host.Invoke, which would deadlock if the GUI thread were
            // ever waiting on _realizedFormsLock.
            Form realized = FormRealizer.Realize(descriptor, _guiHost, handle, queue);

            Form? loser = null;
            Form winner;
            lock (_realizedFormsLock)
            {
                if (_realizedForms.TryGetValue(handle, out Form? raced))
                {
                    loser = realized;
                    winner = raced;
                }
                else
                {
                    _realizedForms[handle] = realized;
                    _eventQueues[handle] = queue;
                    winner = realized;
                }
            }

            if (loser is not null)
            {
                FormRealizer.Destroy(loser, _guiHost);
            }
            return winner;
        }

        /// <summary>
        /// Implementation of <c>@FORMSETENV[name,value]</c>: write
        /// <paramref name="args"/> back into the BTM caller's variable
        /// scope by P/Invoking <c>SetEVariable</c> on TakeCmd.dll.
        /// Proves that a plugin can write back into its caller's
        /// variable scope via the P/Invoke surface.
        /// </summary>
        /// <remarks>
        /// Result convention: "0" on success; numeric error code on
        /// failure (20101 bad arguments, or the raw native return code
        /// from TCC if it rejects the assignment).
        ///
        /// Pass an empty value to delete the variable. Variable names
        /// containing "=" are rejected with 20101 because the helper
        /// composes the buffer as <c>NAME=VALUE</c> and an embedded
        /// equals would corrupt parsing on the TCC side.
        /// </remarks>
        public int f_FORMSETENV(StringBuilder args)
        {
            string[] parts = ArgParser.Split(args.ToString());
            args.Clear();

            // Accept either 1 arg (delete: name only) or 2 args (set).
            if (parts.Length < 1 || parts.Length > 2 || string.IsNullOrEmpty(parts[0]))
            {
                args.Append(ErrBadArguments.ToString(CultureInfo.InvariantCulture));
                return 0;
            }

            string name = parts[0];
            string value = parts.Length == 2 ? parts[1] : string.Empty;

            int rc = TakeCmd.SetEnv(name, value);
            args.Append(rc.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        // -----------------------------------------------------------------
        // FORMSET / FORMGET property routing helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Find a control by id within the form's descriptor tree. Supports
        /// both flat ids (<c>"ok"</c>) and slash-separated paths
        /// (<c>"panel1/ok"</c>) for nested containers.
        /// </summary>
        private static ControlDescriptor? FindControl(FormDescriptor form, string id)
        {
            // A slash-separated id walks the PANEL nesting path
            // explicitly. ResolveNestedParent only returns the leaf
            // when given a non-empty path, which is exactly what we
            // need for "find this nested control by full path".
            if (id.IndexOf('/') >= 0)
            {
                return ResolveNestedParent(form, id);
            }
            return FindControlRecursive(form.Controls, id);
        }

        private static ControlDescriptor? FindControlRecursive(
            List<ControlDescriptor> level, string id)
        {
            foreach (ControlDescriptor c in level)
            {
                if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
                if (c.Children.Count > 0)
                {
                    ControlDescriptor? nested = FindControlRecursive(c.Children, id);
                    if (nested is not null) { return nested; }
                }
            }
            return null;
        }

        /// <summary>
        /// Route a form-level <c>@FORMSET[h,.,prop,value]</c> to the
        /// matching strongly-typed descriptor field, falling through to the
        /// free-form property bag for unknown keys. Always returns
        /// <c>true</c> because the bag accepts anything.
        /// </summary>
        private static bool TrySetFormProperty(FormDescriptor form, string prop, string value)
        {
            switch (prop.ToLowerInvariant())
            {
                case "type":   form.Type = value; return true;
                case "name":   form.Name = value; return true;
                case "title":  form.Title = value; return true;
                case "x":      form.X = ParseInt(value); return true;
                case "y":      form.Y = ParseInt(value); return true;
                case "width":  form.Width = ParseInt(value); return true;
                case "height": form.Height = ParseInt(value); return true;
                case "layout": form.LayoutMode = value; return true;
                default:
                    // Unknown well-known field falls through to the
                    // form-level property bag. This is the extension
                    // point for layout config knobs (grid_rows,
                    // grid_cols, flow_hgap, etc.) that FormLayoutFactory
                    // reads at @FORMRELAYOUT time.
                    form.Properties[prop] = value;
                    return true;
            }
        }

        /// <summary>
        /// Route a form-level <c>@FORMGET[h,.,prop]</c> to the matching
        /// strongly-typed descriptor field, falling through to the property
        /// bag for unknown keys. Returns <c>null</c> when the key is
        /// absent from both the well-known set and the bag.
        /// </summary>
        private static string? TryGetFormProperty(FormDescriptor form, string prop)
        {
            switch (prop.ToLowerInvariant())
            {
                case "type":   return form.Type;
                case "name":   return form.Name;
                case "title":  return form.Title;
                case "x":      return form.X.ToString(CultureInfo.InvariantCulture);
                case "y":      return form.Y.ToString(CultureInfo.InvariantCulture);
                case "width":  return form.Width.ToString(CultureInfo.InvariantCulture);
                case "height": return form.Height.ToString(CultureInfo.InvariantCulture);
                case "layout": return form.LayoutMode;
                case "controls": return form.Controls.Count.ToString(CultureInfo.InvariantCulture);
                case "controllist":
                    {
                        var sb2 = new StringBuilder("{\"items\":[");
                        for (int i = 0; i < form.Controls.Count; i++)
                        {
                            if (i > 0) { sb2.Append(','); }
                            var c2 = form.Controls[i];
                            sb2.Append("{\"id\":\"").Append(c2.Id ?? string.Empty)
                               .Append("\",\"type\":\"").Append(c2.Type ?? string.Empty)
                               .Append("\"}");
                        }
                        sb2.Append("]}");
                        return sb2.ToString();
                    }
                default:
                    // Unknown well-known field falls through to the
                    // form-level property bag.
                    return form.Properties.TryGetValue(prop, out string? v) ? v : null;
            }
        }

        /// <summary>
        /// Route a control-level <c>@FORMSET[h,ctrl,prop,value]</c> to the
        /// appropriate descriptor field and (when the form is realized)
        /// apply the change live on the GUI thread. This is the central
        /// dispatch for all control property mutations.
        /// </summary>
        /// <remarks>
        /// <para>Well-known props (type, id, x/y/width/height, text) go to
        /// the strongly-typed descriptor fields. Props that affect realized
        /// controls (text, backcolor, value, checked, etc.) also call an
        /// Apply* helper to push the change to the live WinForms control.
        /// Unknown props go into the property bag as-is, which is the
        /// extension point for layout hints and future control-type-specific
        /// attributes.</para>
        ///
        /// <para>Pseudo-props (position, size, moveby, resizeby, delete,
        /// reparent, designtarget) perform compound operations and do not
        /// round-trip through <c>@FORMGET</c>.</para>
        /// </remarks>
        private void SetControlProperty(ControlDescriptor c, string prop, string value, int seq, FormDescriptor? form = null)
        {
            switch (prop.ToLowerInvariant())
            {
                // -- Strongly-typed descriptor fields --
                case "type":   c.Type = value; break;
                case "id":     c.Id = value; break;
                case "x":      c.X = ParseInt(value); break;
                case "y":      c.Y = ParseInt(value); break;
                case "width":  c.Width = ParseInt(value); break;
                case "height": c.Height = ParseInt(value); break;
                case "text":
                    c.Text = value;
                    // Sync the live realized control's Text property
                    // so the on-screen display updates immediately.
                    if (string.Equals(c.Type, "RICHMEMO", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyRichMemoOp(seq, c.Id, "settext", value);
                    }
                    else
                    {
                        ApplyLiveText(seq, c.Id, value);
                    }
                    break;

                // RICHMEMO live-only operations. These do not
                // round-trip through the descriptor; they mutate the
                // realized control on the GUI thread and silently
                // no-op when the form has not been realized yet.
                case "appendcolor":
                case "appendstyle":
                case "loadrules":
                    ApplyRichMemoOp(seq, c.Id, prop.ToLowerInvariant(), value);
                    break;

                // appendtext appends a line to a realized MEMO or
                // RICHMEMO without replacing the existing content.
                // For MEMO it calls TextBox.AppendText; for RICHMEMO
                // it appends a plain Run. The descriptor's Text is
                // NOT updated (live-only).
                case "appendtext":
                    AppendTextToControl(seq, c, value);
                    break;

                // LISTVIEW pseudo-props. addcolumn / additem
                // auto-number into the prop bag so the descriptor
                // round-trips through FormSerializer with no schema
                // change. clear wipes both. multiselect / sort go in
                // as plain bag entries with reserved keys.
                //
                // COMBOBOX also uses additem with a _cb.item.N prefix
                // so both control types share the same FORMSET verb
                // but store in separate key spaces.
                case "additem":
                    {
                        string upperType = (c.Type ?? string.Empty).ToUpperInvariant();
                        string prefix = upperType switch
                        {
                            "COMBOBOX" => "_cb.item.",
                            "LISTBOX" => "_lb.item.",
                            "CHECKEDLISTBOX" => "_clb.item.",
                            "DOMAINUPDOWN" => "_dud.item.",
                            _ => "_lv.item.",
                        };
                        c.Properties[NextLvKey(c, prefix)] = value;
                    }
                    break;
                case "addnode":
                    // TREEVIEW: addnode stores path:text entries.
                    c.Properties[NextLvKey(c, "_tv.node.")] = value;
                    // Live-apply: add the node to the realized TreeView
                    {
                        int sep2 = value.IndexOf(':');
                        string nodePath = sep2 >= 0 ? value.Substring(0, sep2) : value;
                        string nodeText = sep2 >= 0 ? value.Substring(sep2 + 1) : value;
                        Form? rl2;
                        lock (_realizedFormsLock) { _realizedForms.TryGetValue(seq, out rl2); }
                        if (rl2 is not null)
                        {
                            _guiHost.Invoke(() =>
                            {
                                Control? target2 = FormRealizer.FindControl(rl2, c.Id);
                                if (target2 is TreeView tv)
                                {
                                    FormRealizer.AddTreeNodeLive(tv, nodePath, nodeText);
                                }
                            });
                        }
                    }
                    break;
                case "expandall":
                    // TREEVIEW: expand all nodes
                    {
                        Form? rlEx;
                        lock (_realizedFormsLock) { _realizedForms.TryGetValue(seq, out rlEx); }
                        if (rlEx is not null)
                        {
                            _guiHost.Invoke(() =>
                            {
                                Control? tvEx = FormRealizer.FindControl(rlEx, c.Id);
                                if (tvEx is TreeView tvExpand)
                                {
                                    tvExpand.ExpandAll();
                                }
                            });
                        }
                    }
                    break;
                case "addcolumn":
                    // LISTVIEW and DATAGRID share addcolumn.
                    if (string.Equals(c.Type, "DATAGRID", StringComparison.OrdinalIgnoreCase))
                    {
                        c.Properties[NextLvKey(c, "_dg.col.")] = value;
                    }
                    else
                    {
                        c.Properties[NextLvKey(c, "_lv.col.")] = value;
                    }
                    break;
                case "addrow":
                    // DATAGRID: addrow stores cell0:cell1:... entries.
                    c.Properties[NextLvKey(c, "_dg.row.")] = value;
                    break;
                case "clear":
                    RemoveLvKeys(c, "_lv.col.");
                    RemoveLvKeys(c, "_lv.item.");
                    break;
                case "multiselect":
                    c.Properties["_lv.multiselect"] = value;
                    break;
                case "sort":
                    c.Properties["_lv.sort"] = value;
                    break;

                // Designer operations for live manipulation of
                // realized controls.
                case "delete":
                    if (form is not null) { DeleteControl(seq, c, form); }
                    break;
                case "bringtofront":
                    ApplyZOrder(seq, c.Id, true);
                    break;
                case "sendtoback":
                    ApplyZOrder(seq, c.Id, false);
                    break;
                case "tabindex":
                    c.Properties["tabindex"] = value;
                    ApplyTabIndex(seq, c.Id, ParseInt(value));
                    break;
                case "reparent":
                    // value = new parent id (empty = form root)
                    if (form is not null) { ReparentControl(seq, c, form, value); }
                    break;

                // Attach a runtime-only context menu to a realized
                // control. The menu is NOT stored in the descriptor
                // so @FORMSAVE produces a clean template. value = the
                // control id of a CONTEXTMENU on the SAME form (or a
                // different form's handle:ctrlid).
                case "runtimecontextmenu":
                    AttachRuntimeContextMenu(seq, c.Id, value);
                    break;

                // tooltip sets hover text on a realized control.
                // Also stored in the prop bag so the realizer can
                // apply it at show time.
                case "tooltip":
                    c.Properties["tooltip"] = value;
                    ApplyTooltip(seq, c.Id, value);
                    break;

                // Designer pseudo-props. position / size set absolute
                // coordinates from a single comma-separated value;
                // moveby / resizeby apply a delta. The designer BTM
                // reads mouse drag deltas and pushes them through
                // these without having to issue separate FORMSET calls
                // per axis.
                case "position":
                    {
                        (int x, int y) = ParseIntPair(value);
                        c.X = x;
                        c.Y = y;
                        ApplyLiveBounds(seq, c.Id, x, y, c.Width, c.Height);
                    }
                    break;
                case "size":
                    {
                        (int w, int h) = ParseIntPair(value);
                        c.Width = w;
                        c.Height = h;
                        ApplyLiveBounds(seq, c.Id, c.X, c.Y, w, h);
                    }
                    break;
                case "moveby":
                    {
                        (int dx, int dy) = ParseIntPair(value);
                        c.X += dx;
                        c.Y += dy;
                        ApplyLiveBounds(seq, c.Id, c.X, c.Y, c.Width, c.Height);
                    }
                    break;
                case "resizeby":
                    {
                        (int dw, int dh) = ParseIntPair(value);
                        c.Width += dw;
                        c.Height += dh;
                        ApplyLiveBounds(seq, c.Id, c.X, c.Y, c.Width, c.Height);
                    }
                    break;

                case "backcolor":
                case "forecolor":
                    c.Properties[prop] = value;
                    ApplyColor(seq, c.Id, prop, value);
                    break;

                case "value":
                case "min":
                case "max":
                case "checked":
                    c.Properties[prop] = value;
                    ApplyLiveProperty(seq, c.Id, prop, value);
                    break;

                case "autoscroll":
                    c.Properties[prop] = value;
                    ApplyLiveAutoScroll(seq, c.Id, value);
                    break;

                case "splitterdistance":
                    c.Properties[prop] = value;
                    ApplyLiveSplitterDistance(seq, c.Id, value);
                    break;

                case "textfromvar":
                    // Read text from a TCC variable by NAME.
                    ApplyTextFromVar(seq, c, value);
                    break;

                case "textfromfile":
                    // Read text from a file path and set as
                    // the control's text. Bypasses TCC variable
                    // expansion entirely.
                    ApplyTextFromFile(seq, c, value);
                    break;

                case "stockicon":
                    c.Properties[prop] = value;
                    ApplyLiveStockIcon(seq, c.Id, value);
                    break;

                case "designtarget":
                    // Bind a PROPERTYGRID to a control or form
                    // descriptor for the visual designer.
                    // value = "handle:ctrlId" or "handle:." for form.
                    BindPropertyGrid(seq, c.Id, value);
                    break;

                default:
                    // Unknown well-known field: stash in the property
                    // bag. This is the extension point for layout hints
                    // (row=N, col=N, dock=top, etc.) and any future
                    // control-type-specific attributes.
                    c.Properties[prop] = value;
                    break;
            }
        }

        /// <summary>
        /// Find the next free numbered key in the LISTVIEW prop bag
        /// for a given prefix (<c>_lv.col.</c> or <c>_lv.item.</c>).
        /// Numbering is dense and monotonic; gaps left by future
        /// remove-column ops are NOT reused (the column index doubles
        /// as the position in the on-screen ListView, so reuse would
        /// confuse the bind-by-index assertions).
        /// </summary>
        private static string NextLvKey(ControlDescriptor c, string prefix)
        {
            int n = 0;
            while (c.Properties.ContainsKey(prefix + n.ToString(CultureInfo.InvariantCulture)))
            {
                n++;
            }
            return prefix + n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Remove all numbered prop bag entries that start with the given
        /// prefix. Used by the "clear" pseudo-prop to wipe LISTVIEW
        /// columns and items.
        /// </summary>
        private static void RemoveLvKeys(ControlDescriptor c, string prefix)
        {
            var toRemove = new List<string>();
            foreach (string key in c.Properties.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(key);
                }
            }
            foreach (string key in toRemove) { c.Properties.Remove(key); }
        }

        /// <summary>
        /// Route a control-level <c>@FORMGET[h,ctrl,prop]</c> to the
        /// matching strongly-typed descriptor field, falling through to
        /// the property bag for unknown keys. Returns <c>null</c> when
        /// the key is absent from both the well-known set and the bag.
        /// </summary>
        private static string? TryGetControlProperty(ControlDescriptor c, string prop)
        {
            switch (prop.ToLowerInvariant())
            {
                case "type":   return c.Type;
                case "id":     return c.Id;
                case "x":      return c.X.ToString(CultureInfo.InvariantCulture);
                case "y":      return c.Y.ToString(CultureInfo.InvariantCulture);
                case "width":  return c.Width.ToString(CultureInfo.InvariantCulture);
                case "height": return c.Height.ToString(CultureInfo.InvariantCulture);
                case "text":   return c.Text;
                // Designer reads: position and size return the colon-
                // separated pair so the BTM designer can pull both
                // axes in one FORMGET call. The colon matches the
                // FORMSET writer (see ParseIntPair) so a get-then-set
                // round trip preserves the value, and unlike `|` it
                // survives TCC SET assignment without being treated as
                // a pipe operator.
                case "position":
                    return c.X.ToString(CultureInfo.InvariantCulture) + ":" +
                           c.Y.ToString(CultureInfo.InvariantCulture);
                case "size":
                    return c.Width.ToString(CultureInfo.InvariantCulture) + ":" +
                           c.Height.ToString(CultureInfo.InvariantCulture);
                default:
                    return c.Properties.TryGetValue(prop, out string? v) ? v : null;
            }
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v
                : 0;
        }

        /// <summary>
        /// Designer helper: parse a colon-separated <c>"a:b"</c>
        /// pair into two ints. Used by the position / size / moveby /
        /// resizeby pseudo-props on FORMSET. Colon (not comma)
        /// because comma is the @FORMSET arg separator and would
        /// split the value into extra positional args; not pipe
        /// because BTM `SET POS=%@formget[...]` round trips through
        /// TCC's pipe parser, which would truncate at the first `|`.
        /// Either side missing or malformed falls back to 0 for
        /// that axis.
        /// </summary>
        private static (int, int) ParseIntPair(string value)
        {
            if (string.IsNullOrEmpty(value)) { return (0, 0); }
            int sep = value.IndexOf(':');
            if (sep < 0) { return (ParseInt(value), 0); }
            return (
                ParseInt(value.Substring(0, sep)),
                ParseInt(value.Substring(sep + 1)));
        }

        // -----------------------------------------------------------------
        // Lifecycle (Initialize / Shutdown)
        // -----------------------------------------------------------------

        /// <inheritdoc />
        public bool Initialize()
        {
            try
            {
                // Install a directory-based assembly resolver BEFORE
                // any code path can touch System.Text.Json. The .NET
                // Framework strong-name loader will not bind to a
                // different version than the one System.Text.Json was
                // compiled against (e.g. it asks for
                // System.Runtime.CompilerServices.Unsafe 4.0.4.1 but
                // the file we ship is 6.0.0.0). Probing the plugin's
                // own bin directory by short name and returning
                // whatever is on disk satisfies the load even when
                // strong names don't line up. This is the side-by-
                // side pattern documented for plugins hosted by EXEs
                // whose own app.config does not carry our redirects.
                EnsureAssemblyResolverInstalled();

                _callbackWorker.UnhandledException += (s, e) =>
                    TryAppendMarker(
                        $"CallbackWorker action threw: {e.Exception.GetType().Name}: {e.Exception.Message}");
                _callbackWorker.Start();

                _guiHost.UnhandledException += (s, e) =>
                    TryAppendMarker(
                        $"GuiHostThread action threw: {e.Exception.GetType().Name}: {e.Exception.Message}");
                _guiHost.Start();

                WriteMarker(initializing: true);
                TryAppendMarker(
                    $"  guiHostStarted:   True (threadId={_guiHost.GuiThreadId})");
                return true;
            }
            catch (Exception ex)
            {
                // We must never throw out of Initialize: an unhandled
                // exception in a plugin entry point can destabilize TCC's
                // host. Log to the marker file (best effort) and return
                // false so TCC unloads us cleanly.
                TryAppendMarker($"Initialize FAILED: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// REQUIRED CONTRACT (see PLUGIN_DESIGN.md section 4.6, "Forced
        /// shutdown contract"): when this method returns, no window
        /// FormCast created may still be on screen. A surviving window
        /// holds event handlers that point into the unloaded assembly,
        /// and the next click on it crashes TCC. The implementation
        /// MUST:
        ///
        ///   1. Snapshot the local-form registry under the registry lock.
        ///   2. Drain any FORMEVENTS pipes by writing a sentinel record
        ///      and closing each pipe so consuming `do x in /p` loops exit.
        ///   3. Marshal a force-close to the GuiHostThread for every
        ///      open form (set internal _forcedShutdown flag, call
        ///      Form.Close so user ON CLOSE handlers run, then call
        ///      Form.Dispose unconditionally).
        ///   4. Dispose any ElementHost instances (RICHMEMO uses these)
        ///      before the WinForms loop exits, or the WPF dispatcher
        ///      deadlocks at thread teardown.
        ///   5. Tear down the GuiHostThread via Application.ExitThread
        ///      and Thread.Join with a bounded timeout (5 seconds).
        ///   6. Clear the local-form handle table.
        ///
        /// The same path runs whether <paramref name="endProcess"/> is
        /// true or false; only the recyclability of the GuiHostThread
        /// differs (we always tear it down, but on endProcess=false the
        /// next PLUGIN /L gets a fresh thread).
        /// </remarks>
        public bool Shutdown(bool endProcess)
        {
            try
            {
                // Forced-shutdown contract: set the sentinel, then
                // destroy every realized form on the GUI thread BEFORE
                // tearing down the message loop. The sentinel makes
                // sure any FormClosing handler that tries to cancel
                // is overridden.
                _guiHost.SetForcedShutdown();
                List<Form> toDestroy;
                lock (_realizedFormsLock)
                {
                    toDestroy = new List<Form>(_realizedForms.Values);
                    _realizedForms.Clear();
                    _eventQueues.Clear();
                }
                // Drop every @FORMBIND entry. Any worker actions
                // already scheduled by DispatchBinding still hold
                // their captured command string and will run as part
                // of the worker drain below; new events fired during
                // teardown find no binding and are no-ops.
                lock (_bindingsLock)
                {
                    _bindings.Clear();
                }
                foreach (Form f in toDestroy)
                {
                    try
                    {
                        FormRealizer.Destroy(f, _guiHost);
                    }
                    catch (Exception ex)
                    {
                        TryAppendMarker(
                            $"Shutdown: forced-close threw for a realized form: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Dispose the system tray icon if present.
                if (_notifyIcon is not null)
                {
                    try
                    {
                        _guiHost.Invoke(() =>
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Dispose();
                            _notifyIcon = null;
                        });
                    }
                    catch { /* swallow: GUI thread may already be gone */ }
                }

                bool guiJoined = _guiHost.Stop();
                _guiHost.Dispose();

                bool workerJoined = _callbackWorker.Stop();
                _callbackWorker.Dispose();

                TryAppendMarker(
                    $"Shutdown at {Timestamp()}; endProcess={endProcess}; " +
                    $"forcedClosed={toDestroy.Count}; " +
                    $"guiHostJoined={guiJoined}; callbackWorkerJoined={workerJoined}");

                Internal.PluginLogger.Shutdown();
                return true;
            }
            catch
            {
                // Same rationale as Initialize: never throw from a host
                // entry point. Returning false is logged by TCC but does
                // not block the unload.
                return false;
            }
        }

        // -----------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Write the initial marker file. Captures the .NET runtime
        /// version, the assembly version, the process id, and (most
        /// importantly) the result of a real
        /// <see cref="TakeCmd.ExpandVariables"/> P/Invoke call against a
        /// known-good TCC variable (<c>%COMSPEC%</c>). If the marker file
        /// contains a sensible expansion (a path ending in <c>tcc.exe</c>),
        /// the .NET -> C++/CLI bridge -> native TakeCmd.dll round-trip works.
        /// </summary>
        private static void WriteMarker(bool initializing)
        {
            if (!MarkerEnabled()) { return; }
            var asm = typeof(Plugin).Assembly;
            var version = asm.GetName().Version?.ToString() ?? "unknown";
            var location = asm.Location;
            var dotnetVersion = Environment.Version.ToString();
            var clrVersion = typeof(string).Assembly.ImageRuntimeVersion;
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;

            // Real P/Invoke call. Pre-size the buffer generously -- TCC's
            // ExpandVariables expects a writable buffer with room for the
            // expanded result.
            const string toExpand = "%COMSPEC%";
            var buffer = new StringBuilder(toExpand, capacity: 1024);
            int expandResult = TakeCmd.ExpandVariables(buffer, 0);

            // Also probe the unicode-output flag while we're here, since
            // we'll need it for any future writers.
            int unicodeFlag = TakeCmd.QueryUnicodeOutput();

            // Refresh headless state from the environment now that we
            // are running inside the host process. The static field was
            // initialized at first access, but on a real load the env
            // var may have been set right before plugin /l, so re-read.
            HeadlessMode.Refresh();

            using var writer = new StreamWriter(MarkerFilePath, append: false, Encoding.UTF8);
            writer.WriteLine("FormCast plugin initialized");
            writer.WriteLine($"  timestamp:        {Timestamp()}");
            writer.WriteLine($"  initializing:     {initializing}");
            writer.WriteLine($"  headless:         {HeadlessMode.IsEnabled}");
            writer.WriteLine($"  assembly:         {asm.FullName}");
            writer.WriteLine($"  assembly version: {version}");
            writer.WriteLine($"  assembly path:    {location}");
            writer.WriteLine($"  Environment.Version (legacy): {dotnetVersion}");
            writer.WriteLine($"  CLR ImageRuntimeVersion:      {clrVersion}");
            writer.WriteLine($"  process id:       {processId}");
            writer.WriteLine();
            writer.WriteLine("--- TakeCmd.dll P/Invoke round-trip ---");
            writer.WriteLine($"  ExpandVariables(\"{toExpand}\") returned: {expandResult}");
            writer.WriteLine($"  expanded value:                          {buffer}");
            writer.WriteLine($"  QueryUnicodeOutput() returned:           {unicodeFlag}");
            writer.WriteLine();
            writer.WriteLine("If the expanded value above is a real path ending in tcc.exe,");
            writer.WriteLine("the .NET to C++/CLI to TakeCmd.dll bridge is working.");
        }

        // -----------------------------------------------------------------
        // Side-by-side assembly resolver
        // -----------------------------------------------------------------

        /// <summary>
        /// Latch so the AppDomain.AssemblyResolve hook is installed
        /// at most once even if the plugin is loaded, unloaded, and
        /// reloaded inside the same TCC process. AppDomain handlers
        /// persist across plugin load cycles (the host does not unload
        /// our AppDomain), so a second subscription would call the
        /// resolver twice for every miss.
        /// </summary>
        private static int _resolverInstalled;

        /// <summary>
        /// Idempotent installer for the directory-based assembly
        /// resolver. Called from <see cref="Initialize"/> before
        /// any code path that touches System.Text.Json or its
        /// transitive dependencies.
        /// </summary>
        private static void EnsureAssemblyResolverInstalled()
        {
            if (System.Threading.Interlocked.Exchange(ref _resolverInstalled, 1) != 0)
            {
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;
        }

        /// <summary>
        /// Probe FormCast.dll's directory for an assembly with the
        /// requested short name. Returns the loaded assembly on a hit
        /// (regardless of strong-name version), or <c>null</c> to let
        /// the next handler in the AssemblyResolve invocation list run.
        /// </summary>
        /// <remarks>
        /// <para>This handler intentionally returns the file with the
        /// matching short name even when its strong-name version does
        /// not match the request. The .NET Framework loader's default
        /// strict binding refuses to bind across versions; this is the
        /// FormCast-side override that lets System.Text.Json find a
        /// 6.0.0.0 copy of <c>System.Runtime.CompilerServices.Unsafe</c>
        /// when it was compiled against 4.0.4.1.</para>
        ///
        /// <para>The handler does not cache results: <see cref="Assembly.LoadFrom(string)"/>
        /// is itself idempotent for the same path, and the CLR caches
        /// successful loads at the AppDomain level. A failed load
        /// throws and we return <c>null</c> so the regular probing path
        /// runs and the original FileNotFoundException reaches the
        /// caller with the original missing-version detail.</para>
        /// </remarks>
        private static Assembly? ResolvePluginDependency(object? sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);
                if (string.IsNullOrEmpty(requested.Name)) { return null; }

                // Skip resource satellite assemblies (they have a
                // .resources suffix and are loaded per-culture; the
                // CLR knows how to fail those gracefully).
                if (requested.Name.EndsWith(".resources", StringComparison.Ordinal))
                {
                    return null;
                }

                string baseDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
                    ?? AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir)) { return null; }

                string candidate = Path.Combine(baseDir, requested.Name + ".dll");
                if (!File.Exists(candidate)) { return null; }

                return Assembly.LoadFrom(candidate);
            }
            catch (Exception ex)
            {
                // Logging the resolve failure is best-effort; never
                // throw out of an AssemblyResolve handler.
                TryAppendMarker(
                    $"AssemblyResolve failed for {args.Name}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Append a single line to the marker file, swallowing any I/O
        /// errors. Used for non-fatal status updates from places where
        /// throwing would be worse than losing a log line.
        /// </summary>
        private static void TryAppendMarker(string line)
        {
            if (!MarkerEnabled()) { return; }
            try
            {
                File.AppendAllText(
                    MarkerFilePath,
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // Best effort; the marker file is diagnostic, not load-bearing.
            }
        }

        private static string Timestamp() =>
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
