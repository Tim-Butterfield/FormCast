// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Threading/GuiHostThread.cs
// ==========================
//
// Dedicated STA thread that owns FormCast's WinForms message pump.
// The WinForms host thread is the foundation. Every Form, Control,
// ElementHost, etc. that FormCast creates is owned by THIS thread,
// never by the TCC dispatch thread or the CallbackWorker.
//
// Why a dedicated thread (and not the TCC dispatch thread):
//
// - Apartment. WinForms requires STA. The thread TCC dispatches on
// is whatever TCC happened to be running on; we cannot assume STA.
// - Lifetime. WinForms wants a single owning thread for any given
// Form, with a message loop pumping for as long as the Form is
// alive. The TCC dispatch thread comes and goes per command.
// - Forced shutdown. The forced-shutdown contract requires that
// Plugin.Shutdown force-close every window before returning. With
// a dedicated owner thread we can post ExitThread, join with a
// bounded timeout, and know that no window event handler can fire
// into the unloaded assembly afterward.
//
// Why a single dedicated thread (and not "one thread per Form"):
//
// - WinForms parenting and modal dialogs require all participating
// forms to live on the same UI thread.
// - The cost of one extra background thread is negligible.
// - This matches the standard "WinForms.Application.Run on a worker
// thread" pattern used by every embedded/plugin host that hosts
// WinForms inside a console or service process.
//
// Bring-up sequence:
//
// 1. Caller (Plugin.Initialize) calls Start().
// 2. We spin up a Thread, set ApartmentState.STA before Start.
// 3. Inside the thread proc we create a hidden marshaler Control
// (NEVER shown -- we only use it for BeginInvoke), force its
// handle to be created on this thread, then post a "ready"
// message via the marshaler so it fires AFTER the message
// loop is actually pumping.
// 4. Application.Run(_context) starts the loop. The "ready"
// message fires, signaling the calling thread.
// 5. Start() returns once the signal arrives, with a bounded wait
// so a hung thread proc cannot block Plugin.Initialize forever.
//
// Tear-down sequence (load-bearing for forced shutdown):
//
// 1. Caller (Plugin.Shutdown) calls Stop().
// 2. We BeginInvoke ApplicationContext.ExitThread() onto the GUI
// thread via the marshaler.
// 3. Application.Run returns, the thread proc disposes the
// marshaler and exits.
// 4. Stop() joins with a bounded timeout (default 5 seconds, the
// the forced-shutdown contract budget).
//
// Re-entrancy guard: Invoke from the GUI thread itself runs the
// action inline. Submitting work via Control.Invoke from the same
// thread that owns the control would deadlock waiting for the
// message loop to drain past the current message. We detect that
// case and short-circuit.
//
// What this file deliberately does NOT do (yet):
//
// - Application.EnableVisualStyles / SetCompatibleTextRenderingDefault.
// These touch process-wide state and must only be called once per
// AppDomain, before the first Control is created. The visible-window
// code path decides whether the plugin should call them defensively
// or trust the host.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows.Forms;

namespace FormCast.Threading
{
 /// <summary>
 /// Dedicated STA thread that runs <see cref="Application.Run(ApplicationContext)"/>
 /// and exposes <see cref="Invoke"/> / <see cref="BeginInvoke"/> entry
 /// points for marshaling work onto it. The thread is set to
 /// <see cref="ApartmentState.STA"/> before <see cref="Thread.Start()"/>
 /// so any WinForms or WPF control created on it inherits the right
 /// apartment.
 /// </summary>
 /// <remarks>
 /// <para>Each <see cref="GuiHostThread"/> instance manages exactly one
 /// underlying <see cref="Thread"/>. <see cref="Start"/> is one-shot
 /// per instance: subsequent calls are no-ops. Once <see cref="Stop"/>
 /// has been called the message loop has exited and the underlying
 /// thread is gone -- to "reload" you construct a new instance. The
 /// Plugin lifecycle does this naturally: every <c>plugin /l</c> gets
 /// a fresh <see cref="Plugin"/>, which gets a fresh
 /// <see cref="GuiHostThread"/>.</para>
 /// </remarks>
    internal sealed class GuiHostThread : IDisposable
    {
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim(false);
        private Thread? _thread;
        private ApplicationContext? _context;
        private Control? _marshaler;
        private Exception? _startupException;
        private int _startedFlag;
        private int _stoppedFlag;
        private int _disposedFlag;
        private int _forcedShutdownFlag;

 /// <summary>
 /// Optional name for the GUI thread. Defaults to
 /// <c>"FormCast.GuiHost"</c>.
 /// </summary>
        public string ThreadName { get; set; } = "FormCast.GuiHost";

