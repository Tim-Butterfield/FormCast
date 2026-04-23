// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Threading/CallbackWorker.cs
// ===========================
//
// Single-thread STA work queue. FormCast's runtime splits across
// three threads:
//
// 1. The TCC dispatch thread (where TCC calls f_FORMxxx and the
// command/lifecycle methods). This is whichever thread the host
// is currently on -- the plugin can NOT keep references that
// assume affinity to it.
//
// 2. The GuiHostThread: a dedicated STA thread running
// Application.Run, owning every WinForms Form FormCast creates.
//
// 3. The CallbackWorker (this file): a dedicated STA thread that
// drains a BlockingCollection<Action> queue. The GuiHostThread
// marshals "user clicked button X" -> "run the BTM @FORMBIND
// callback for X" through this queue.
//
// Why a dedicated worker thread instead of running callbacks directly
// on the GuiHostThread:
//
// - Re-entrancy. A BTM callback that calls back into @FORMSHOW
// (or any other plugin function that touches WinForms) would
// deadlock if it were running on the GUI thread itself.
//
// - Ordering. Two GUI events that fire in quick succession get
// queued in arrival order and dispatched serially. Callbacks
// never overlap, so script authors don't have to think about
// reentrancy in their own code.
//
// - Forced shutdown. The worker can be drained and stopped
// independently of the GUI thread, which lets the // forced-shutdown sequence cleanly cancel pending callbacks
// before tearing down WinForms.
//
// The worker is STA because:
//
// - WPF (used by RICHMEMO via ElementHost in ) requires STA.
// - Some COM components TCC plugins commonly bridge to require STA.
// - The cost of STA on a single-thread queue is negligible.
//
// This file ships in as standalone infrastructure with full
// xUnit coverage. wires it up to a real @FORMCMD test function
// that issues a TCC command via TakeCmd.Command from the worker
// thread. makes it load-bearing for @FORMBIND callbacks.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace FormCast.Threading
{
 /// <summary>
 /// Single-thread STA work queue. Items submitted via
 /// <see cref="Enqueue(Action)"/> or <see cref="SubmitAndWait(Action)"/>
 /// run sequentially on a dedicated background thread, in arrival
 /// order, with no overlap. The worker thread is set to apartment
 /// state STA before <see cref="Thread.Start()"/>.
 /// </summary>
 /// <remarks>
 /// <para>The class is thread-safe: any number of producer threads
 /// may enqueue work concurrently. Exceptions thrown by enqueued
 /// actions are caught and surfaced via the
 /// <see cref="UnhandledException"/> event without killing the
 /// worker thread; an action that throws does NOT prevent the next
 /// queued action from running. <see cref="SubmitAndWait(Action)"/>
 /// is the exception: it rethrows on the calling thread because
 /// the caller is by definition waiting for the result.</para>
 ///
 /// <para>Lifecycle: construct, call <see cref="Start"/> exactly once,
 /// enqueue work, call <see cref="Stop"/> exactly once. Both
 /// <see cref="Start"/> and <see cref="Stop"/> are idempotent on
 /// repeated calls -- the second and subsequent invocations are
 /// no-ops. After <see cref="Stop"/> returns, no further work runs;
 /// <see cref="Enqueue(Action)"/> on a stopped worker throws
 /// <see cref="InvalidOperationException"/>.</para>
 /// </remarks>
    internal sealed class CallbackWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue =
            new BlockingCollection<Action>(new ConcurrentQueue<Action>());
        private readonly object _lifecycleLock = new object();
        private Thread? _thread;
        private int _started;  // 0 = not started, 1 = started
        private int _stopped;  // 0 = running, 1 = stop requested or completed
        private int _disposed; // 0 = live, 1 = disposed

 /// <summary>
 /// Optional name for the worker thread. Defaults to
 /// <c>"FormCast.CallbackWorker"</c>. Useful in debugger
 /// thread lists and crash dumps.
 /// </summary>
        public string ThreadName { get; set; } = "FormCast.CallbackWorker";

 /// <summary>
 /// Raised on the worker thread whenever an enqueued
 /// <see cref="Action"/> throws. The handler runs inline on
 /// the worker, so it must not block. Subscribers should log
 /// and return; the worker will continue draining after the
 /// handler completes.
 /// </summary>
        public event EventHandler<CallbackWorkerExceptionEventArgs>? UnhandledException;

 /// <summary>
 /// Returns <c>true</c> after <see cref="Start"/> has been
 /// called and the worker thread has been spun up.
 /// </summary>
        public bool IsRunning => Volatile.Read(ref _started) == 1
                              && Volatile.Read(ref _stopped) == 0;

 /// <summary>
 /// The managed thread id of the worker, or <c>null</c> if
 /// <see cref="Start"/> has not been called yet. Useful in
 /// asserts that need to verify "this code is running on the
 /// callback worker, not the GUI thread."
 /// </summary>
        public int? WorkerThreadId => _thread?.ManagedThreadId;

 /// <summary>
 /// Spin up the worker thread. The first call sets up the
 /// background STA thread and returns immediately; subsequent
 /// calls are no-ops. Throws if <see cref="Dispose"/> has
 /// already been called.
 /// </summary>
        public void Start()
        {
            ThrowIfDisposed();
 // Idempotent: Interlocked.Exchange returns the previous
 // value; if it was already 1 we have nothing to do.
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            _thread = new Thread(WorkerLoop)
            {
                Name = ThreadName,
                IsBackground = true,
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

 /// <summary>
 /// Enqueue an action for asynchronous execution on the worker
 /// thread. Returns immediately. Throws
 /// <see cref="InvalidOperationException"/> if the worker has
 /// not been started or has already been stopped.
 /// </summary>
 /// <param name="work">The work to run. Must not be <c>null</c>.</param>
        public void Enqueue(Action work)
        {
            if (work is null) { throw new ArgumentNullException(nameof(work)); }
            ThrowIfDisposed();
            if (Volatile.Read(ref _started) == 0)
            {
                throw new InvalidOperationException(
                    "CallbackWorker.Start has not been called.");
            }
            if (Volatile.Read(ref _stopped) == 1)
            {
                throw new InvalidOperationException(
                    "CallbackWorker has been stopped; no further work accepted.");
            }
            try
            {
                _queue.Add(work);
            }
            catch (InvalidOperationException)
            {
 // The queue was completed between our _stopped check
 // and Add. Surface this as the same lifecycle error
 // so callers see one consistent exception type.
                throw new InvalidOperationException(
                    "CallbackWorker has been stopped; no further work accepted.");
            }
        }

 /// <summary>
 /// Enqueue an action and block the calling thread until it
 /// has run to completion (or thrown). Exceptions raised by
 /// <paramref name="work"/> are rethrown on the calling
 /// thread, wrapped in <see cref="CallbackWorkerInvocationException"/>
 /// to preserve the original stack trace.
 /// </summary>
 /// <remarks>
 /// MUST NOT be called from the worker thread itself; doing so
 /// would deadlock waiting for the queue to drain past the
 /// current item. We detect that case and throw immediately
 /// rather than hang.
 /// </remarks>
        public void SubmitAndWait(Action work)
        {
            if (work is null) { throw new ArgumentNullException(nameof(work)); }
            if (Environment.CurrentManagedThreadId == _thread?.ManagedThreadId)
            {
                throw new InvalidOperationException(
                    "SubmitAndWait must not be called from the callback worker thread.");
            }

            using var done = new ManualResetEventSlim(false);
            Exception? captured = null;
            Enqueue(() =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
            if (captured is not null)
            {
                throw new CallbackWorkerInvocationException(
                    "An action submitted via SubmitAndWait threw.", captured);
            }
        }

 /// <summary>
 /// Stop the worker. Marks the queue complete (so the worker
 /// loop exits its <see cref="BlockingCollection{T}.GetConsumingEnumerable()"/>
 /// after draining), then joins the thread with a bounded
 /// timeout. After this returns no further work runs. Idempotent.
 /// </summary>
 /// <param name="joinTimeout">
 /// Maximum time to wait for the worker thread to exit cleanly.
 /// If the timeout elapses we abandon the wait but the worker
 /// thread is a background thread, so it will not block process
 /// exit. Default: 5 seconds (matches the forced
 /// shutdown contract).
 /// </param>
 /// <returns>
 /// <c>true</c> if the worker thread exited within the timeout
 /// (clean shutdown); <c>false</c> if we abandoned the join.
 /// First call after a no-op (already stopped) returns
 /// <c>true</c>.
 /// </returns>
        public bool Stop(TimeSpan? joinTimeout = null)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1)
            {
                return true;
            }
 // Always safe to call CompleteAdding even if Start was
 // never invoked: the worker loop simply won't be there
 // to consume.
            try
            {
                _queue.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
 // Race with Dispose -- treat as already stopped.
                return true;
            }

            if (_thread is null)
            {
                return true;
            }

            TimeSpan timeout = joinTimeout ?? TimeSpan.FromSeconds(5);
            return _thread.Join(timeout);
        }

 /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
 // Make a best-effort stop, but don't throw out of Dispose.
            try { Stop(); } catch { /* swallow */ }
            _queue.Dispose();
        }

 // -----------------------------------------------------------------
 // Worker loop
 // -----------------------------------------------------------------

        private void WorkerLoop()
        {
 // GetConsumingEnumerable blocks until items arrive and
 // exits cleanly when CompleteAdding is called and the
 // queue is drained.
            foreach (Action work in _queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
 // Surface to subscribers but never let it kill
 // the worker thread; the next item still runs.
                    EventHandler<CallbackWorkerExceptionEventArgs>? h = UnhandledException;
                    if (h is not null)
                    {
                        try
                        {
                            h(this, new CallbackWorkerExceptionEventArgs(ex));
                        }
                        catch
                        {
 // The handler itself threw; nothing useful
 // we can do without recursion.
                        }
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(CallbackWorker));
            }
        }
    }

 /// <summary>
 /// Carries an exception thrown by a worker action up to the
 /// <see cref="CallbackWorker.UnhandledException"/> subscriber.
 /// </summary>
    internal sealed class CallbackWorkerExceptionEventArgs : EventArgs
    {
 /// <summary>The exception that the action threw.</summary>
        public Exception Exception { get; }

 /// <summary>Construct with the captured exception.</summary>
        public CallbackWorkerExceptionEventArgs(Exception ex)
        {
            Exception = ex;
        }
    }

 /// <summary>
 /// Wraps an exception thrown inside a
 /// <see cref="CallbackWorker.SubmitAndWait(Action)"/> call so the
 /// caller can distinguish "the action I waited for failed" from
 /// "the wait itself failed."
 /// </summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public",
        Justification = "CallbackWorker is internal infrastructure; this exception is only thrown to internal callers and InternalsVisibleTo'd to FormCast.Tests.")]
    internal sealed class CallbackWorkerInvocationException : Exception
    {
 /// <summary>Construct with a message and inner exception.</summary>
        public CallbackWorkerInvocationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
