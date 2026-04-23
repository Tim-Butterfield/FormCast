// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Threading.Tasks;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Unit tests for <see cref="FormEventQueue"/> . Pure
 /// in-process tests; no plugin, no GUI host, no WinForms.
 /// </summary>
 public class FormEventQueueTests
 {
 [Fact]
 public void New_queue_is_empty()
 {
 var q = new FormEventQueue();
 Assert.Equal(0, q.Count);
 Assert.Empty(q.DrainAll());
 }

 [Fact]
 public void Enqueue_increases_count()
 {
 var q = new FormEventQueue();
 q.Enqueue(new FormEvent(1, "btn", "click", string.Empty));
 Assert.Equal(1, q.Count);
 }

 [Fact]
 public void Enqueue_null_throws()
 {
 var q = new FormEventQueue();
 Assert.Throws<ArgumentNullException>(() => q.Enqueue(null!));
 }

 [Fact]
 public void DrainAll_returns_events_in_FIFO_order_and_empties_queue()
 {
 var q = new FormEventQueue();
 q.Enqueue(new FormEvent(1, "a", "click", string.Empty));
 q.Enqueue(new FormEvent(1, "b", "change", "v1"));
 q.Enqueue(new FormEvent(1, "c", "change", "v2"));

 var drained = q.DrainAll();
 Assert.Equal(3, drained.Count);
 Assert.Equal("a", drained[0].ControlId);
 Assert.Equal("b", drained[1].ControlId);
 Assert.Equal("c", drained[2].ControlId);
 Assert.Equal(0, q.Count);
 }

 [Fact]
 public void TryDequeue_returns_false_on_empty()
 {
 var q = new FormEventQueue();
 Assert.False(q.TryDequeue(out var ev));
 Assert.Null(ev);
 }

 [Fact]
 public void TryDequeue_returns_oldest_first()
 {
 var q = new FormEventQueue();
 q.Enqueue(new FormEvent(1, "first", "click", string.Empty));
 q.Enqueue(new FormEvent(1, "second", "click", string.Empty));

 Assert.True(q.TryDequeue(out var a));
 Assert.Equal("first", a!.ControlId);
 Assert.True(q.TryDequeue(out var b));
 Assert.Equal("second", b!.ControlId);
 Assert.False(q.TryDequeue(out var _));
 }

 [Fact]
 public void Concurrent_enqueue_does_not_lose_records()
 {
 var q = new FormEventQueue();
 const int producers = 8;
 const int perProducer = 250;

 Parallel.For(0, producers, p =>
 {
 for (int i = 0; i < perProducer; i++)
 {
 q.Enqueue(new FormEvent(p, "ctrl", "change", i.ToString()));
 }
 });

 Assert.Equal(producers * perProducer, q.Count);
 var drained = q.DrainAll();
 Assert.Equal(producers * perProducer, drained.Count);
 }

 [Fact]
 public void FormEvent_normalizes_null_strings_to_empty()
 {
 var ev = new FormEvent(7, null!, null!, null!);
 Assert.Equal(string.Empty, ev.ControlId);
 Assert.Equal(string.Empty, ev.EventType);
 Assert.Equal(string.Empty, ev.Value);
 Assert.Equal(7, ev.FormHandle);
 }

 [Fact]
 public void FormEvent_timestamp_is_set_at_construction()
 {
 DateTime before = DateTime.UtcNow.AddMilliseconds(-1);
 var ev = new FormEvent(1, "x", "click", string.Empty);
 DateTime after = DateTime.UtcNow.AddMilliseconds(1);
 Assert.InRange(ev.TimestampUtc, before, after);
 }
 }
}
