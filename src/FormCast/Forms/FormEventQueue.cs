// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/FormEventQueue.cs
// =======================
//
// Per-form event capture. WinForms event handlers wired up by
// FormRealizer push FormEvent records into a per-form FormEventQueue.
// The FORMEVENTS streaming command drains the queue and writes
// line-oriented output to the BTM caller via wwriteXP. @FORMBIND
// lets user scripts subscribe to specific events.
//
// Threading model:
// - Enqueue happens on the GUI host thread, since WinForms event
// handlers run on the thread that owns the control.
// - Drain happens on whatever thread calls FORMEVENTS / the test
// code, typically the script thread or an xUnit test thread.
// - ConcurrentQueue<T> handles the cross-thread access without
// external locking.
//
// The queue is intentionally minimal: no bounded capacity, no
// backpressure, no event filtering. It can be hardened once real
// load patterns are known.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// A single event captured from a realized WinForms control.
 /// Records the form handle, the source control id, the event
 /// type (lowercase short name like <c>click</c>, <c>change</c>,
 /// <c>close</c>), an optional value payload (e.g. the new text
 /// for a TextChanged event, or <c>"true"</c>/<c>"false"</c>
 /// for CheckedChanged), and the UTC timestamp at the moment of
 /// enqueue.
 /// </summary>
    internal sealed class FormEvent
    {
 /// <summary>The realized form's registry handle (sequence id).</summary>
        public int FormHandle { get; }

 /// <summary>Source control id (the <c>Name</c> on the WinForms control).
 /// Empty string for form-level events such as <c>close</c>.</summary>
        public string ControlId { get; }

 /// <summary>Lowercase short event name. Recognized values:
 /// <c>click</c>, <c>change</c>, <c>close</c>.</summary>
        public string EventType { get; }

 /// <summary>Optional value payload. Empty for events without
 /// a meaningful value (e.g. <c>click</c>).</summary>
        public string Value { get; }

 /// <summary>UTC timestamp at enqueue.</summary>
        public DateTime TimestampUtc { get; }

        public FormEvent(int formHandle, string controlId, string eventType, string value)
        {
            FormHandle = formHandle;
            ControlId = controlId ?? string.Empty;
            EventType = eventType ?? string.Empty;
            Value = value ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
        }
    }

 /// <summary>
 /// Per-form thread-safe event queue. WinForms handlers on the
 /// GUI thread enqueue records; the FORMEVENTS streaming command
 /// The FORMEVENTS command drains them in FIFO order on the script thread.
 /// </summary>
    internal sealed class FormEventQueue
    {
        private readonly ConcurrentQueue<FormEvent> _queue =
            new ConcurrentQueue<FormEvent>();

 /// <summary>Number of events currently buffered.</summary>
        public int Count => _queue.Count;

 /// <summary>
 /// Binding-dispatch hook. When non-null, this delegate is
 /// invoked synchronously after every successful enqueue, on the
 /// thread that called <see cref="Enqueue"/>. <see cref="Plugin"/>
 /// installs a hook in <c>GetOrRealize</c> that looks up
 /// <c>@FORMBIND</c> entries for the event and schedules the
 /// bound TCC command on the callback worker thread. The hook is
 /// invoked inside a try/catch so a faulting binding cannot
 /// corrupt the queue or break further event capture.
 /// </summary>
        public Action<FormEvent>? OnEnqueue { get; set; }

 /// <summary>Enqueue an event. Safe from any thread.</summary>
        public void Enqueue(FormEvent ev)
        {
            if (ev is null) { throw new ArgumentNullException(nameof(ev)); }
            Internal.PluginLogger.Trace($"EVENT enqueue: {ev.FormHandle} {ev.EventType} {ev.ControlId} {ev.Value}");
            _queue.Enqueue(ev);
            Action<FormEvent>? hook = OnEnqueue;
            if (hook is null) { return; }
            try { hook(ev); }
            catch
            {
 // A faulting binding-dispatch hook must NOT corrupt
 // the queue or break further event capture. The hook
 // itself logs to the marker file before surfacing
 // exceptions, so swallowing here loses no diagnostic
 // information.
            }
        }

 /// <summary>
 /// Drain every buffered event in FIFO order, leaving the
 /// queue empty. The returned list is a snapshot owned by the
 /// caller. Concurrent enqueues during drain are not lost --
 /// any event added after the drain loop exits is simply
 /// returned by the next drain call.
 /// </summary>
        public IReadOnlyList<FormEvent> DrainAll()
        {
            var list = new List<FormEvent>();
            while (_queue.TryDequeue(out FormEvent? ev))
            {
                list.Add(ev);
            }
            return list;
        }

 /// <summary>
 /// Try to dequeue the next event in FIFO order. Returns
 /// <c>true</c> and sets <paramref name="ev"/> on success;
 /// returns <c>false</c> with <paramref name="ev"/> set to
 /// <c>null</c> when the queue is empty.
 /// </summary>
        public bool TryDequeue(out FormEvent? ev)
        {
            return _queue.TryDequeue(out ev);
        }
    }
}
