// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/FormRealizer.cs
// =====================
//
// The realizer is the bridge from the logical-layer POCOs
// (FormDescriptor, ControlDescriptor) to actual System.Windows.Forms
// objects living on the GuiHostThread.
//
// Architectural notes:
//
//   - All Form construction happens INSIDE host.Invoke(...) so the
//     Form's owning thread is the GuiHostThread, not whatever thread
//     called Realize. WinForms tracks owning threads on every control
//     and throws InvalidOperationException on cross-thread access.
//
//   - Forms are constructed with Visible = false. The
//     unattended-automation-first principle in PLUGIN_DESIGN.md
//     depends on this: every test BTM that creates and queries a
//     Form does it without ever flashing a window in front of a user.
//
//   - Every realized Form gets a FormClosing handler that consults
//     host.ForcedShutdown. The forced-shutdown contract
//     requires that Plugin.Shutdown can guarantee tear-down of every
//     window FormCast created -- a surviving window holds delegate
//     references into our (now-unloaded) assembly, and the next click
//     on it crashes TCC. The handler clears any user-set e.Cancel
//     when forced shutdown is in effect, so a misbehaving close
//     handler cannot keep a window alive past plugin /u.
//
// Property bag handling applies the universal subset (text, position,
// size) plus per-type extensions as each dispatch surface needs them.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using FormCast.Threading;

namespace FormCast.Forms
{
    /// <summary>
    /// Constructs real <see cref="Form"/> instances from
    /// <see cref="FormDescriptor"/> POCOs on the
    /// <see cref="GuiHostThread"/>. All public methods marshal onto
    /// the GUI thread internally; callers may invoke from any thread.
    /// </summary>
    internal static class FormRealizer
    {
        /// <summary>
        /// Construct a hidden <see cref="Form"/> for the given
        /// descriptor on the GUI thread. The returned form is created
        /// with <c>Visible = false</c> and a sentinel
        /// <c>FormClosing</c> handler that honors the host's
        /// forced-shutdown flag.
        /// </summary>
        /// <param name="descriptor">Form descriptor to realize.</param>
        /// <param name="host">GUI host thread that will own the form.</param>
        /// <param name="formHandle">Registry handle of the form (stamped onto
        /// every event the realized form pushes into <paramref name="eventQueue"/>).</param>
        /// <param name="eventQueue">Optional per-form event queue. When supplied,
        /// the realizer wires WinForms event handlers (Click, CheckedChanged,
        /// TextChanged, FormClosing) that push <see cref="FormEvent"/> records
        /// into the queue. The FORMEVENTS streaming command drains it.</param>
        /// <returns>The realized form, owned by the GUI thread.</returns>
        public static Form Realize(
            FormDescriptor descriptor,
            GuiHostThread host,
            int formHandle = 0,
            FormEventQueue? eventQueue = null)
        {
            if (descriptor is null) { throw new ArgumentNullException(nameof(descriptor)); }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }

            Form? result = null;
            host.Invoke(() =>
            {
                result = BuildOnGuiThread(descriptor, host, formHandle, eventQueue);
            });
            return result!;
        }

