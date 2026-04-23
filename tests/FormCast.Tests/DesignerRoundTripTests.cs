// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// round-trip regression for the designer workflow.
 /// Mirrors examples/smoke/formcast-designer-smoke.btm but in
 /// headless xUnit so we get fast, deterministic coverage of
 /// every primitive composing through the FormSerializer
 /// save/load path.
 /// </summary>
 public class DesignerRoundTripTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly string _tempFile;

 public DesignerRoundTripTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 _tempFile = Path.Combine(Path.GetTempPath(),
 "formcast-designer-rt-" + Guid.NewGuid().ToString("N") + ".jsonc");
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 try { if (File.Exists(_tempFile)) { File.Delete(_tempFile); } }
 catch { /* swallow */ }
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm()
 {
 var args = Buf("form,designtest,40,40,400,300");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private void Add(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 private string Set(string handle, string ctrl, string prop, string value)
 {
 var args = Buf($"{handle},{ctrl},{prop},{value}");
 _plugin.f_FORMSET(args);
 return args.ToString();
 }

 private string Get(string handle, string ctrl, string prop)
 {
 var args = Buf($"{handle},{ctrl},{prop}");
 _plugin.f_FORMGET(args);
 return args.ToString();
 }

 private string Save(string handle)
 {
 var args = Buf($"{handle},{_tempFile}");
 _plugin.f_FORMSAVE(args);
 return args.ToString();
 }

 private string Load()
 {
 var args = Buf(_tempFile);
 _plugin.f_FORMLOAD(args);
 return args.ToString();
 }

 // -----------------------------------------------------------------
 // Mirror of formcast-designer-smoke.btm but headless
 // -----------------------------------------------------------------

 [Fact]
 public void Designer_workflow_round_trips_through_save_and_load()
 {
 string h = OpenForm();
 Add(h, "btn1", "BUTTON", x: 10, y: 10, w: 80, h: 24, text: "Button 1");
 Add(h, "btn2", "BUTTON", x: 10, y: 50, w: 80, h: 24, text: "Button 2");

 // Designer-side ops: enable mode, select, move, resize.
 Assert.Equal("0", Set(h, ".", "design_mode", "1"));
 Assert.Equal("0", Set(h, ".", "selected", "btn1"));

 string sel = Get(h, ".", "selected");
 Assert.Equal("btn1", sel);

 Assert.Equal("0", Set(h, sel, "moveby", "15:5"));
 Assert.Equal("0", Set(h, sel, "resizeby", "20:0"));

 Assert.Equal("25:15", Get(h, "btn1", "position"));
 Assert.Equal("100:24", Get(h, "btn1", "size"));

 // Switch to btn2, set absolute position.
 Set(h, ".", "selected", "btn2");
 Assert.Equal("0", Set(h, "btn2", "position", "200:100"));
 Assert.Equal("200:100", Get(h, "btn2", "position"));

 // Save to disk.
 Assert.Equal("0", Save(h));
 Assert.True(File.Exists(_tempFile));

 // Reload from disk and verify all positions survived.
 string h2 = Load();
 Assert.NotEqual(string.Empty, h2);

 Assert.Equal("25:15", Get(h2, "btn1", "position"));
 Assert.Equal("100:24", Get(h2, "btn1", "size"));
 Assert.Equal("200:100", Get(h2, "btn2", "position"));
 }

 // -----------------------------------------------------------------
 // Byte-stable save: serialize twice with the same descriptor
 // and compare the file contents.
 // -----------------------------------------------------------------

 [Fact]
 public void Save_is_byte_stable_for_a_modified_form()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 10, w: 80, h: 24, text: "X");
 Set(h, "btn", "moveby", "5:5");
 Set(h, "btn", "resizeby", "10:2");

 Assert.Equal("0", Save(h));
 string firstWrite = File.ReadAllText(_tempFile);

 // Save again to a fresh path and compare.
 string secondPath = _tempFile + ".second";
 try
 {
 var args = Buf($"{h},{secondPath}");
 _plugin.f_FORMSAVE(args);
 Assert.Equal("0", args.ToString());
 string secondWrite = File.ReadAllText(secondPath);
 Assert.Equal(firstWrite, secondWrite);
 }
 finally
 {
 try { File.Delete(secondPath); } catch { /* swallow */ }
 }
 }

 // -----------------------------------------------------------------
 // Save -> Load -> Save produces a byte-stable file (round trip
 // through deserialization does not perturb the JSON).
 // -----------------------------------------------------------------

 [Fact]
 public void Save_load_save_cycle_is_byte_stable()
 {
 string h = OpenForm();
 Add(h, "btn1", "BUTTON", x: 10, y: 10);
 Add(h, "btn2", "BUTTON", x: 10, y: 50);
 Set(h, "btn1", "position", "25:15");
 Set(h, "btn2", "position", "200:100");

 Save(h);
 string a = File.ReadAllText(_tempFile);

 string h2 = Load();
 string secondPath = _tempFile + ".cycle";
 try
 {
 var args = Buf($"{h2},{secondPath}");
 _plugin.f_FORMSAVE(args);
 string b = File.ReadAllText(secondPath);
 Assert.Equal(a, b);
 }
 finally
 {
 try { File.Delete(secondPath); } catch { /* swallow */ }
 }
 }

 // -----------------------------------------------------------------
 // Diff against expected: pin the descriptor's exact field
 // values after a known sequence of designer ops. Catches any
 // accidental drift in the position/size/moveby/resizeby
 // semantics.
 // -----------------------------------------------------------------

 [Fact]
 public void Designer_op_sequence_lands_at_expected_field_values()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 0, y: 0, w: 50, h: 20);

 Set(h, "btn", "position", "100:200"); // (100, 200)
 Set(h, "btn", "moveby", "10:-5"); // -> (110, 195)
 Set(h, "btn", "size", "300:40"); // (300, 40)
 Set(h, "btn", "resizeby", "-50:10"); // -> (250, 50)

 int seq = int.Parse(h.Split(':')[2],
 System.Globalization.CultureInfo.InvariantCulture);
 FormDescriptor form = _plugin.LookupDescriptor(seq)!;
 ControlDescriptor btn = form.Controls.First(c => c.Id == "btn");

 Assert.Equal(110, btn.X);
 Assert.Equal(195, btn.Y);
 Assert.Equal(250, btn.Width);
 Assert.Equal(50, btn.Height);
 }

 // -----------------------------------------------------------------
 // Simulate-then-save: drive a synthetic click on a button on
 // the designer's target form, then save and reload. The
 // descriptor should be unaffected by the click (events do not
 // mutate descriptor state ).
 // -----------------------------------------------------------------

 [Fact]
 public void Simulate_click_does_not_mutate_descriptor_round_trip()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 10, w: 80, h: 24, text: "Press");
 Set(h, "btn", "moveby", "5:5");

 var simArgs = Buf($"{h},btn,click");
 _plugin.f_FORMSIMULATE(simArgs);
 Assert.Equal("0", simArgs.ToString());

 Save(h);
 string h2 = Load();

 int seq2 = int.Parse(h2.Split(':')[2],
 System.Globalization.CultureInfo.InvariantCulture);
 FormDescriptor reloaded = _plugin.LookupDescriptor(seq2)!;
 ControlDescriptor btn = reloaded.Controls.First(c => c.Id == "btn");
 Assert.Equal(15, btn.X);
 Assert.Equal(15, btn.Y);
 Assert.Equal("Press", btn.Text);
 }

 // -----------------------------------------------------------------
 // Snapshot: the saved JSON for a small known form contains the
 // expected literal field values. Locks the wire shape so any
 // accidental schema change requires an explicit test update.
 // -----------------------------------------------------------------

 [Fact]
 public void Save_output_contains_expected_literal_fields()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 10, w: 80, h: 24, text: "X");
 Set(h, "btn", "position", "100:200");

 Save(h);
 string json = File.ReadAllText(_tempFile);

 // Keys + values that any healthy serializer must emit.
 Assert.Contains("\"type\": \"form\"", json);
 Assert.Contains("\"name\": \"designtest\"", json);
 Assert.Contains("\"id\": \"btn\"", json);
 Assert.Contains("\"type\": \"BUTTON\"", json);
 Assert.Contains("\"x\": 100", json);
 Assert.Contains("\"y\": 200", json);
 Assert.Contains("\"width\": 80", json);
 Assert.Contains("\"text\": \"X\"", json);
 }
 }
}
