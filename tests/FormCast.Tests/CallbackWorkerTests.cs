// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Tests for <see cref="CallbackWorker"/>: STA apartment, ordering,
    /// exception isolation, lifecycle, and SubmitAndWait semantics.
    /// </summary>
    public class CallbackWorkerTests
    {
        private static CallbackWorker NewStarted()
        {
            var w = new CallbackWorker();
            w.Start();
            return w;
        }

        // ---- Apartment / threading ----

        [Fact]
        public void Worker_thread_is_STA()
        {
            using var w = NewStarted();
            ApartmentState? observed = null;
            w.SubmitAndWait(() => observed = Thread.CurrentThread.GetApartmentState());
            Assert.Equal(ApartmentState.STA, observed);
        }

        [Fact]
        public void Worker_thread_is_a_distinct_thread()
        {
            using var w = NewStarted();
            int callerId = Thread.CurrentThread.ManagedThreadId;
            int observed = -1;
            w.SubmitAndWait(() => observed = Thread.CurrentThread.ManagedThreadId);
            Assert.NotEqual(callerId, observed);
            Assert.Equal(w.WorkerThreadId, observed);
        }

        [Fact]
        public void Enqueue_on_unstarted_throws()
        {
            using var w = new CallbackWorker();
            Assert.Throws<InvalidOperationException>(() => w.Enqueue(() => { }));
        }

        // ---- Ordering ----

        [Fact]
        public void Items_run_in_arrival_order()
        {
            using var w = NewStarted();
            var seen = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                int captured = i;
                w.Enqueue(() => seen.Add(captured));
            }
            // Drain by submitting a sentinel that we wait on.
            w.SubmitAndWait(() => { });

            Assert.Equal(100, seen.Count);
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(i, seen[i]);
            }
        }

        [Fact]
        public void Concurrent_producers_dispatch_serially_no_overlap()
        {
            using var w = NewStarted();
            int active = 0;
            int maxActive = 0;
            var lockObj = new object();

            const int producers = 8;
            const int perProducer = 50;
            var producerThreads = new Thread[producers];
            for (int p = 0; p < producers; p++)
            {
                producerThreads[p] = new Thread(() =>
                {
                    for (int i = 0; i < perProducer; i++)
                    {
                        w.Enqueue(() =>
                        {
                            lock (lockObj)
                            {
                                active++;
                                if (active > maxActive) { maxActive = active; }
                            }
                            Thread.Sleep(1);
                            lock (lockObj) { active--; }
                        });
                    }
                });
                producerThreads[p].Start();
            }
            foreach (var t in producerThreads) { t.Join(); }
            // Drain.
            w.SubmitAndWait(() => { });

            Assert.Equal(1, maxActive);  // never two actions at once
        }

        // ---- Exception isolation ----

        [Fact]
        public void Action_that_throws_does_not_kill_worker()
        {
            using var w = NewStarted();
            var captured = new ConcurrentBag<Exception>();
            w.UnhandledException += (s, e) => captured.Add(e.Exception);

            w.Enqueue(() => throw new InvalidOperationException("boom"));

            // Worker should still be alive: a follow-up SubmitAndWait completes.
            bool ran = false;
            w.SubmitAndWait(() => ran = true);
            Assert.True(ran);

            Assert.Single(captured);
            Assert.IsType<InvalidOperationException>(System.Linq.Enumerable.First(captured));
        }

        [Fact]
        public void SubmitAndWait_rethrows_in_caller()
        {
            using var w = NewStarted();
            var ex = Assert.Throws<CallbackWorkerInvocationException>(() =>
                w.SubmitAndWait(() => throw new ArgumentException("nope")));
            Assert.IsType<ArgumentException>(ex.InnerException);
            Assert.Equal("nope", ex.InnerException!.Message);
        }

        [Fact]
        public void SubmitAndWait_from_worker_thread_throws_immediately()
        {
            using var w = NewStarted();
            // Capture the exception thrown synchronously inside the action;
            // SubmitAndWait should rethrow it (wrapped) on the caller side.
            var outer = Assert.Throws<CallbackWorkerInvocationException>(() =>
                w.SubmitAndWait(() =>
                {
                    // Reentrant SubmitAndWait must throw, not deadlock.
                    w.SubmitAndWait(() => { });
                }));
            Assert.IsType<InvalidOperationException>(outer.InnerException);
        }

        // ---- Lifecycle ----

        [Fact]
        public void Stop_drains_pending_items()
        {
            var w = NewStarted();
            int count = 0;
            for (int i = 0; i < 50; i++)
            {
                w.Enqueue(() => Interlocked.Increment(ref count));
            }
            bool joined = w.Stop();
            Assert.True(joined);
            Assert.Equal(50, count);
            w.Dispose();
        }

        [Fact]
        public void Stop_is_idempotent()
        {
            var w = NewStarted();
            Assert.True(w.Stop());
            Assert.True(w.Stop());  // second call no-ops
            w.Dispose();
        }

        [Fact]
        public void Start_is_idempotent()
        {
            using var w = new CallbackWorker();
            w.Start();
            w.Start();  // no-op, no extra thread
            int? id1 = w.WorkerThreadId;
            int? id2 = null;
            w.SubmitAndWait(() => id2 = Thread.CurrentThread.ManagedThreadId);
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void Enqueue_after_stop_throws()
        {
            var w = NewStarted();
            w.Stop();
            Assert.Throws<InvalidOperationException>(() => w.Enqueue(() => { }));
            w.Dispose();
        }

        [Fact]
        public void Enqueue_null_throws_arg_null()
        {
            using var w = NewStarted();
            Assert.Throws<ArgumentNullException>(() => w.Enqueue(null!));
        }

        [Fact]
        public void Dispose_is_idempotent_and_stops_worker()
        {
            var w = NewStarted();
            w.Dispose();
            w.Dispose();  // second call no-ops
            Assert.False(w.IsRunning);
        }

        [Fact]
        public void Custom_thread_name_is_applied()
        {
            using var w = new CallbackWorker { ThreadName = "FormCast.TestThread" };
            w.Start();
            string? observed = null;
            w.SubmitAndWait(() => observed = Thread.CurrentThread.Name);
            Assert.Equal("FormCast.TestThread", observed);
        }
    }
}
