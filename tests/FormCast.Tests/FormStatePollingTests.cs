// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Text;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the events_pending bit (32) added to
 /// <c>@FORMSTATE</c>. The bit is the polling target for TCC
 /// <c>ON CONDITION</c> handlers in BTM scripts: when the bit
 /// flips on, the BTM knows the per-form event queue has at
 /// least one record waiting and can drain it via
 /// <c>FORMEVENTS</c> or look it up by control id.
 /// </summary>
 public class FormStatePollingTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormStatePollingTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "poll", int w = 200, int h = 100)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private void AddControl(string handle, string id, string type)
 {
 var args = Buf($"{handle},{id},{type},5,5,100,24,");
 _plugin.f_FORMADD(args);
 }

 private int State(string handle)
 {
 var args = Buf(handle);
 _plugin.f_FORMSTATE(args);
 return int.Parse(args.ToString(), System.Globalization.CultureInfo.InvariantCulture);
 }

 // -----------------------------------------------------------------
 // The events_pending bit starts clear and never flips on its own.
 // -----------------------------------------------------------------

 [Fact]
 public void Events_pending_bit_starts_clear()
 {
 string h = OpenForm("ep1");
 int state = State(h);
 Assert.Equal(0, state & 32);
 }

 // -----------------------------------------------------------------
 // Simulating an event sets the bit.
 // -----------------------------------------------------------------

 [Fact]
 public void Simulate_click_sets_events_pending_bit()
 {
 string h = OpenForm("ep2");
 AddControl(h, "btn", "BUTTON");

 // Before any simulate: queue does not exist or is empty.
 Assert.Equal(0, State(h) & 32);

 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));

 int state = State(h);
 Assert.True((state & 32) != 0,
 "events_pending bit (32) not set after simulate. state=" + state);
 }

 // -----------------------------------------------------------------
 // Draining the queue clears the bit.
 // -----------------------------------------------------------------

 [Fact]
 public void Draining_queue_clears_events_pending_bit()
 {
 string h = OpenForm("ep3");
 int seq = int.Parse(h.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
 AddControl(h, "btn", "BUTTON");

 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));
 Assert.True((State(h) & 32) != 0);

 // Drain the queue.
 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var drained = queue!.DrainAll();
 Assert.Single(drained);

 // Bit should now be clear.
 int state = State(h);
 Assert.Equal(0, state & 32);
 }

 // -----------------------------------------------------------------
 // Multiple events keep the bit set; partial drain via TryDequeue
 // keeps the bit set until the last event is removed.
 // -----------------------------------------------------------------

 [Fact]
 public void Bit_stays_set_until_last_event_is_drained()
 {
 string h = OpenForm("ep4");
 int seq = int.Parse(h.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
 AddControl(h, "btn", "BUTTON");

 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));

 Assert.True((State(h) & 32) != 0);

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);

 Assert.True(queue!.TryDequeue(out _));
 Assert.True((State(h) & 32) != 0); // still 2 left

 Assert.True(queue.TryDequeue(out _));
 Assert.True((State(h) & 32) != 0); // still 1 left

 Assert.True(queue.TryDequeue(out _));
 Assert.Equal(0, State(h) & 32); // empty now
 }

 // -----------------------------------------------------------------
 // FORMCLOSE clears the queue (and the bit) -- though FORMCLOSE
 // also frees the registry handle so subsequent FORMSTATE returns
 // -1, which is also != (X & 32).
 // -----------------------------------------------------------------

 [Fact]
 public void FORMCLOSE_clears_queue_so_bit_is_no_longer_observable()
 {
 string h = OpenForm("ep5");
 AddControl(h, "btn", "BUTTON");

 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));
 Assert.True((State(h) & 32) != 0);

 _plugin.f_FORMCLOSE(Buf(h));

 // After FORMCLOSE, FORMSTATE returns -1 (invalid handle).
 var args = Buf(h);
 _plugin.f_FORMSTATE(args);
 Assert.Equal("-1", args.ToString());
 }

 // -----------------------------------------------------------------
 // The events_pending bit and the existing visibility/enabled
 // bits compose into a single bitmask without collisions.
 // -----------------------------------------------------------------

 [Fact]
 public void Events_pending_bit_composes_with_other_bits()
 {
 // Realize the form so we get bits 1/2 plus 32 from the queue.
 string h = OpenForm("compose");
 int seq = int.Parse(h.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
 AddControl(h, "btn", "BUTTON");

 // Realize via FORMSHOW (headless: realized but not shown,
 // so Visible=false, but Enabled=true).
 _plugin.f_FORMSHOW(Buf(h));
 int s1 = State(h);
 Assert.Equal(0, s1 & 1); // not visible (headless)
 Assert.NotEqual(0, s1 & 2); // enabled
 Assert.Equal(0, s1 & 32); // no events pending

 _plugin.f_FORMSIMULATE(Buf($"{h},btn,click"));
 int s2 = State(h);
 Assert.NotEqual(0, s2 & 2); // still enabled
 Assert.NotEqual(0, s2 & 32); // events pending now
 }
 }
}