 /// <summary>
 /// Maximum time <see cref="Start"/> will wait for the message
 /// loop to come up before throwing. Defaults to 5 seconds.
 /// </summary>
        public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(5);

 /// <summary>
 /// Raised on the GUI thread whenever an action submitted via
 /// <see cref="BeginInvoke"/> throws. Synchronous
 /// <see cref="Invoke"/> calls rethrow on the caller instead and
 /// do not raise this event. Subscribers must not block.
 /// </summary>
        public event EventHandler<GuiHostExceptionEventArgs>? UnhandledException;

 /// <summary>
 /// True after <see cref="Start"/> has succeeded and before
 /// <see cref="Stop"/> has been called.
 /// </summary>
        public bool IsRunning => Volatile.Read(ref _startedFlag) == 1
                              && Volatile.Read(ref _stoppedFlag) == 0
                              && (_thread?.IsAlive ?? false);

 /// <summary>
 /// The managed thread id of the GUI thread, or <c>null</c> if
 /// <see cref="Start"/> has not been called yet.
 /// </summary>
        public int? GuiThreadId => _thread?.ManagedThreadId;

 /// <summary>
 /// Set to <c>true</c> by <see cref="SetForcedShutdown"/> right
 /// before the forced-shutdown sequence runs. Form FormClosing
 /// handlers check this flag and suppress user cancellation
 /// when it is set.
 /// </summary>
        public bool ForcedShutdown => Volatile.Read(ref _forcedShutdownFlag) == 1;

 /// <summary>
 /// Spin up the STA thread and block until the message loop is
 /// pumping. Idempotent: subsequent calls are no-ops. Throws
 /// <see cref="InvalidOperationException"/> if the thread proc
 /// fails or the bring-up exceeds <see cref="StartTimeout"/>.
 /// </summary>
        public void Start()
        {
            ThrowIfDisposed();
            if (Interlocked.Exchange(ref _startedFlag, 1) == 1)
            {
                return;
            }

            _thread = new Thread(GuiThreadProc)
            {
                Name = ThreadName,
                IsBackground = true,
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_started.Wait(StartTimeout))
            {
                throw new InvalidOperationException(
                    "GuiHostThread.Start: timed out waiting for the WinForms " +
                    "message loop to come up after " +
                    StartTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " seconds.");
            }
            if (_startupException is not null)
            {
                throw new InvalidOperationException(
                    "GuiHostThread.Start: thread proc faulted during bring-up: " +
                    _startupException.GetType().Name + ": " + _startupException.Message,
                    _startupException);
            }
        }

 /// <summary>
 /// Run <paramref name="work"/> synchronously on the GUI thread
 /// and block the caller until it completes. Exceptions thrown
 /// by the action are wrapped in
 /// <see cref="GuiHostInvocationException"/> and rethrown on the
 /// caller. Calling from the GUI thread itself runs the action
 /// inline (no deadlock).
 /// </summary>
        public void Invoke(Action work)
        {
            if (work is null) { throw new ArgumentNullException(nameof(work)); }
            EnsureRunning();
            if (Environment.CurrentManagedThreadId == _thread?.ManagedThreadId)
            {
                work();
                return;
            }

            Exception? captured = null;
            try
            {
                _marshaler!.Invoke((Action)(() =>
                {
                    try { work(); }
                    catch (Exception ex) { captured = ex; }
                }));
            }
            catch (InvalidOperationException ioe)
            {
 // The marshaler handle was destroyed mid-call (race
 // with Stop). Surface as the same lifecycle error
 // EnsureRunning would throw.
                throw new InvalidOperationException(
                    "GuiHostThread.Invoke: target was torn down during the call.", ioe);
            }
            if (captured is not null)
            {
                throw new GuiHostInvocationException(
                    "An action invoked on the GuiHostThread threw.", captured);
            }
        }