        /// <summary>
        /// Render the realized form to a PNG file on disk. The render
        /// happens on the GUI thread because <c>Control.DrawToBitmap</c>
        /// requires the control's owning thread, and PNG encoding
        /// happens inline so any I/O failure surfaces synchronously.
        /// </summary>
        /// <param name="form">A realized form, owned by the GUI thread.</param>
        /// <param name="host">The GUI host that owns the form.</param>
        /// <param name="path">Output file path. Must be writable.</param>
        /// <remarks>
        /// The form's window handle is forced into existence (via
        /// <c>_ = form.Handle</c>) before <c>DrawToBitmap</c> is
        /// called, because <c>DrawToBitmap</c> against a form whose
        /// HWND has not been created is a no-op that yields a blank
        /// bitmap. Forms constructed by <see cref="Realize"/> have not
        /// had their handles forced yet (the realizer is lazy on
        /// purpose so that pure-descriptor flows do not pay for HWND
        /// creation).
        /// </remarks>
        public static void SaveImage(Form form, GuiHostThread host, string path)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            host.Invoke(() =>
            {
                int w = Math.Max(1, form.Width);
                int h = Math.Max(1, form.Height);
                using var bmp = new System.Drawing.Bitmap(w, h);
                ForceHandlesRecursive(form);
                // Briefly show the form off-screen so complex
                // composite controls (LISTVIEW's internal column
                // header child window in particular) actually
                // create their inner Win32 control hierarchy.
                // Without this DrawToBitmap on a never-shown form
                // can paint LISTVIEW as a blank gray rectangle
                // because the header HWND has not been created
                // yet. ShowInTaskbar is already false on every
                // realized form so this never adds a taskbar
                // entry; the off-screen Location is hidden under
                // every reasonable monitor configuration.
                System.Drawing.Point originalLocation = form.Location;
                FormStartPosition originalStart = form.StartPosition;
                bool wasVisible = form.Visible;
                if (!wasVisible)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-32000, -32000);
                    form.Show();
                    // Pump the message loop several times so animated
                    // controls (ProgressBar, etc.) finish painting.
                    for (int pump = 0; pump < 5; pump++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(50);
                    }
                }
                try
                {
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, w, h));
                }
                finally
                {
                    if (!wasVisible)
                    {
                        form.Hide();
                        form.Location = originalLocation;
                        form.StartPosition = originalStart;
                    }
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            });
        }

        /// <summary>
        /// Walk the control tree and touch every control's Handle
        /// property to force HWND creation on the current thread.
        /// Without this, <c>Control.DrawToBitmap</c> against a
        /// never-shown form paints the form's background but the
        /// child controls render as empty regions because their
        /// HWNDs do not yet exist. Recursive: panels and other
        /// containers are walked too.
        /// </summary>
        /// <summary>
        /// Render multiple realized forms into a single composite PNG.
        /// Each form is drawn at its current screen position relative to
        /// the bounding rectangle of all forms. The background is filled
        /// with <paramref name="background"/> (default: transparent).
        /// </summary>
        public static void SaveCompositeImage(
            IReadOnlyList<Form> forms,
            GuiHostThread host,
            string path,
            System.Drawing.Color? background = null)
        {
            if (forms is null || forms.Count == 0)
            {
                throw new ArgumentException("At least one form is required.", nameof(forms));
            }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            host.Invoke(() =>
            {
                // Calculate bounding rectangle of all forms
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;
                foreach (Form f in forms)
                {
                    if (f.IsDisposed) { continue; }
                    int left = f.Left;
                    int top = f.Top;
                    int right = left + f.Width;
                    int bottom = top + f.Height;
                    if (left < minX) { minX = left; }
                    if (top < minY) { minY = top; }
                    if (right > maxX) { maxX = right; }
                    if (bottom > maxY) { maxY = bottom; }
                }

                int totalW = Math.Max(1, maxX - minX);
                int totalH = Math.Max(1, maxY - minY);

                using var composite = new System.Drawing.Bitmap(totalW, totalH);
                using (var g = System.Drawing.Graphics.FromImage(composite))
                {
                    g.Clear(background ?? System.Drawing.Color.Transparent);

                    foreach (Form f in forms)
                    {
                        if (f.IsDisposed) { continue; }
                        int w = Math.Max(1, f.Width);
                        int h = Math.Max(1, f.Height);
                        using var bmp = new System.Drawing.Bitmap(w, h);
                        ForceHandlesRecursive(f);
                        bool wasVisible = f.Visible;
                        System.Drawing.Point origLoc = f.Location;
                        FormStartPosition origStart = f.StartPosition;
                        if (!wasVisible)
                        {
                            f.StartPosition = FormStartPosition.Manual;
                            f.Location = new System.Drawing.Point(-32000, -32000);
                            f.Show();
                            for (int pump = 0; pump < 5; pump++)
                            {
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        try
                        {
                            f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, w, h));
                        }
                        finally
                        {
                            if (!wasVisible)
                            {
                                f.Hide();
                                f.Location = origLoc;
                                f.StartPosition = origStart;
                            }
                        }
                        g.DrawImage(bmp, f.Left - minX, f.Top - minY);
                    }
                }
                composite.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            });
        }

        private static void ForceHandlesRecursive(Control control)
        {
            _ = control.Handle;
            foreach (Control child in control.Controls)
            {
                ForceHandlesRecursive(child);
            }
        }

        /// <summary>
        /// Render the realized form to a fresh in-memory
        /// <see cref="System.Drawing.Bitmap"/>. The bitmap is owned
        /// by the caller and must be disposed. Used by xUnit tests
        /// that compare two renders against each other via
        /// <see cref="BitmapDiff"/> without going through the file
        /// system.
        /// </summary>
        public static System.Drawing.Bitmap SnapshotToBitmap(Form form, GuiHostThread host)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            System.Drawing.Bitmap? result = null;
            host.Invoke(() =>
            {
                ForceHandlesRecursive(form);
                int w = Math.Max(1, form.Width);
                int h = Math.Max(1, form.Height);
                var bmp = new System.Drawing.Bitmap(w, h);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, w, h));
                result = bmp;
            });
            return result!;
        }

        /// <summary>
        /// Cohabitation probe: show <paramref name="form"/> on the
        /// GUI thread via <c>Form.Show()</c>, schedule a one-shot
        /// <see cref="System.Windows.Forms.Timer"/> on the same thread
        /// that closes the form after <paramref name="autoCloseMs"/>
        /// milliseconds, and return immediately. The caller can wait
        /// for closure by polling <c>form.IsDisposed</c> through
        /// <see cref="GuiHostThread.Invoke(Action)"/>.
        /// </summary>
        /// <param name="form">A realized form, owned by the GUI thread.</param>
        /// <param name="host">The GUI host that owns the form.</param>
        /// <param name="autoCloseMs">Delay before the timer fires and
        /// closes the form. Must be greater than zero. The probe BTMs
        /// and xUnit tests use 200-500 ms so the cycle is unattended.</param>
        /// <remarks>
        /// <para>This validates that the dedicated-STA cohabitation
        /// strategy (see PLUGIN_DESIGN.md section 7 #17) actually works
        /// by pumping a real <c>Form.Show()</c> call and a follow-up
        /// <c>Form.Close()</c> through the existing
        /// <see cref="GuiHostThread"/> message loop.</para>
        /// <para>The timer is constructed on the GUI thread and ticks
        /// on the GUI thread, so the close runs in-context. The
        /// timer instance is captured in the closure and disposed on
        /// the first tick to make the helper one-shot regardless of
        /// what the underlying <see cref="System.Windows.Forms.Timer"/>
        /// would do otherwise.</para>
        /// </remarks>
        public static void ShowAutoClose(Form form, GuiHostThread host, int autoCloseMs)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            if (autoCloseMs <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(autoCloseMs),
                    "autoCloseMs must be greater than zero.");
            }

            host.Invoke(() =>
            {
                if (form.IsDisposed) { return; }
                _ = form.Handle;  // force HWND on the GUI thread
                form.Show();

                var timer = new System.Windows.Forms.Timer { Interval = autoCloseMs };
                timer.Tick += (s, e) =>
                {
                    try { timer.Stop(); } catch { /* swallow */ }
                    try { timer.Dispose(); } catch { /* swallow */ }
                    if (!form.IsDisposed)
                    {
                        try { form.Close(); } catch { /* swallow */ }
                    }
                };
                timer.Start();
            });
        }

        /// <summary>
        /// Tear down a realized form on the GUI thread. Calls
        /// <c>Form.Close</c> followed by <c>Form.Dispose</c>.
        /// Safe to call multiple times and safe to call on a form
        /// that has already been disposed. Use this instead of a
        /// direct <c>form.Dispose()</c> from a non-GUI thread, which
        /// would throw a cross-thread access exception.
        /// </summary>
        public static void Destroy(Form? form, GuiHostThread host)
        {
            if (form is null) { return; }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }

            host.Invoke(() =>
            {
                if (form.IsDisposed) { return; }
                try { form.Close(); } catch { /* swallow */ }
                try { form.Dispose(); } catch { /* swallow */ }
            });
        }

        /// <summary>
        /// Result of a <see cref="Simulate"/> call. Distinguishes the
        /// "everything dispatched cleanly" path from the various
        /// failure modes the BTM caller needs to differentiate (no
        /// such control on the form, action name not recognized,
        /// action recognized but not applicable to the control type).
        /// </summary>
        internal enum SimulateResult
        {
            /// <summary>The action ran on the GUI thread without throwing.</summary>
            Success = 0,
            /// <summary>No control with the given id exists on the form.</summary>
            UnknownControl = 1,
            /// <summary>The action name is not in the recognized set.</summary>
            UnknownAction = 2,
            /// <summary>The action is recognized but does not apply to this
            /// control type (e.g. <c>type</c> on a Button).</summary>
            UnsupportedForControl = 3,
        }

        /// <summary>
        /// Synthetic event dispatch. Marshals to the GUI thread, finds
        /// the named control on the realized form, and performs the
        /// requested action. Recognized actions: <c>click</c>,
        /// <c>type</c>, <c>settext</c>, <c>check</c>, <c>uncheck</c>,
        /// <c>focus</c>, <c>blur</c>, <c>dblclick</c>,
        /// <c>mousedown</c>/<c>mouseup</c>, <c>keypress</c>. The goal
        /// is to drive the underlying WinForms event handlers
        /// programmatically without requiring a visible window.
        /// </summary>
        /// <remarks>
        /// <para>The action runs synchronously on the GUI thread via
        /// <see cref="GuiHostThread.Invoke"/>; if the WinForms handler
        /// itself does work that needs to happen on the worker thread,
        /// that's the caller's responsibility (@FORMBIND callbacks
        /// route to the CallbackWorker).</para>
        ///
        /// <para>For controls that recognize <c>click</c>
        /// (<c>Button</c>, <c>CheckBox</c>, <c>RadioButton</c>) the call
        /// goes through <c>PerformClick</c>, which raises the same
        /// <c>Click</c> event a real mouse click would. <c>type</c>
        /// only applies to <c>TextBox</c> (it calls <c>AppendText</c>);
        /// <c>settext</c> writes <c>Control.Text</c> directly and works
        /// on any control. <c>check</c> / <c>uncheck</c> apply to
        /// <c>CheckBox</c>, plus <c>check</c> on <c>RadioButton</c>.</para>
        /// </remarks>
        internal static SimulateResult Simulate(
            Form form,
            GuiHostThread host,
            string controlId,
            string action,
            string? value)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            if (controlId is null) { throw new ArgumentNullException(nameof(controlId)); }
            if (action is null) { throw new ArgumentNullException(nameof(action)); }

            SimulateResult result = SimulateResult.Success;
            host.Invoke(() =>
            {
                Control? target = FindControlByName(form, controlId);

                // ToolStripItems (toolbar/menu buttons) are not Controls.
                // Fall back to searching all ToolStrips on the form.
                ToolStripItem? tsItem = null;
                if (target is null)
                {
                    tsItem = FindToolStripItem(form, controlId);
                    if (tsItem is null)
                    {
                        result = SimulateResult.UnknownControl;
                        return;
                    }
                }

                switch (action.ToLowerInvariant())
                {
                    case "click":
                        // ToolStripItem click via PerformClick
                        if (tsItem is not null)
                        {
                            tsItem.PerformClick();
                            break;
                        }
                        // We cannot use Button.PerformClick / Radio.PerformClick
                        // here because those methods short-circuit on
                        // !CanSelect, and our headless forms are
                        // Visible = false (which forces CanSelect to
                        // false). Synthetic clicks need to fire
                        // on never-shown forms, so we bypass via
                        // reflection and invoke the protected OnClick
                        // method directly. CheckBox/RadioButton also
                        // get their Checked state mutated to match
                        // what a real mouse click would have done.
                        // CheckBox.OnClick and RadioButton.OnClick
                        // toggle Checked internally (when AutoCheck is
                        // true, the default), so we MUST NOT pre-toggle
                        // before calling OnClick or we'd double-toggle
                        // and net to no state change. Plain Button has
                        // no internal state mutation in OnClick.
                        if (target is Button or CheckBox or RadioButton)
                        {
                            RaiseOnClick(target);
                        }
                        else
                        {
                            result = SimulateResult.UnsupportedForControl;
                        }
                        break;

                    case "type":
                        if (target is TextBox tb) { tb.AppendText(value ?? string.Empty); }
                        else { result = SimulateResult.UnsupportedForControl; }
                        break;

                    case "settext":
                        if (target is not null) { target.Text = value ?? string.Empty; }
                        else { result = SimulateResult.UnsupportedForControl; }
                        break;

                    case "check":
                        if (target is CheckBox cbk) { cbk.Checked = true; }
                        else if (target is RadioButton rbk) { rbk.Checked = true; }
                        else { result = SimulateResult.UnsupportedForControl; }
                        break;

                    case "uncheck":
                        if (target is CheckBox cbu) { cbu.Checked = false; }
                        else { result = SimulateResult.UnsupportedForControl; }
                        break;

                    case "focus":
                        if (target is null) { result = SimulateResult.UnsupportedForControl; break; }
                        _ = form.Handle;
                        _ = target.Handle;
                        RaiseOnEnter(target);
                        break;

                    case "blur":
                        if (target is null) { result = SimulateResult.UnsupportedForControl; break; }
                        _ = form.Handle;
                        _ = target.Handle;
                        RaiseOnLeave(target);
                        break;

                    case "dblclick":
                        if (target is null) { result = SimulateResult.UnsupportedForControl; break; }
                        if (target is Button or CheckBox or RadioButton or Label or Panel)
                        {
                            RaiseOnDoubleClick(target);
                        }
                        else
                        {
                            result = SimulateResult.UnsupportedForControl;
                        }
                        break;

                    case "mousedown":
                    case "mouseup":
                        {
                            if (target is null) { result = SimulateResult.UnsupportedForControl; break; }
                            // Parse "x:y" or "x|y"; default to (0,0).
                            int mx = 0, my = 0;
                            string mv = value ?? string.Empty;
                            int sep = mv.IndexOf(':');
                            if (sep < 0) { sep = mv.IndexOf('|'); }
                            if (sep > 0)
                            {
                                int.TryParse(mv.Substring(0, sep),
                                    System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out mx);
                                int.TryParse(mv.Substring(sep + 1),
                                    System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out my);
                            }
                            var mea = new MouseEventArgs(MouseButtons.Left, 1, mx, my, 0);
                            if (action.Equals("mousedown", StringComparison.OrdinalIgnoreCase))
                            {
                                RaiseOnMouseDown(target, mea);
                            }
                            else
                            {
                                RaiseOnMouseUp(target, mea);
                            }
                        }
                        break;

                    case "keypress":
                        if (target is null) { result = SimulateResult.UnsupportedForControl; break; }
                        if (target is TextBox tbk)
                        {
                            char ch = string.IsNullOrEmpty(value) ? '\0' : value![0];
                            RaiseOnKeyPress(tbk, ch);
                        }
                        else
                        {
                            result = SimulateResult.UnsupportedForControl;
                        }
                        break;

                    default:
                        result = SimulateResult.UnknownAction;
                        break;
                }
            });
            return result;
        }

        /// <summary>
        /// Reflection-call <c>Control.OnClick(EventArgs.Empty)</c> on
        /// the given target. WinForms makes <c>OnClick</c> protected,
        /// but the click event raise path is otherwise inaccessible to
        /// us through public APIs that work on hidden controls (the
        /// public <c>PerformClick</c> on Button/Radio short-circuits on
        /// <c>!CanSelect</c>, which is always true for headless forms).
        /// </summary>
        private static readonly System.Reflection.MethodInfo? OnClickMethod =
            typeof(Control).GetMethod(
                "OnClick",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);

        private static void RaiseOnClick(Control target)
        {
            // OnClickMethod is found at type-load time. The fallback
            // path (if it ever returned null) is a no-op rather than a
            // crash, because failing to fire a synthetic click should
            // surface as a test assertion failure, not a plugin crash.
            OnClickMethod?.Invoke(target, new object[] { EventArgs.Empty });
        }

        // Same reflection pattern for Enter/Leave/DoubleClick/
        // KeyPress so synthetic events fire on hidden forms where
        // public WinForms entry points short-circuit on !CanFocus or
        // !CanSelect.
        private static readonly System.Reflection.MethodInfo? OnEnterMethod =
            typeof(Control).GetMethod(
                "OnEnter",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);

        private static readonly System.Reflection.MethodInfo? OnLeaveMethod =
            typeof(Control).GetMethod(
                "OnLeave",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);

        private static readonly System.Reflection.MethodInfo? OnDoubleClickMethod =
            typeof(Control).GetMethod(
                "OnDoubleClick",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);

        private static readonly System.Reflection.MethodInfo? OnKeyPressMethod =
            typeof(Control).GetMethod(
                "OnKeyPress",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(KeyPressEventArgs) },
                modifiers: null);

        private static void RaiseOnEnter(Control target)
        {
            OnEnterMethod?.Invoke(target, new object[] { EventArgs.Empty });
        }

        private static void RaiseOnLeave(Control target)
        {
            OnLeaveMethod?.Invoke(target, new object[] { EventArgs.Empty });
        }

        private static void RaiseOnDoubleClick(Control target)
        {
            OnDoubleClickMethod?.Invoke(target, new object[] { EventArgs.Empty });
        }

        private static void RaiseOnKeyPress(Control target, char keyChar)
        {
            OnKeyPressMethod?.Invoke(target, new object[] { new KeyPressEventArgs(keyChar) });
        }

        private static readonly System.Reflection.MethodInfo? OnMouseDownMethod =
            typeof(Control).GetMethod(
                "OnMouseDown",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(MouseEventArgs) },
                modifiers: null);

        private static readonly System.Reflection.MethodInfo? OnMouseUpMethod =
            typeof(Control).GetMethod(
                "OnMouseUp",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public,
                binder: null,
                types: new[] { typeof(MouseEventArgs) },
                modifiers: null);

        private static void RaiseOnMouseDown(Control target, MouseEventArgs e)
        {
            OnMouseDownMethod?.Invoke(target, new object[] { e });
        }

        private static void RaiseOnMouseUp(Control target, MouseEventArgs e)
        {
            OnMouseUpMethod?.Invoke(target, new object[] { e });
        }

        /// <summary>
        /// Recursive search for a control by <c>Name</c>. Case-insensitive
        /// to match the rest of the dispatch surface (FORMSET / FORMGET
        /// also lookup control ids case-insensitively).
        /// </summary>
        /// <summary>
        /// Find a ToolStripItem by name across all ToolStrip controls on a form.
        /// Searches MenuStrips, ToolStrips, ContextMenuStrips, and StatusStrips.
        /// </summary>
        private static ToolStripItem? FindToolStripItem(Form form, string name)
        {
            foreach (Control c in form.Controls)
            {
                if (c is ToolStrip ts)
                {
                    ToolStripItem? item = FindItemInStrip(ts, name);
                    if (item is not null) { return item; }
                }
            }
            // Also check Form.ContextMenuStrip
            if (form.ContextMenuStrip is not null)
            {
                ToolStripItem? item = FindItemInStrip(form.ContextMenuStrip, name);
                if (item is not null) { return item; }
            }
            return null;
        }

        private static ToolStripItem? FindItemInStrip(ToolStrip strip, string name)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
                // Recurse into dropdown menus
                if (item is ToolStripDropDownItem dropdown && dropdown.HasDropDownItems)
                {
                    ToolStripItem? nested = FindItemInStrip(dropdown.DropDown, name);
                    if (nested is not null) { return nested; }
                }
            }
            return null;
        }

        /// <summary>Find a realized control by name (recursive).</summary>
        internal static Control? FindControl(Control parent, string? name)
        {
            if (string.IsNullOrEmpty(name)) { return null; }
            return FindControlByName(parent, name!);
        }

        private static Control? FindControlByName(Control parent, string name)
        {
            foreach (Control c in parent.Controls)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
                Control? nested = FindControlByName(c, name);
                if (nested is not null) { return nested; }
            }
            return null;
        }

        /// <summary>
        /// The forced-shutdown sentinel policy that the realizer wires
        /// onto every <c>FormClosing</c> event. When the host's
        /// <see cref="GuiHostThread.ForcedShutdown"/> flag is set, any
        /// user-set <c>e.Cancel = true</c> from a prior handler in the
        /// invocation list is cleared, guaranteeing the close goes
        /// through. When the flag is not set, the policy is a no-op
        /// and user cancellation behaves normally.
        /// </summary>
        /// <remarks>
        /// Exposed as <c>internal</c> so xUnit can verify the policy
        /// directly without needing to push a real WM_CLOSE through a
        /// visible window message loop.
        /// </remarks>
        internal static void ApplyForcedShutdownPolicy(GuiHostThread host, FormClosingEventArgs e)
        {
            if (host is null) { throw new ArgumentNullException(nameof(host)); }
            if (e is null) { throw new ArgumentNullException(nameof(e)); }
            if (host.ForcedShutdown)
            {
                e.Cancel = false;
            }
        }

        // -----------------------------------------------------------------
        // Internal: must be called from the GUI thread
        // -----------------------------------------------------------------

        private static Form BuildOnGuiThread(
            FormDescriptor descriptor,
            GuiHostThread host,
            int formHandle,
            FormEventQueue? eventQueue)
        {
            string title = string.IsNullOrEmpty(descriptor.Title)
                ? descriptor.Name
                : descriptor.Title;

            var form = new Form
            {
                Text = title,
                Name = descriptor.Name,
                StartPosition = FormStartPosition.Manual,
                ClientSize = ClampSize(descriptor.Width, descriptor.Height),
                Location = new Point(descriptor.X, descriptor.Y),
                Visible = false,
                // ShowInTaskbar defaults to false: most FormCast
                // usage is utility dialogs under TCC, not
                // standalone apps. Scripts that want the app-window
                // pattern opt in with showintaskbar=1 before
                // @FORMSHOW.
                ShowInTaskbar = descriptor.Properties.TryGetValue("showintaskbar", out string? sit) &&
                    ParseBoolFlag(sit),
            };

            // Form font from the "font" prop. Format: "family:size"
            // or "family:size:style". WinForms inherits the form's
            // font to every child control, so setting it once here
            // affects the entire form.
            descriptor.Properties.TryGetValue("font", out string? fontSpec);
            if (!string.IsNullOrEmpty(fontSpec))
            {
                string[] fontParts = fontSpec!.Split(':');
                string family = fontParts.Length >= 1 ? fontParts[0] : "Segoe UI";
                float size = 9f;
                if (fontParts.Length >= 2)
                {
                    float.TryParse(fontParts[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out size);
                }
                System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular;
                if (fontParts.Length >= 3)
                {
                    string s = fontParts[2].ToLowerInvariant();
                    if (s.Contains("bold")) { style |= System.Drawing.FontStyle.Bold; }
                    if (s.Contains("italic")) { style |= System.Drawing.FontStyle.Italic; }
                }
                try { form.Font = new System.Drawing.Font(family, size, style); }
                catch { /* keep default if font not found */ }
            }

            // App icon from the "icon" prop. Loaded from a
            // .ico file path on disk. If the file doesn't exist or
            // isn't a valid icon, the default WinForms icon is used.
            descriptor.Properties.TryGetValue("icon", out string? iconPath);
            Internal.PluginLogger.Debug($"Icon prop: [{iconPath ?? "(null)"}] exists={(!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))}");
            if (!string.IsNullOrEmpty(iconPath) &&
                System.IO.File.Exists(iconPath))
            {
                try
                {
                    form.Icon = new System.Drawing.Icon(iconPath!);
                    Internal.PluginLogger.Debug($"Icon loaded: {iconPath}");
                }
                catch (Exception ex)
                {
                    Internal.PluginLogger.Warn($"Icon load failed: {iconPath} - {ex.Message}");
                }
            }

            // When a form opts into the taskbar with a custom icon,
            // set the process AppUserModelID so Windows shows the
            // form's icon instead of TCC's embedded exe icon.
            if (form.ShowInTaskbar && form.Icon is not null)
            {
                try
                {
                    Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID(
                        "FormCast." + (descriptor.Name ?? "App"));
                    Internal.PluginLogger.Debug("AppUserModelID set for taskbar icon");
                }
                catch (Exception ex)
                {
                    Internal.PluginLogger.Warn($"AppUserModelID failed: {ex.Message}");
                }
            }

            // FormClosing handler with two modes:
            //
            // 1. Default (confirmclose not set): the forced-shutdown
            //    policy runs (e.Cancel = false) and a "close" event
            //    is enqueued for FORMEVENTS.
            //
            // 2. confirmclose=1: the close is CANCELLED (e.Cancel =
            //    true) and a "closing" event is enqueued instead.
            //    The BTM sees "closing", prompts the user, and calls
            //    @FORMCLOSE explicitly if confirmed. This prevents
            //    WinForms from destroying the form before the BTM
            //    can decide.
            {
                FormDescriptor desc = descriptor;
                int fh = formHandle;
                form.FormClosing += (sender, e) =>
                {
                    if (desc.Properties.TryGetValue("confirmclose", out string? cc) &&
                        (cc == "1" || string.Equals(cc, "true", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Cancel the close -- let the BTM decide
                        e.Cancel = true;
                        eventQueue?.Enqueue(new FormEvent(fh, string.Empty, "closing", string.Empty));
                    }
                    else
                    {
                        // Default: allow close
                        ApplyForcedShutdownPolicy(host, e);
                        eventQueue?.Enqueue(new FormEvent(fh, string.Empty, "close", string.Empty));
                    }
                };
            }

            System.Drawing.Font? formFont =
                form.Font.Size > 8.25f ? form.Font : null;

            foreach (ControlDescriptor controlDesc in descriptor.Controls)
            {
                Control? control = RealizeControl(
                    controlDesc, formHandle, eventQueue, formFont);
                if (control is not null)
                {
                    if (control is ContextMenuStrip cms2)
                    {
                        // ContextMenuStrip disrupts form layout
                        // when added to Controls (auto-docks as
                        // ToolStrip). Store separately and attach
                        // as Form.ContextMenuStrip so it's
                        // accessible without being in Controls.
                        form.ContextMenuStrip = cms2;
                    }
                    else
                    {
                        form.Controls.Add(control);
                    }
                }
            }

            // Force the form font onto ToolStrip-family controls
            // (MenuStrip, ToolStrip, StatusStrip) which have their
            // own default font and ignore Form.Font inheritance.
            // This runs AFTER controls are parented so the font
            // sticks. TabControl headers are handled via owner-
            // draw in the TABCONTROL creation case instead.
            // .NET Framework 4.8 does not have
            // Application.SetDefaultFont, so Form.Font
            // inheritance is unreliable. Walk every control
            // and set the font explicitly after the tree is
            // fully built.
            if (formFont is not null)
            {
                ApplyFontRecursive(form, formFont);
            }

            // AcceptButton / CancelButton: wire Enter and Escape
            // keys to named buttons on the form.
            if (descriptor.Properties.TryGetValue("acceptbutton", out string? abId)
                && !string.IsNullOrEmpty(abId))
            {
                Control? ab = FindControlByName(form, abId!);
                if (ab is IButtonControl abc) { form.AcceptButton = abc; }
            }
            if (descriptor.Properties.TryGetValue("cancelbutton", out string? cbId)
                && !string.IsNullOrEmpty(cbId))
            {
                Control? cb = FindControlByName(form, cbId!);
                if (cb is IButtonControl cbc) { form.CancelButton = cbc; }
            }

            // Theme: apply a color scheme to the form and all
            // controls. Supported values: "system" (default,
            // no changes), "dark", "light". The legacy
            // "darkmode=1" prop is an alias for theme=dark.
            string theme = "system";
            if (descriptor.Properties.TryGetValue("theme", out string? themeVal)
                && !string.IsNullOrEmpty(themeVal))
            {
                theme = themeVal!.Trim().ToLowerInvariant();
            }
            else if (descriptor.Properties.TryGetValue("darkmode", out string? dmMode)
                && ParseBoolFlag(dmMode))
            {
                theme = "dark";
            }
            if (theme == "dark")
            {
                ApplyThemeColors(form, DarkBg, DarkSurface, DarkFg, true);
            }
            else if (theme == "light")
            {
                ApplyThemeColors(form, LightBg, LightSurface, LightFg, false);
            }

            // Re-apply individual backcolor/forecolor from each
            // control's descriptor props so they override the
            // theme. Walk the descriptor tree and push explicit
            // colors onto the already-realized controls.
            if (theme != "system")
            {
                ReapplyExplicitColors(form, descriptor.Controls);
            }

            // If design_mode is "1" on the descriptor, attach
            // the interactive drag handler so users can click-to-select
            // and drag-to-move controls on the realized form. The
            // handler runs entirely on the GUI thread and commits
            // final positions back to the descriptor on mouseup.
            if (descriptor.Properties.TryGetValue("design_mode", out string? dm)
                && (dm == "1" || string.Equals(dm, "true", StringComparison.OrdinalIgnoreCase)))
            {
                var handler = new Controls.DesignModeHandler(form, descriptor);
                handler.Attach();
                // Store handler so DeleteControl can clear
                // the selection overlay when a control is removed.
                _designHandlers[formHandle] = handler;
            }

            return form;
        }

        /// <summary>
        /// Recursively set <paramref name="font"/> on every control
        /// in the tree. .NET Framework 4.8 lacks
        /// <c>Application.SetDefaultFont</c>, so <c>Form.Font</c>
        /// inheritance is unreliable for ToolStrip-family controls
        /// and TabControl. This brute-force walk ensures every
        /// control, menu item, and tab page uses the requested font.
        /// </summary>
        private static void ApplyFontRecursive(Control parent, System.Drawing.Font font)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is ToolStrip ts)
                {
                    ts.Font = font;
                    foreach (ToolStripItem item in ts.Items)
                    {
                        item.Font = font;
                    }
                }
                else
                {
                    c.Font = font;
                }
                // Recurse into children (panels, groupboxes,
                // tab pages, split panels, etc.)
                if (c.Controls.Count > 0)
                {
                    ApplyFontRecursive(c, font);
                }
            }
            // TabControl.TabPages are not in Controls for
            // iteration purposes on some .NET builds; handle
            // them explicitly.
            if (parent is TabControl tc)
            {
                foreach (TabPage tp in tc.TabPages)
                {
                    tp.Font = font;
                    ApplyFontRecursive(tp, font);
                }
            }
        }

        // -- Theme color palettes --
        private static readonly System.Drawing.Color DarkBg =
            System.Drawing.Color.FromArgb(32, 32, 32);
        private static readonly System.Drawing.Color DarkSurface =
            System.Drawing.Color.FromArgb(45, 45, 45);
        private static readonly System.Drawing.Color DarkFg =
            System.Drawing.Color.FromArgb(230, 230, 230);

        private static readonly System.Drawing.Color LightBg =
            System.Drawing.Color.FromArgb(243, 243, 243);
        private static readonly System.Drawing.Color LightSurface =
            System.Drawing.Color.White;
        private static readonly System.Drawing.Color LightFg =
            System.Drawing.Color.FromArgb(26, 26, 26);

        /// <summary>
        /// Apply a color scheme to the form and all child controls.
        /// <paramref name="useDarkTitleBar"/> triggers the DWM
        /// immersive dark mode attribute on the title bar.
        /// </summary>
        private static void ApplyThemeColors(
            Form form,
            System.Drawing.Color bg,
            System.Drawing.Color surface,
            System.Drawing.Color fg,
            bool useDarkTitleBar)
        {
            if (useDarkTitleBar)
            {
                form.HandleCreated += (s, e) =>
                {
                    try
                    {
                        int value = 1;
                        int hr = Interop.NativeMethods.DwmSetWindowAttribute(
                            form.Handle,
                            Interop.NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                            ref value, sizeof(int));
                        _ = hr;
                    }
                    catch { /* pre-1809 Windows */ }
                };
            }

            form.BackColor = bg;
            form.ForeColor = fg;
            ApplyThemeRecursive(form, bg, surface, fg);
        }

        private static void ApplyThemeRecursive(
            Control parent,
            System.Drawing.Color bg,
            System.Drawing.Color surface,
            System.Drawing.Color fg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is ToolStrip ts)
                {
                    ts.BackColor = surface;
                    ts.ForeColor = fg;
                    ts.RenderMode = ToolStripRenderMode.Professional;
                    foreach (ToolStripItem item in ts.Items)
                    {
                        item.BackColor = surface;
                        item.ForeColor = fg;
                    }
                }
                else if (c is DataGridView dgv)
                {
                    dgv.BackgroundColor = surface;
                    dgv.ForeColor = fg;
                    dgv.GridColor = bg;
                    dgv.DefaultCellStyle.BackColor = surface;
                    dgv.DefaultCellStyle.ForeColor = fg;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = bg;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = fg;
                    dgv.RowHeadersDefaultCellStyle.BackColor = bg;
                    dgv.RowHeadersDefaultCellStyle.ForeColor = fg;
                    dgv.EnableHeadersVisualStyles = false;
                }
                else if (c is System.Windows.Forms.Integration.ElementHost eh
                    && eh.Child is System.Windows.Controls.RichTextBox wpfRtb)
                {
                    // WPF RichTextBox inside ElementHost (RICHMEMO)
                    wpfRtb.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(surface.R, surface.G, surface.B));
                    wpfRtb.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(fg.R, fg.G, fg.B));
                }
                else if (c is TextBoxBase tb)
                {
                    tb.BackColor = surface;
                    tb.ForeColor = fg;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is LinkLabel ll)
                {
                    ll.BackColor = bg;
                    ll.ForeColor = fg;
                    ll.LinkColor = System.Drawing.Color.FromArgb(100, 180, 255);
                    ll.VisitedLinkColor = System.Drawing.Color.FromArgb(180, 140, 255);
                    ll.ActiveLinkColor = System.Drawing.Color.FromArgb(140, 210, 255);
                }
                else if (c is ScrollBar sb)
                {
                    sb.BackColor = surface;
                    sb.ForeColor = fg;
                }
                else if (c is ListControl || c is ListView || c is TreeView)
                {
                    c.BackColor = surface;
                    c.ForeColor = fg;
                }
                else if (c is TabControl tc)
                {
                    tc.BackColor = bg;
                    tc.ForeColor = fg;
                    foreach (TabPage tp in tc.TabPages)
                    {
                        tp.BackColor = bg;
                        tp.ForeColor = fg;
                        ApplyThemeRecursive(tp, bg, surface, fg);
                    }
                    // Force repaint of owner-drawn tab headers
                    tc.Invalidate();
                }
                else if (c is Panel)
                {
                    c.BackColor = surface;
                    c.ForeColor = fg;
                }
                else
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }

                if (c.Controls.Count > 0 && c is not TabControl)
                {
                    ApplyThemeRecursive(c, bg, surface, fg);
                }
            }
        }

        /// <summary>
        /// After a theme has been applied, re-apply any explicit
        /// backcolor/forecolor from each control's descriptor so
        /// individual overrides win over the theme defaults.
        /// </summary>
        private static void ReapplyExplicitColors(
            Form form, System.Collections.Generic.List<ControlDescriptor> descriptors)
        {
            foreach (ControlDescriptor desc in descriptors)
            {
                string? id = desc.Id;
                if (string.IsNullOrEmpty(id)) { continue; }
                bool hasBg = desc.Properties.TryGetValue("backcolor", out string? bgSpec)
                    && !string.IsNullOrEmpty(bgSpec);
                bool hasFg = desc.Properties.TryGetValue("forecolor", out string? fgSpec)
                    && !string.IsNullOrEmpty(fgSpec);
                if (hasBg || hasFg)
                {
                    Control? target = FindControlByName(form, id!);
                    if (target is not null)
                    {
                        if (hasBg)
                        {
                            var c = ParseColor(bgSpec!);
                            if (c.HasValue)
                            {
                                target.BackColor = c.Value;
                                if (target is ToolStrip ts2)
                                {
                                    ts2.RenderMode = ToolStripRenderMode.Professional;
                                }
                            }
                        }
                        if (hasFg)
                        {
                            var c = ParseColor(fgSpec!);
                            if (c.HasValue) { target.ForeColor = c.Value; }
                        }
                    }
                }
                // Recurse into children
                if (desc.Children.Count > 0)
                {
                    ReapplyExplicitColors(form, desc.Children);
                }
            }
        }

        /// <summary>
        /// Public entry point for live-adding a single
        /// control to an already-realized form. Wraps the private
        /// RealizeControl with a GuiHostThread.Invoke so the
        /// caller doesn't have to marshal manually.
        /// </summary>
        public static Control? RealizeOneControl(
            ControlDescriptor desc,
            Threading.GuiHostThread host,
            int formHandle,
            FormEventQueue? eventQueue)
        {
            if (desc is null || host is null) { return null; }
            Control? result = null;
            host.Invoke(() =>
            {
                result = RealizeControl(desc, formHandle, eventQueue);
            });
            return result;
        }

        /// <summary>
        /// Map a single <see cref="ControlDescriptor"/> to a WinForms
        /// <see cref="Control"/>. The switch dispatches on the uppercase
        /// type token and builds the appropriate control with its initial
        /// state from the descriptor's fields and property bag. Container
        /// types (PANEL, GROUPBOX, TABCONTROL, SPLITCONTAINER, etc.)
        /// recurse into their children. Returns <c>null</c> for types
        /// that can only appear as children (TABPAGE at top level).
        /// </summary>
        /// <remarks>
        /// Must be called on the GUI thread. Event handlers that push
        /// into <paramref name="eventQueue"/> are wired here so every
        /// realized control participates in the FORMEVENTS / @FORMBIND
        /// dispatch pipeline.
        /// </remarks>
        private static Control? RealizeControl(
            ControlDescriptor desc,
            int formHandle,
            FormEventQueue? eventQueue,
            System.Drawing.Font? formFont = null)
        {
            string type = desc.Type?.ToUpperInvariant() ?? string.Empty;
            Control control;
            switch (type)
            {
                case "LABEL":
                    control = new Label { Text = desc.Text };
                    break;
                case "EDIT":
                    control = new TextBox { Text = desc.Text };
                    break;
                case "BUTTON":
                    control = new Button { Text = desc.Text };
                    break;
                case "CHECKBOX":
                    {
                        var cb = new CheckBox { Text = desc.Text };
                        string? chk = GetProp(desc, "checked");
                        if (ParseBoolFlag(chk))
                        {
                            cb.Checked = true;
                        }
                        control = cb;
                    }
                    break;
                case "TOGGLE":
                    {
                        var toggle = new Controls.ToggleSwitch();
                        string? chk = GetProp(desc, "checked");
                        if (ParseBoolFlag(chk))
                        {
                            toggle.Checked = true;
                        }
                        control = toggle;
                    }
                    break;
                case "RADIO":
                    {
                        var rb = new RadioButton { Text = desc.Text };
                        string? chk = GetProp(desc, "checked");
                        if (ParseBoolFlag(chk))
                        {
                            rb.Checked = true;
                        }
                        control = rb;
                    }
                    break;
                case "PANEL":
                    {
                        var panel = new Panel();
                        string? autoScroll = GetProp(desc, "autoscroll");
                        if (ParseBoolFlag(autoScroll))
                        {
                            panel.AutoScroll = true;
                        }
                        string? borderProp = GetProp(desc, "border");
                        if (string.Equals(borderProp, "single", StringComparison.OrdinalIgnoreCase))
                        {
                            panel.BorderStyle = BorderStyle.FixedSingle;
                        }
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            Control? child = RealizeControl(childDesc, formHandle, eventQueue);
                            if (child is not null)
                            {
                                panel.Controls.Add(child);
                            }
                        }
                        control = panel;
                    }
                    break;
                case "LISTVIEW":
                    control = BuildListView(desc);
                    break;
                case "RICHMEMO":
                    // WPF RichTextBox in an ElementHost. The
                    // build helper handles the WPF tree construction
                    // AND wires the forced-shutdown dispose-order
                    // safety net (HandleDestroyed -> Child = null).
                    control = Controls.RichMemoBuilder.Build(desc);
                    break;
                case "GROUPBOX":
                    {
                        // GroupBox is a container like PANEL
                        // but with a titled border. Critical for
                        // radio button grouping: WinForms only
                        // allows one selected RadioButton per parent
                        // container, so two groups of radios need
                        // two GroupBoxes.
                        var gb = new GroupBox { Text = desc.Text };
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            Control? child = RealizeControl(childDesc, formHandle, eventQueue);
                            if (child is not null)
                            {
                                gb.Controls.Add(child);
                            }
                        }
                        control = gb;
                    }
                    break;
                case "COMBOBOX":
                    {
                        // Dropdown selection. Items come from
                        // the prop bag via additem (same pattern as
                        // LISTVIEW). DropDownStyle defaults to
                        // DropDown (editable); set style=list for
                        // non-editable.
                        var cb = new ComboBox { Text = desc.Text };
                        string cbStyle = (GetProp(desc, "style") ?? "dropdown").Trim().ToLowerInvariant();
                        cb.DropDownStyle = cbStyle switch
                        {
                            "list" => ComboBoxStyle.DropDownList,
                            "simple" => ComboBoxStyle.Simple,
                            _ => ComboBoxStyle.DropDown,
                        };
                        // Walk _cb.item.N entries (same dense-numbering
                        // pattern as LISTVIEW's _lv.item.N).
                        for (int i = 0; ; i++)
                        {
                            string key = "_cb.item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? item)) { break; }
                            cb.Items.Add(item);
                        }
                        // Pre-select if a selectedindex prop is set.
                        string? selIdx = GetProp(desc, "selectedindex");
                        if (selIdx is not null && int.TryParse(selIdx,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int si)
                            && si >= 0 && si < cb.Items.Count)
                        {
                            cb.SelectedIndex = si;
                        }
                        control = cb;
                    }
                    break;
                case "TABCONTROL":
                    {
                        // TabControl. Each child must be a
                        // TABPAGE descriptor; non-TABPAGE children
                        // are skipped. Tab pages are added in
                        // descriptor order.
                        var tc = new TabControl();
                        if (formFont is not null)
                        {
                            tc.Font = formFont;
                            // The Windows theme engine ignores
                            // TabControl.Font for header rendering.
                            // Owner-draw bypasses the theme and draws
                            // headers with the correct font.
                            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
                            tc.SizeMode = TabSizeMode.Fixed;
                            // Measure each tab label with the custom font
                            // and set ItemSize to the widest + generous
                            // padding to prevent text clipping.
                            int maxW = 0;
                            foreach (ControlDescriptor cd in desc.Children)
                            {
                                if (string.Equals(cd.Type, "TABPAGE",
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    var sz = System.Windows.Forms.TextRenderer.MeasureText(
                                        cd.Text ?? string.Empty, formFont);
                                    if (sz.Width > maxW) { maxW = sz.Width; }
                                }
                            }
                            int headerH = (int)(formFont.GetHeight() + 10);
                            tc.ItemSize = new System.Drawing.Size(
                                maxW + 32, headerH);
                            // Paint handler: draw text centered in
                            // each tab header using the form font.
                            System.Drawing.Font drawFont = formFont;
                            tc.DrawItem += (s, e) =>
                            {
                                // Use the TabControl's own colors so dark/light
                                // theme changes are reflected in the tab headers.
                                Color bgColor;
                                if (e.Index == tc.SelectedIndex)
                                {
                                    bgColor = tc.TabPages[e.Index].BackColor;
                                }
                                else
                                {
                                    Color baseBg = tc.BackColor;
                                    int shift = baseBg.GetBrightness() < 0.5f ? 30 : -15;
                                    bgColor = Color.FromArgb(
                                        Math.Max(0, Math.Min(255, baseBg.R + shift)),
                                        Math.Max(0, Math.Min(255, baseBg.G + shift)),
                                        Math.Max(0, Math.Min(255, baseBg.B + shift)));
                                }
                                // Ensure text contrasts with background
                                Color fgColor = bgColor.GetBrightness() < 0.5f
                                    ? Color.FromArgb(220, 220, 220)
                                    : Color.FromArgb(30, 30, 30);
                                using (var bg2 = new SolidBrush(bgColor))
                                {
                                    e.Graphics.FillRectangle(bg2, e.Bounds);
                                }
                                string text = tc.TabPages[e.Index].Text;
                                using (var brush = new SolidBrush(fgColor))
                                {
                                    e.Graphics.DrawString(text, drawFont, brush,
                                        e.Bounds.X + 6, e.Bounds.Y + 4);
                                }
                            };
                        }
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            if (!string.Equals(childDesc.Type, "TABPAGE",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            var tp = new TabPage(childDesc.Text)
                            {
                                Name = childDesc.Id ?? string.Empty,
                            };
                            foreach (ControlDescriptor grandchild in childDesc.Children)
                            {
                                Control? gc = RealizeControl(
                                    grandchild, formHandle, eventQueue, formFont);
                                if (gc is not null) { tp.Controls.Add(gc); }
                            }
                            tc.TabPages.Add(tp);
                        }
                        control = tc;
                    }
                    break;
                case "TABPAGE":
                    // TABPAGE is only valid as a child of TABCONTROL;
                    // if it appears at the top level, skip it.
                    return null;
                case "NUMERICUPDOWN":
                    {
                        // Numeric spinner.
                        var nud = new NumericUpDown
                        {
                            Minimum = ParseIntOrDefault(GetProp(desc, "min") ?? "0", 0),
                            Maximum = ParseIntOrDefault(GetProp(desc, "max") ?? "100", 100),
                            Value = ParseIntOrDefault(GetProp(desc, "value") ?? "0", 0),
                            DecimalPlaces = ParseIntOrDefault(GetProp(desc, "decimals") ?? "0", 0),
                        };
                        // Clamp value to min/max range.
                        if (nud.Value < nud.Minimum) { nud.Value = nud.Minimum; }
                        if (nud.Value > nud.Maximum) { nud.Value = nud.Maximum; }
                        control = nud;
                    }
                    break;
                case "DATETIMEPICKER":
                    {
                        // Date/time selector.
                        var dtp = new DateTimePicker();
                        string dtpFormat = (GetProp(desc, "format") ?? "long").Trim().ToLowerInvariant();
                        dtp.Format = dtpFormat switch
                        {
                            "short" => DateTimePickerFormat.Short,
                            "time" => DateTimePickerFormat.Time,
                            "custom" => DateTimePickerFormat.Custom,
                            _ => DateTimePickerFormat.Long,
                        };
                        string? customFmt = GetProp(desc, "customformat");
                        if (customFmt is not null) { dtp.CustomFormat = customFmt; }
                        string? dtpValue = GetProp(desc, "value");
                        if (dtpValue is not null &&
                            DateTime.TryParse(dtpValue, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out DateTime dtVal))
                        {
                            dtp.Value = dtVal;
                        }
                        control = dtp;
                    }
                    break;
                case "LINKLABEL":
                    {
                        var ll = new LinkLabel { Text = desc.Text };
                        string? url = GetProp(desc, "url");
                        if (!string.IsNullOrEmpty(url))
                        {
                            ll.LinkClicked += (s, e) =>
                            {
                                try { System.Diagnostics.Process.Start(url!); }
                                catch { /* swallow: bad URL or no browser */ }
                            };
                        }
                        control = ll;
                    }
                    break;
                case "PICTUREBOX":
                    {
                        var pb = new PictureBox
                        {
                            SizeMode = PictureBoxSizeMode.Zoom,
                            BorderStyle = BorderStyle.FixedSingle,
                        };
                        string? imagePath = GetProp(desc, "image");
                        if (!string.IsNullOrEmpty(imagePath) &&
                            System.IO.File.Exists(imagePath))
                        {
                            try { pb.Image = System.Drawing.Image.FromFile(imagePath!); }
                            catch { /* swallow: bad image file */ }
                        }
                        control = pb;
                    }
                    break;
                case "TRACKBAR":
                    {
                        var tb = new TrackBar
                        {
                            Minimum = ParseIntOrDefault(GetProp(desc, "min") ?? "0", 0),
                            Maximum = ParseIntOrDefault(GetProp(desc, "max") ?? "100", 100),
                            TickFrequency = ParseIntOrDefault(GetProp(desc, "tickfrequency") ?? "10", 10),
                        };
                        int tv = ParseIntOrDefault(GetProp(desc, "value") ?? "0", 0);
                        if (tv < tb.Minimum) { tv = tb.Minimum; }
                        if (tv > tb.Maximum) { tv = tb.Maximum; }
                        tb.Value = tv;
                        string orient = (GetProp(desc, "orientation") ?? "horizontal").Trim().ToLowerInvariant();
                        if (orient == "vertical") { tb.Orientation = Orientation.Vertical; }
                        control = tb;
                    }
                    break;
                case "LISTBOX":
                    {
                        var lb = new ListBox();
                        for (int i = 0; ; i++)
                        {
                            string key = "_lb.item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? item)) { break; }
                            lb.Items.Add(item);
                        }
                        control = lb;
                    }
                    break;
                case "CHECKEDLISTBOX":
                    {
                        var clb = new CheckedListBox();
                        for (int i = 0; ; i++)
                        {
                            string key = "_clb.item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? item)) { break; }
                            clb.Items.Add(item);
                        }
                        control = clb;
                    }
                    break;
                case "MASKEDTEXTBOX":
                    {
                        var mtb = new MaskedTextBox
                        {
                            Text = desc.Text,
                            Mask = GetProp(desc, "mask") ?? string.Empty,
                        };
                        control = mtb;
                    }
                    break;
                case "MONTHCALENDAR":
                    {
                        control = new MonthCalendar();
                    }
                    break;
                case "TREEVIEW":
                    {
                        var tv = new TreeView();
                        // Tree nodes are stored as _tv.node.N with
                        // value = "path|text" where path is a
                        // slash-separated parent chain:
                        //   "root" -> top-level node "root"
                        //   "root/child" -> child of "root"
                        for (int i = 0; ; i++)
                        {
                            string key = "_tv.node." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? spec)) { break; }
                            string[] parts = SplitLvSpec(spec);
                            if (parts.Length == 0) { continue; }
                            string path = parts[0];
                            string text = parts.Length >= 2 ? parts[1] : path;
                            AddTreeNode(tv, path, text);
                        }
                        tv.ExpandAll();
                        control = tv;
                    }
                    break;
                case "SPLITCONTAINER":
                    {
                        var sc = new SplitContainer
                        {
                            Orientation = string.Equals(
                                (GetProp(desc, "orientation") ?? "vertical").Trim(),
                                "horizontal", StringComparison.OrdinalIgnoreCase)
                                ? Orientation.Horizontal
                                : Orientation.Vertical,
                            SplitterDistance = ParseIntOrDefault(GetProp(desc, "splitterdistance") ?? "100", 100),
                            BorderStyle = BorderStyle.Fixed3D,
                        };
                        // Children go into Panel1 and Panel2.
                        // First child -> Panel1, second -> Panel2.
                        int panelIdx = 0;
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            Control? child = RealizeControl(childDesc, formHandle, eventQueue);
                            if (child is null) { continue; }
                            if (panelIdx == 0) { sc.Panel1.Controls.Add(child); }
                            else if (panelIdx == 1) { sc.Panel2.Controls.Add(child); }
                            panelIdx++;
                        }
                        control = sc;
                    }
                    break;
                case "MENUSTRIP":
                    {
                        // MenuStrip with ToolStripMenuItem
                        // children built from the descriptor's
                        // Children list. Each child becomes a
                        // top-level menu (File, Edit, etc.); each
                        // grandchild becomes a menu item. The text
                        // "-" creates a separator.
                        var ms = new MenuStrip();
                        BuildMenuItems(ms.Items, desc.Children, formHandle, eventQueue);
                        control = ms;
                    }
                    break;
                case "CONTEXTMENU":
                    {
                        // ContextMenuStrip is a popup component.
                        // Set Name and build items, then return
                        // directly -- the common sizing/anchor code
                        // below doesn't apply. BuildOnGuiThread
                        // adds it to form.Controls with Dock=None
                        // so FindRealizedControl can locate it by
                        // name for runtimecontextmenu lookups.
                        var cms = new ContextMenuStrip
                        {
                            Name = desc.Id ?? string.Empty,
                            Dock = DockStyle.None,
                        };
                        BuildMenuItems(cms.Items, desc.Children, formHandle, eventQueue);
                        return cms;
                    }
                case "TOOLBAR":
                    {
                        // ToolStrip with typed children.
                        // Children with type BUTTON become
                        // ToolStripButtons; text "-" becomes a
                        // separator; COMBOBOX becomes
                        // ToolStripComboBox; LABEL becomes
                        // ToolStripLabel.
                        var ts = new Controls.ClickThroughToolStrip();
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            string childType = (childDesc.Type ?? string.Empty).ToUpperInvariant();
                            switch (childType)
                            {
                                case "BUTTON":
                                    var tsBtn = new ToolStripButton(childDesc.Text)
                                    {
                                        Name = childDesc.Id ?? string.Empty,
                                    };
                                    // Stock icon from descriptor prop
                                    string? stockIcon = GetProp(childDesc, "stockicon");
                                    if (!string.IsNullOrEmpty(stockIcon))
                                    {
                                        tsBtn.Image = Controls.StockIcons.Get(stockIcon!);
                                        tsBtn.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                                        tsBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
                                    }
                                    if (eventQueue is not null)
                                    {
                                        string btnId = tsBtn.Name;
                                        tsBtn.Click += (s, e) => eventQueue.Enqueue(
                                            new FormEvent(formHandle, btnId, "click", string.Empty));
                                    }
                                    ts.Items.Add(tsBtn);
                                    break;
                                case "LABEL":
                                    ts.Items.Add(new ToolStripLabel(childDesc.Text)
                                    {
                                        Name = childDesc.Id ?? string.Empty,
                                    });
                                    break;
                                default:
                                    if (childDesc.Text == "-")
                                    {
                                        ts.Items.Add(new ToolStripSeparator());
                                    }
                                    else
                                    {
                                        ts.Items.Add(new ToolStripButton(childDesc.Text)
                                        {
                                            Name = childDesc.Id ?? string.Empty,
                                        });
                                    }
                                    break;
                            }
                        }
                        control = ts;
                    }
                    break;
                case "STATUSBAR":
                    {
                        // StatusStrip with
                        // ToolStripStatusLabel panels. Each child
                        // descriptor becomes a status panel.
                        var ss = new StatusStrip();
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            var label = new ToolStripStatusLabel(childDesc.Text)
                            {
                                Name = childDesc.Id ?? string.Empty,
                            };
                            string spring = GetProp(childDesc, "spring") ?? "0";
                            if (spring == "1" || string.Equals(spring, "true", StringComparison.OrdinalIgnoreCase))
                            {
                                label.Spring = true;
                            }
                            ss.Items.Add(label);
                        }
                        control = ss;
                    }
                    break;
                case "DOMAINUPDOWN":
                    {
                        var dud = new DomainUpDown { Text = desc.Text };
                        for (int i = 0; ; i++)
                        {
                            string key = "_dud.item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? item)) { break; }
                            dud.Items.Add(item);
                        }
                        control = dud;
                    }
                    break;
                case "HSCROLLBAR":
                    {
                        control = new HScrollBar
                        {
                            Minimum = ParseIntOrDefault(GetProp(desc, "min") ?? "0", 0),
                            Maximum = ParseIntOrDefault(GetProp(desc, "max") ?? "100", 100),
                            Value = ParseIntOrDefault(GetProp(desc, "value") ?? "0", 0),
                        };
                    }
                    break;
                case "VSCROLLBAR":
                    {
                        control = new VScrollBar
                        {
                            Minimum = ParseIntOrDefault(GetProp(desc, "min") ?? "0", 0),
                            Maximum = ParseIntOrDefault(GetProp(desc, "max") ?? "100", 100),
                            Value = ParseIntOrDefault(GetProp(desc, "value") ?? "0", 0),
                        };
                    }
                    break;
                case "DATAGRID":
                    {
                        // DataGridView. Columns from
                        // _dg.col.N = "name:width:type"; rows from
                        // _dg.row.N = "cell0:cell1:...". Column
                        // type defaults to TextBoxColumn.
                        var dg = new DataGridView
                        {
                            AllowUserToAddRows = ParseBoolFlag(GetProp(desc, "allowaddrows")),
                            AllowUserToDeleteRows = ParseBoolFlag(GetProp(desc, "allowdeleterows")),
                            ReadOnly = ParseBoolFlag(GetProp(desc, "readonly")),
                            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                        };
                        for (int i = 0; ; i++)
                        {
                            string key = "_dg.col." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? colSpec)) { break; }
                            string[] parts = SplitLvSpec(colSpec);
                            string colName = parts.Length >= 1 ? parts[0] : "Col" + i;
                            int colWidth = parts.Length >= 2 ? ParseIntOrDefault(parts[1], 100) : 100;
                            string colType = parts.Length >= 3 ? parts[2].Trim().ToLowerInvariant() : "text";
                            DataGridViewColumn col = colType switch
                            {
                                "checkbox" => new DataGridViewCheckBoxColumn { HeaderText = colName, Width = colWidth },
                                "combobox" => new DataGridViewComboBoxColumn { HeaderText = colName, Width = colWidth },
                                "button" => new DataGridViewButtonColumn { HeaderText = colName, Width = colWidth },
                                "link" => new DataGridViewLinkColumn { HeaderText = colName, Width = colWidth },
                                "image" => new DataGridViewImageColumn { HeaderText = colName, Width = colWidth },
                                _ => new DataGridViewTextBoxColumn { HeaderText = colName, Width = colWidth },
                            };
                            col.Name = colName;
                            dg.Columns.Add(col);
                        }
                        for (int i = 0; ; i++)
                        {
                            string key = "_dg.row." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (!desc.Properties.TryGetValue(key, out string? rowSpec)) { break; }
                            string[] cells = SplitLvSpec(rowSpec);
                            dg.Rows.Add(cells);
                        }
                        control = dg;
                    }
                    break;
                case "FLOWPANEL":
                    {
                        var flp = new FlowLayoutPanel
                        {
                            FlowDirection = string.Equals(
                                (GetProp(desc, "direction") ?? "lefttoright").Trim(),
                                "toptobottom", StringComparison.OrdinalIgnoreCase)
                                ? System.Windows.Forms.FlowDirection.TopDown
                                : System.Windows.Forms.FlowDirection.LeftToRight,
                            WrapContents = !ParseBoolFlag(GetProp(desc, "nowrap")),
                        };
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            Control? child = RealizeControl(childDesc, formHandle, eventQueue);
                            if (child is not null) { flp.Controls.Add(child); }
                        }
                        control = flp;
                    }
                    break;
                case "TABLEPANEL":
                    {
                        int rows = ParseIntOrDefault(GetProp(desc, "rows") ?? "2", 2);
                        int cols = ParseIntOrDefault(GetProp(desc, "cols") ?? "2", 2);
                        var tlp = new TableLayoutPanel
                        {
                            RowCount = rows,
                            ColumnCount = cols,
                        };
                        // Set uniform column/row sizing.
                        for (int c = 0; c < cols; c++)
                        {
                            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
                        }
                        for (int r = 0; r < rows; r++)
                        {
                            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
                        }
                        foreach (ControlDescriptor childDesc in desc.Children)
                        {
                            Control? child = RealizeControl(childDesc, formHandle, eventQueue);
                            if (child is null) { continue; }
                            int row = ParseIntOrDefault(GetProp(childDesc, "row") ?? "0", 0);
                            int col = ParseIntOrDefault(GetProp(childDesc, "col") ?? "0", 0);
                            tlp.Controls.Add(child, col, row);
                        }
                        control = tlp;
                    }
                    break;
                case "PROPERTYGRID":
                    {
                        control = new PropertyGrid();
                    }
                    break;
                case "WEBBROWSER":
                    {
                        var wb = new WebBrowser
                        {
                            ScriptErrorsSuppressed = true,
                        };
                        string? url = GetProp(desc, "url");
                        if (!string.IsNullOrEmpty(url))
                        {
                            try { wb.Navigate(url!); }
                            catch { /* swallow: bad URL */ }
                        }
                        else if (!string.IsNullOrEmpty(desc.Text))
                        {
                            wb.DocumentText = desc.Text;
                        }
                        control = wb;
                    }
                    break;
                case "PROGRESSBAR":
                    {
                        // System.Windows.Forms.ProgressBar.
                        // Min/max/value come from the prop bag with
                        // descriptor-friendly defaults; style picks
                        // marquee vs continuous.
                        var pb = new ProgressBar
                        {
                            Minimum = ParseIntOrDefault(GetProp(desc, "min") ?? "0", 0),
                            Maximum = ParseIntOrDefault(GetProp(desc, "max") ?? "100", 100),
                        };
                        int v = ParseIntOrDefault(GetProp(desc, "value") ?? "0", 0);
                        if (v < pb.Minimum) { v = pb.Minimum; }
                        if (v > pb.Maximum) { v = pb.Maximum; }
                        pb.Value = v;
                        string style = (GetProp(desc, "style") ?? "blocks").Trim().ToLowerInvariant();
                        switch (style)
                        {
                            case "marquee":
                                pb.Style = ProgressBarStyle.Marquee;
                                break;
                            case "continuous":
                                pb.Style = ProgressBarStyle.Continuous;
                                break;
                            default:
                                pb.Style = ProgressBarStyle.Blocks;
                                break;
                        }
                        control = pb;
                    }
                    break;
                case "MEMO":
                    // Multiline TextBox. ReadOnly and WordWrap
                    // come from the descriptor's prop bag (defaults:
                    // editable, word-wrap on). ScrollBars defaults to
                    // Vertical so long content has somewhere to go;
                    // setting wordwrap=0 widens the typical use case
                    // and we switch to Both scrollbars in that case.
                    {
                        bool readOnly = ParseBoolFlag(GetProp(desc, "readonly"));
                        bool wordWrap = !ParseBoolFlag(GetProp(desc, "nowrap"));
                        var memo = new TextBox
                        {
                            Multiline = true,
                            Text = desc.Text,
                            ReadOnly = readOnly,
                            WordWrap = wordWrap,
                            ScrollBars = wordWrap ? ScrollBars.Vertical : ScrollBars.Both,
                            AcceptsReturn = true,
                            AcceptsTab = true,
                        };
                        control = memo;
                    }
                    break;
                default:
                    // Unknown control type: skip rather than throw, so
                    // a single bad descriptor doesn't take down the
                    // entire form. The dispatch surface surfaces
                    // "unknown control type" to BTM callers as 20102.
                    return null;
            }

            control.Name = desc.Id ?? string.Empty;
            control.Location = new Point(desc.X, desc.Y);
            control.Size = ClampSize(desc.Width, desc.Height);

            // Anchor property: edges separated by + or space
            // (e.g. "top+bottom+left+right"). Default is top,left.
            string? anchorSpec = GetProp(desc, "anchor");
            if (!string.IsNullOrEmpty(anchorSpec))
            {
                AnchorStyles a = AnchorStyles.None;
                string lower = anchorSpec!.ToLowerInvariant();
                if (lower.Contains("top")) { a |= AnchorStyles.Top; }
                if (lower.Contains("bottom")) { a |= AnchorStyles.Bottom; }
                if (lower.Contains("left")) { a |= AnchorStyles.Left; }
                if (lower.Contains("right")) { a |= AnchorStyles.Right; }
                if (a != AnchorStyles.None) { control.Anchor = a; }
            }

            // Color properties: named colors (e.g. "Blue", "White",
            // "DarkBlue") or hex "#RRGGBB" / "#AARRGGBB".
            string? bgSpec = GetProp(desc, "backcolor");
            if (!string.IsNullOrEmpty(bgSpec))
            {
                System.Drawing.Color? bg = ParseColor(bgSpec!);
                if (bg.HasValue) { control.BackColor = bg.Value; }
            }
            string? fgSpec = GetProp(desc, "forecolor");
            if (!string.IsNullOrEmpty(fgSpec))
            {
                System.Drawing.Color? fg = ParseColor(fgSpec!);
                if (fg.HasValue) { control.ForeColor = fg.Value; }
            }

            // Stock icon: apply to any control type that supports
            // the Image property (Button, Label, PictureBox,
            // CheckBox, RadioButton, LinkLabel, etc.)
            string? stockIconSpec = GetProp(desc, "stockicon");
            if (!string.IsNullOrEmpty(stockIconSpec))
            {
                var icon = Controls.StockIcons.Get(stockIconSpec!);
                if (icon is not null)
                {
                    ApplyStockIcon(control, icon);
                }
            }

            // Event capture: wire WinForms handlers that enqueue
            // FormEvent records for FORMEVENTS consumption. All event
            // wiring is centralized in EventWiringTable -- adding a
            // new event is a single registry entry, not a switch case.
            if (eventQueue is not null)
            {
                EventWiringTable.WireAll(control, formHandle,
                    control.Name, eventQueue);
            }

            return control;
        }

        // -----------------------------------------------------------------
        // LISTVIEW realization
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a <see cref="ListView"/> in Details view from the
        /// LISTVIEW descriptor's prop bag entries:
        /// <list type="bullet">
        ///   <item><description><c>_lv.col.N = name|width|type</c></description></item>
        ///   <item><description><c>_lv.item.N = col0|col1|col2|...</c></description></item>
        ///   <item><description><c>_lv.multiselect = 1</c></description></item>
        ///   <item><description><c>_lv.sort = ColName|asc</c> or <c>|desc</c></description></item>
        /// </list>
        /// Header clicks invoke the type-aware
        /// <see cref="Controls.ListViewItemSorter"/> to re-sort.
        /// </summary>
        private static ListView BuildListView(ControlDescriptor desc)
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = ParseBoolFlag(GetProp(desc, "_lv.multiselect")),
            };

            // Parse columns in order. The descriptor's prop bag stores
            // them as _lv.col.0, _lv.col.1, ... and we walk densely
            // until the next index is missing.
            var colTypes = new System.Collections.Generic.List<string>();
            var colNames = new System.Collections.Generic.List<string>();
            for (int i = 0; ; i++)
            {
                string key = "_lv.col." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!desc.Properties.TryGetValue(key, out string? spec)) { break; }

                string[] parts = SplitLvSpec(spec);
                string name = parts.Length >= 1 ? parts[0] : string.Empty;
                int width = parts.Length >= 2
                    ? ParseIntOrDefault(parts[1], 100)
                    : 100;
                string type = parts.Length >= 3 ? parts[2].Trim() : "text";

                lv.Columns.Add(name, width);
                colNames.Add(name);
                colTypes.Add(type);
            }

            // Parse items in order. Each item value is pipe-separated
            // and may have FEWER cells than there are columns; the
            // missing cells get an empty string.
            for (int i = 0; ; i++)
            {
                string key = "_lv.item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!desc.Properties.TryGetValue(key, out string? row)) { break; }

                string[] cells = SplitLvSpec(row);
                var item = new ListViewItem(cells.Length >= 1 ? cells[0] : string.Empty);
                for (int c = 1; c < cells.Length; c++)
                {
                    item.SubItems.Add(cells[c]);
                }
                lv.Items.Add(item);
            }

            // Wire the type-aware sorter. State carries the active
            // column / direction; ColumnClick mutates it and re-sorts.
            var state = new Controls.ListViewSortState
            {
                ColumnTypes = colTypes.ToArray(),
                SortColumn = -1,
                Ascending = true,
            };
            var sorter = new Controls.ListViewItemSorter(state);
            lv.ListViewItemSorter = sorter;

            lv.ColumnClick += (s, e) =>
            {
                if (e.Column == state.SortColumn)
                {
                    state.Ascending = !state.Ascending;
                }
                else
                {
                    state.SortColumn = e.Column;
                    state.Ascending = true;
                }
                lv.Sort();
            };

            // Apply initial sort spec if present. Format: "ColName|asc"
            // or "ColName|desc" or "0|asc" (column index).
            string? sortSpec = GetProp(desc, "_lv.sort");
            if (!string.IsNullOrEmpty(sortSpec))
            {
                string[] sortParts = SplitLvSpec(sortSpec!);
                int sortCol = ResolveSortColumn(sortParts[0], colNames);
                bool ascending = sortParts.Length < 2 ||
                    !string.Equals(sortParts[1].Trim(), "desc",
                        StringComparison.OrdinalIgnoreCase);
                if (sortCol >= 0)
                {
                    state.SortColumn = sortCol;
                    state.Ascending = ascending;
                    // ListView.Sort() only physically reorders the
                    // managed Items collection once the underlying
                    // win32 control has been created. Force the
                    // handle here so the test-visible order matches
                    // the sort spec even on never-shown forms.
                    _ = lv.Handle;
                    lv.Sort();
                }
            }

            return lv;
        }

        /// <summary>
        /// Split a LISTVIEW pseudo-prop value (column spec, item
        /// row, sort spec) on its field separator. Accepts BOTH
        /// pipe and colon: pipe is the original convention
        /// and is what every xUnit test uses, but bare pipe inside
        /// a TCC `set RC=%@formset[...,Name|280|text]` line gets
        /// eaten by the SET command's pipe parser before the
        /// `%@formset[]` expansion happens. Colon is the safe
        /// residual choice for BTM authors. The plugin accepts
        /// either, with colon taking precedence when both are
        /// present (which would be an authoring bug anyway).
        /// </summary>
        private static string[] SplitLvSpec(string spec)
        {
            if (spec is null) { return Array.Empty<string>(); }
            if (spec.IndexOf(':') >= 0) { return spec.Split(':'); }
            return spec.Split('|');
        }

        /// <summary>
        /// Add a tree node at the given slash-separated path. Creates
        /// intermediate nodes as needed. For a path like "root/child/leaf",
        /// ensures "root" and "root/child" exist, then adds "leaf" under
        /// "root/child".
        /// </summary>
        /// <summary>
        /// Build a tree of ToolStripMenuItems from the descriptor's
        /// Children list. Used by both MENUSTRIP and CONTEXTMENU.
        /// Each descriptor child becomes a ToolStripMenuItem; if the
        /// child's text is "-", a ToolStripSeparator is added instead.
        /// Grandchildren recurse, giving arbitrarily nested submenus.
        /// Click events are wired to the per-form event queue so
        /// @FORMBIND can capture them.
        /// </summary>
        private static void BuildMenuItems(
            ToolStripItemCollection target,
            System.Collections.Generic.List<ControlDescriptor> descriptors,
            int formHandle,
            FormEventQueue? eventQueue)
        {
            foreach (ControlDescriptor itemDesc in descriptors)
            {
                if (itemDesc.Text == "-")
                {
                    target.Add(new ToolStripSeparator());
                    continue;
                }
                var mi = new ToolStripMenuItem(itemDesc.Text)
                {
                    Name = itemDesc.Id ?? string.Empty,
                };
                // Wire click event for leaf items and items with
                // click bindings. Non-leaf items (submenus) fire
                // too so the user can bind to a top-level menu
                // click if they want.
                if (eventQueue is not null)
                {
                    string menuItemId = mi.Name;
                    mi.Click += (s, e) => eventQueue.Enqueue(
                        new FormEvent(formHandle, menuItemId, "click", string.Empty));
                }
                // Recurse into sub-items.
                if (itemDesc.Children.Count > 0)
                {
                    BuildMenuItems(mi.DropDownItems, itemDesc.Children, formHandle, eventQueue);
                }
                target.Add(mi);
            }
        }

        /// <summary>
        /// Add a tree node to a realized TreeView. Public entry point
        /// for live-apply from Plugin.SetControlProperty.
        /// </summary>
        internal static void AddTreeNodeLive(TreeView tv, string path, string text)
        {
            AddTreeNode(tv, path, text);
        }

        private static void AddTreeNode(TreeView tv, string path, string text)
        {
            string[] segments = path.Split('/');
            TreeNodeCollection level = tv.Nodes;
            TreeNode? current = null;
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];
                TreeNode? found = null;
                foreach (TreeNode n in level)
                {
                    if (string.Equals(n.Name, seg, StringComparison.OrdinalIgnoreCase))
                    {
                        found = n;
                        break;
                    }
                }
                if (found is null)
                {
                    // Last segment uses the display text; intermediates
                    // use the segment name as both Name and Text.
                    string nodeText = (i == segments.Length - 1) ? text : seg;
                    found = new TreeNode(nodeText) { Name = seg };
                    level.Add(found);
                }
                current = found;
                level = found.Nodes;
            }
        }

        private static int ResolveSortColumn(
            string token,
            System.Collections.Generic.List<string> colNames)
        {
            if (int.TryParse(token, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int idx))
            {
                return idx >= 0 && idx < colNames.Count ? idx : -1;
            }
            for (int i = 0; i < colNames.Count; i++)
            {
                if (string.Equals(colNames[i], token, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string? GetProp(ControlDescriptor desc, string key)
        {
            return desc.Properties.TryGetValue(key, out string? v) ? v : null;
        }

        /// <summary>
        /// Parse a color string: named color ("Blue", "DarkBlue"),
        /// hex "#RRGGBB" or "#AARRGGBB".
        /// Returns null if the string cannot be parsed.
        /// </summary>
        // Per-form design mode handlers, keyed by form handle.
        // Used by Plugin.DeleteControl to clear the selection
        // overlay when a control is removed.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Controls.DesignModeHandler>
            _designHandlers = new System.Collections.Concurrent.ConcurrentDictionary<int, Controls.DesignModeHandler>();

        /// <summary>
        /// Clear the design-mode selection on the given form
        /// handle. No-op if the form isn't in design mode.
        /// </summary>
        internal static void ClearDesignSelection(int formHandle)
        {
            if (_designHandlers.TryGetValue(formHandle, out var handler))
            {
                handler.ClearSelection();
            }
        }

        /// <summary>
        /// Refresh the design-mode selection overlay and form paint.
        /// Called after programmatic position/size changes to update
        /// the visual selection indicators.
        /// </summary>
        /// <summary>
        /// Update the design grid size on a form in design mode.
        /// </summary>
        internal static void SetDesignGridSize(int formHandle, int size)
        {
            if (_designHandlers.TryGetValue(formHandle, out var handler))
            {
                handler.SetGridSize(size);
            }
        }

        internal static void RefreshDesignOverlay(int formHandle)
        {
            if (_designHandlers.TryGetValue(formHandle, out var handler))
            {
                handler.RefreshOverlay();
            }
        }

        /// <summary>
        /// Apply a stock icon to any control that supports images.
        /// Handles Button, Label, LinkLabel, CheckBox, RadioButton,
        /// PictureBox, and their derivatives.
        /// </summary>
        internal static void ApplyStockIcon(Control control, System.Drawing.Image icon)
        {
            if (control is ButtonBase bb)
            {
                // Button, CheckBox, RadioButton, LinkLabel
                bb.Image = icon;
                bb.TextImageRelation = TextImageRelation.ImageBeforeText;
                bb.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
                bb.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            }
            else if (control is Label lbl)
            {
                lbl.Image = icon;
                lbl.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
                lbl.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            }
            else if (control is PictureBox pb)
            {
                pb.Image = icon;
                pb.SizeMode = PictureBoxSizeMode.CenterImage;
            }
        }

        internal static System.Drawing.Color? ParseColorPublic(string spec)
            => ParseColor(spec);

        /// <summary>
        /// Apply a theme to an already-realized form. Called from
        /// Plugin when <c>@FORMSET[h,.,theme,value]</c> changes the
        /// theme on a live form. Must be called on the GUI thread.
        /// </summary>
        internal static void ApplyThemeLive(
            Form form, FormDescriptor descriptor, string themeValue)
        {
            string theme = (themeValue ?? "system").Trim().ToLowerInvariant();
            if (theme == "dark")
            {
                ApplyThemeColors(form, DarkBg, DarkSurface, DarkFg, true);
            }
            else if (theme == "light")
            {
                ApplyThemeColors(form, LightBg, LightSurface, LightFg, false);
            }
            else
            {
                // system: reset to default colors
                form.BackColor = SystemColors.Control;
                form.ForeColor = SystemColors.ControlText;
                ResetToSystemColors(form);
                // Remove dark title bar
                try
                {
                    int value = 0;
                    int hr = Interop.NativeMethods.DwmSetWindowAttribute(
                        form.Handle,
                        Interop.NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                        ref value, sizeof(int));
                    _ = hr;
                }
                catch { }
            }
            // Re-apply individual color overrides
            ReapplyExplicitColors(form, descriptor.Controls);
            form.Refresh();
        }

        private static void ResetToSystemColors(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is ToolStrip ts)
                {
                    ts.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                    ts.BackColor = SystemColors.Control;
                    ts.ForeColor = SystemColors.ControlText;
                    foreach (ToolStripItem item in ts.Items)
                    {
                        item.BackColor = SystemColors.Control;
                        item.ForeColor = SystemColors.ControlText;
                    }
                }
                else if (c is DataGridView dgv)
                {
                    dgv.BackgroundColor = SystemColors.Window;
                    dgv.ForeColor = SystemColors.WindowText;
                    dgv.GridColor = SystemColors.ControlDark;
                    dgv.DefaultCellStyle.BackColor = SystemColors.Window;
                    dgv.DefaultCellStyle.ForeColor = SystemColors.WindowText;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
                    dgv.RowHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                    dgv.RowHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
                    dgv.EnableHeadersVisualStyles = true;
                }
                else if (c is System.Windows.Forms.Integration.ElementHost eh2
                    && eh2.Child is System.Windows.Controls.RichTextBox wpfRtb2)
                {
                    wpfRtb2.Background = System.Windows.Media.Brushes.White;
                    wpfRtb2.Foreground = System.Windows.Media.Brushes.Black;
                }
                else if (c is TextBoxBase tb)
                {
                    tb.BackColor = SystemColors.Window;
                    tb.ForeColor = SystemColors.WindowText;
                    tb.BorderStyle = BorderStyle.Fixed3D;
                }
                else if (c is LinkLabel ll)
                {
                    ll.BackColor = SystemColors.Control;
                    ll.ForeColor = SystemColors.ControlText;
                    ll.LinkColor = SystemColors.HotTrack;
                    ll.VisitedLinkColor = SystemColors.HotTrack;
                    ll.ActiveLinkColor = System.Drawing.Color.Red;
                }
                else if (c is ScrollBar sb)
                {
                    sb.BackColor = SystemColors.Control;
                    sb.ForeColor = SystemColors.ControlText;
                }
                else if (c is ListControl || c is ListView || c is TreeView)
                {
                    c.BackColor = SystemColors.Window;
                    c.ForeColor = SystemColors.WindowText;
                }
                else if (c is TabControl tc)
                {
                    tc.BackColor = SystemColors.Control;
                    tc.ForeColor = SystemColors.ControlText;
                    foreach (TabPage tp in tc.TabPages)
                    {
                        tp.BackColor = SystemColors.Control;
                        tp.ForeColor = SystemColors.ControlText;
                        ResetToSystemColors(tp);
                    }
                }
                else
                {
                    c.BackColor = SystemColors.Control;
                    c.ForeColor = SystemColors.ControlText;
                }
                if (c.Controls.Count > 0 && c is not TabControl)
                {
                    ResetToSystemColors(c);
                }
            }
        }

        private static System.Drawing.Color? ParseColor(string spec)
        {
            spec = spec.Trim();
            if (spec.StartsWith("#", StringComparison.Ordinal) && spec.Length >= 7)
            {
                try
                {
                    int argb = Convert.ToInt32(spec.Substring(1), 16);
                    if (spec.Length <= 7) { argb |= unchecked((int)0xFF000000); }
                    return System.Drawing.Color.FromArgb(argb);
                }
                catch { return null; }
            }
            // Try named color
            System.Drawing.Color named = System.Drawing.Color.FromName(spec);
            if (named.IsKnownColor || named.A != 0) { return named; }
            return null;
        }

        private static bool ParseBoolFlag(string? value)
        {
            if (string.IsNullOrEmpty(value)) { return false; }
            switch (value!.Trim().ToLowerInvariant())
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

        private static int ParseIntOrDefault(string s, int fallback)
        {
            return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int v)
                ? v
                : fallback;
        }

        private static Size ClampSize(int width, int height)
        {
            // WinForms accepts zero/negative sizes silently and clamps
            // to a minimum on display, but we want predictable values
            // so xUnit tests can assert exact dimensions.
            int w = width <= 0 ? 1 : width;
            int h = height <= 0 ? 1 : height;
            return new Size(w, h);
        }
    }
}