 /// <summary>
 /// Queue <paramref name="work"/> to run asynchronously on the
 /// GUI thread. Returns immediately. Exceptions thrown by the
 /// action are surfaced via <see cref="UnhandledException"/>;
 /// they never crash the GUI thread.
 /// </summary>
        public void BeginInvoke(Action work)
        {
            if (work is null) { throw new ArgumentNullException(nameof(work)); }
            EnsureRunning();
            try
            {
                _marshaler!.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        EventHandler<GuiHostExceptionEventArgs>? h = UnhandledException;
                        if (h is not null)
                        {
                            try { h(this, new GuiHostExceptionEventArgs(ex)); }
                            catch { /* never let a handler kill the GUI thread */ }
                        }
                    }
                }));
            }
            catch (InvalidOperationException ioe)
            {
                throw new InvalidOperationException(
                    "GuiHostThread.BeginInvoke: target was torn down during the call.", ioe);
            }
        }

 /// <summary>
 /// Set the forced-shutdown sentinel. Idempotent. Form
 /// FormClosing handlers consult this flag and suppress user
 /// cancellation while it is set, so Plugin.Shutdown can
 /// guarantee that no FormCast window survives unload.
 /// </summary>
        public void SetForcedShutdown()
        {
            Interlocked.Exchange(ref _forcedShutdownFlag, 1);
        }

 /// <summary>
 /// Stop the GUI thread by posting <see cref="ApplicationContext.ExitThread"/>
 /// onto its message loop, then joining with a bounded timeout.
 /// Idempotent.
 /// </summary>
 /// <param name="joinTimeout">
 /// Maximum wait for the thread to exit cleanly. Defaults to
 /// 5 seconds (the forced-shutdown contract budget).
 /// </param>
 /// <returns>
 /// <c>true</c> if the GUI thread exited within the timeout, or
 /// if it was never started; <c>false</c> if we abandoned the join.
 /// </returns>
        public bool Stop(TimeSpan? joinTimeout = null)
        {
            if (Interlocked.Exchange(ref _stoppedFlag, 1) == 1)
            {
                return true;
            }
            if (_thread is null || !_thread.IsAlive)
            {
                return true;
            }

            try
            {
                Control? marshaler = _marshaler;
                ApplicationContext? context = _context;
                if (marshaler is { IsHandleCreated: true } && context is not null)
                {
                    marshaler.BeginInvoke((Action)(() => context.ExitThread()));
                }
            }
            catch
            {
 // Best effort: if the marshaler is already gone, the
 // thread is already on its way out.
            }

            return _thread.Join(joinTimeout ?? TimeSpan.FromSeconds(5));
        }

 /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) == 1)
            {
                return;
            }
            try { Stop(); } catch { /* swallow */ }
            try { _started.Dispose(); } catch { /* swallow */ }
        }

 // -----------------------------------------------------------------
 // Thread proc
 // -----------------------------------------------------------------

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Top-level catch on a worker thread: any unhandled exception here would tear down the host process. We capture and surface via _startupException so Start() can rethrow on the caller.")]
        private void GuiThreadProc()
        {
            try
            {
 // Build the marshaler control on this thread and force
 // its handle into existence so subsequent BeginInvoke
 // calls have a window handle to post messages to.
                _marshaler = new Control();
                _ = _marshaler.Handle;
                _context = new ApplicationContext();

 // Post the "ready" signal as a message so it fires
 // AFTER Application.Run starts pumping. Without this
 // there is a tiny window where Start() could return
 // before the message loop is actually running.
                _marshaler.BeginInvoke((Action)(() => _started.Set()));

                Application.Run(_context);
            }
            catch (Exception ex)
            {
                _startupException = ex;
                _started.Set();
            }
            finally
            {
                try { _marshaler?.Dispose(); } catch { /* swallow */ }
                try { _context?.Dispose(); } catch { /* swallow */ }
            }
        }

 // -----------------------------------------------------------------
 // Helpers
 // -----------------------------------------------------------------

        private void EnsureRunning()
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _startedFlag) == 0)
            {
                throw new InvalidOperationException(
                    "GuiHostThread.Start has not been called.");
            }
            if (Volatile.Read(ref _stoppedFlag) == 1)
            {
                throw new InvalidOperationException(
                    "GuiHostThread has been stopped; no further work accepted.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposedFlag) == 1)
            {
                throw new ObjectDisposedException(nameof(GuiHostThread));
            }
        }
    }

 /// <summary>
 /// Carries an exception thrown by an action submitted via
 /// <see cref="GuiHostThread.BeginInvoke(Action)"/> up to the
 /// <see cref="GuiHostThread.UnhandledException"/> subscriber.
 /// </summary>
    internal sealed class GuiHostExceptionEventArgs : EventArgs
    {
 /// <summary>The exception that the action threw.</summary>
        public Exception Exception { get; }

 /// <summary>Construct with the captured exception.</summary>
        public GuiHostExceptionEventArgs(Exception ex)
        {
            Exception = ex;
        }
    }

 /// <summary>
 /// Wraps an exception thrown inside a
 /// <see cref="GuiHostThread.Invoke(Action)"/> call so the caller
 /// can distinguish "the action I waited for failed" from "the
 /// invoke itself failed."
 /// </summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public",
        Justification = "GuiHostThread is internal infrastructure; this exception is only thrown to internal callers and InternalsVisibleTo'd to FormCast.Tests.")]
    internal sealed class GuiHostInvocationException : Exception
    {
 /// <summary>Construct with a message and inner exception.</summary>
        public GuiHostInvocationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
